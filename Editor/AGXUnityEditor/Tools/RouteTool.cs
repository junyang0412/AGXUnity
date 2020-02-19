﻿using System;
using System.Linq;
using UnityEngine;
using UnityEditor;
using AGXUnity;
using GUI = AGXUnity.Utils.GUI;
using Object = UnityEngine.Object;

namespace AGXUnityEditor.Tools
{
  public class RouteTool<ParentT, NodeT> : CustomTargetTool
    where ParentT : ScriptComponent
    where NodeT : RouteNode, new()
  {
    public Func<float> NodeVisualRadius = null;

    public ParentT Parent
    {
      get
      {
        return Targets[ 0 ] as ParentT;
      }
    }

    public Route<NodeT> Route { get; private set; }

    private NodeT m_selected = null;
    public NodeT Selected
    {
      get { return m_selected; }
      set
      {
        if ( value == m_selected )
          return;

        if ( m_selected != null ) {
          GetFoldoutData( m_selected ).Bool = false;
          SelectedTool.FrameTool.TransformHandleActive = false;
          SelectedTool.FrameTool.InactivateTemporaryChildren();
          // Not selected anymore - enable picking (OnMouseClick callback).
          SelectedTool.Visual.Pickable = true;
        }

        m_selected = value;

        if ( m_selected != null ) {
          GetFoldoutData( m_selected ).Bool = true;
          SelectedTool.FrameTool.TransformHandleActive = true;
          // This flags that we don't expect OnMouseClick when the
          // node is already selected. This solves transform handles
          // completely inside the visual (otherwise Manager will
          // swallow the mouse click and it's not possible to move
          // the nodes).
          SelectedTool.Visual.Pickable = false;
          EditorUtility.SetDirty( Parent );
        }
      }
    }

    public RouteNodeTool SelectedTool { get { return FindActive<RouteNodeTool>( tool => tool.Node == m_selected ); } }

    /// <summary>
    /// Not visual in scene view when the editor is playing or selected in project (asset).
    /// </summary>
    public bool VisualInSceneView { get; private set; }

    public bool DisableCollisionsTool
    {
      get { return GetChild<DisableCollisionsTool>() != null; }
      set
      {
        if ( value && !DisableCollisionsTool ) {
          var disableCollisionsTool = new DisableCollisionsTool( Parent.gameObject );
          AddChild( disableCollisionsTool );
        }
        else if ( !value )
          RemoveChild( GetChild<DisableCollisionsTool>() );
      }
    }

    public RouteTool( Object[] targets )
      : base( targets )
    {
      // , Route<NodeT> route
      Route = (Route<NodeT>)Parent.GetType().GetProperty( "Route", System.Reflection.BindingFlags.Instance |
                                                                   System.Reflection.BindingFlags.Public ).GetGetMethod().Invoke( Parent, null );

      VisualInSceneView = true;
    }

    public override void OnAdd()
    {
      VisualInSceneView = !EditorApplication.isPlaying &&
                          !AssetDatabase.Contains( Parent.gameObject );

      HideDefaultHandlesEnableWhenRemoved();

      if ( VisualInSceneView ) {
        foreach ( var node in Route ) {
          CreateRouteNodeTool( node );
          if ( GetFoldoutData( node ).Bool )
            Selected = node;
        }
      }
    }

    public override void OnSceneViewGUI( SceneView sceneView )
    {
      if ( !VisualInSceneView )
        return;

      // Something happens to our child tools when Unity is performing
      // undo and redo. Try to restore the tools.
      if ( GetChildren().Length == 0 ) {
        m_selected = null;
        foreach ( var node in Route ) {
          if ( GetRouteNodeTool( node ) == null )
            CreateRouteNodeTool( node );
          if ( GetFoldoutData( node ).Bool )
            Selected = node;
        }
      }
    }

    public override void OnPreTargetMembersGUI()
    {
      if ( IsMultiSelect ) {
        if ( VisualInSceneView ) {
          foreach ( var node in Route )
            RemoveChild( GetRouteNodeTool( node ) );
          VisualInSceneView = false;
        }
        return;
      }

      bool toggleDisableCollisions = false;
      var skin = InspectorEditor.Skin;

      InspectorGUI.ToolButtons( InspectorGUI.ToolButtonData.Create( ToolIcon.DisableCollisions,
                                                                    DisableCollisionsTool,
                                                                    "Disable collisions against other objects",
                                                                    () => toggleDisableCollisions = true ) );

      if ( DisableCollisionsTool ) {
        GetChild<DisableCollisionsTool>().OnInspectorGUI();
      }

      if ( !EditorApplication.isPlaying )
        RouteGUI();

      if ( toggleDisableCollisions )
        DisableCollisionsTool = !DisableCollisionsTool;
    }

    protected virtual string GetNodeTypeString( RouteNode node ) { return string.Empty; }
    protected virtual Color GetNodeColor( RouteNode node ) { return Color.yellow; }
    protected virtual void OnPreFrameGUI( NodeT node ) { }
    protected virtual void OnPostFrameGUI( NodeT node ) { }
    protected virtual void OnNodeCreate( NodeT newNode, NodeT refNode, bool addPressed ) { }

    protected RouteNodeTool GetRouteNodeTool( NodeT node )
    {
      return FindActive<RouteNodeTool>( tool => tool.Node == node );
    }

    private void RouteGUI()
    {
      var skin                           = InspectorEditor.Skin;
      var invalidNodeStyle               = new GUIStyle( skin.Label );
      invalidNodeStyle.normal.background = GUI.CreateColoredTexture( 1,
                                                                     1,
                                                                     Color.Lerp( UnityEngine.GUI.color,
                                                                                 Color.red,
                                                                                 0.75f ) );

      var listButtonsStyle = new GUILayoutOption[]
      {
        GUILayout.Width( 1.0f * EditorGUIUtility.singleLineHeight ),
        GUILayout.Height( 0.75f * EditorGUIUtility.singleLineHeight )
      };

      bool addNewPressed        = false;
      bool insertBeforePressed  = false;
      bool insertAfterPressed   = false;
      bool erasePressed         = false;
      NodeT listOpNode          = null;

      Undo.RecordObject( Route, "Route changed" );

      if ( InspectorGUI.Foldout( EditorData.Instance.GetData( Parent,
                                                              "Route",
                                                              entry => { entry.Bool = true; } ),
                                 GUI.MakeLabel( "Route", true ) ) ) {
        Route<NodeT>.ValidatedRoute validatedRoute = Route.GetValidated();
        foreach ( var validatedNode in validatedRoute ) {
          var node = validatedNode.Node;
          using ( InspectorGUI.IndentScope.Single ) {
            if ( validatedNode.Valid )
              GUILayout.BeginVertical();
            else
              GUILayout.BeginVertical( invalidNodeStyle );

            if ( InspectorGUI.Foldout( GetFoldoutData( node ),
                                       GUI.MakeLabel( GetNodeTypeString( node ) + ' ' +
                                                      SelectGameObjectDropdownMenuTool.GetGUIContent( node.Parent ).text,
                                                      !validatedNode.Valid,
                                                      validatedNode.ErrorString ),
                                       newState =>
                                       {
                                         Selected = newState ? node : null;
                                         EditorUtility.SetDirty( Parent );
                                       } ) ) {

              OnPreFrameGUI( node );

              InspectorGUI.HandleFrame( node, 1 );

              OnPostFrameGUI( node );

              GUILayout.BeginHorizontal();
              {
                GUILayout.FlexibleSpace();

                insertBeforePressed = GUILayout.Button( GUI.MakeLabel( GUI.Symbols.ListInsertElementBefore.ToString(),
                                                                        14,
                                                                        false,
                                                                        "Insert a new node before this node" ),
                                                        skin.ButtonMiddle,
                                                        listButtonsStyle ) || insertBeforePressed;
                insertAfterPressed  = GUILayout.Button( GUI.MakeLabel( GUI.Symbols.ListInsertElementAfter.ToString(),
                                                                        14,
                                                                        false,
                                                                        "Insert a new node after this node" ),
                                                        skin.ButtonMiddle,
                                                        listButtonsStyle ) || insertAfterPressed;
                erasePressed        = GUILayout.Button( GUI.MakeLabel( GUI.Symbols.ListEraseElement.ToString(),
                                                                        14,
                                                                        false,
                                                                        "Erase this node" ),
                                                        skin.ButtonMiddle,
                                                        listButtonsStyle ) || erasePressed;

                if ( listOpNode == null && ( insertBeforePressed || insertAfterPressed || erasePressed ) )
                  listOpNode = node;
              }
              GUILayout.EndHorizontal();
            }

            GUILayout.EndVertical();
          }

          if ( GUILayoutUtility.GetLastRect().Contains( Event.current.mousePosition ) &&
               Event.current.type == EventType.MouseDown &&
               Event.current.button == 0 ) {
            Selected = node;
          }
        }

        GUILayout.BeginHorizontal();
        {
          GUILayout.FlexibleSpace();

          addNewPressed = GUILayout.Button( GUI.MakeLabel( GUI.Symbols.ListInsertElementAfter.ToString(),
                                                            14,
                                                            false,
                                                            "Add new node to route" ),
                                            skin.ButtonMiddle,
                                            listButtonsStyle );
          if ( listOpNode == null && addNewPressed )
            listOpNode = Route.LastOrDefault();
        }
        GUILayout.EndHorizontal();
      }

      if ( addNewPressed || insertBeforePressed || insertAfterPressed ) {
        NodeT newRouteNode = null;
        // Clicking "Add" will not copy data from last node.
        newRouteNode = listOpNode != null ?
                         addNewPressed ?
                           RouteNode.Create<NodeT>( null, listOpNode.Position, listOpNode.Rotation ) :
                           RouteNode.Create<NodeT>( listOpNode.Parent, listOpNode.LocalPosition, listOpNode.LocalRotation ) :
                         RouteNode.Create<NodeT>();
        OnNodeCreate( newRouteNode, listOpNode, addNewPressed );

        if ( addNewPressed )
          Route.Add( newRouteNode );
        if ( insertBeforePressed )
          Route.InsertBefore( newRouteNode, listOpNode );
        if ( insertAfterPressed )
          Route.InsertAfter( newRouteNode, listOpNode );

        if ( newRouteNode != null ) {
          CreateRouteNodeTool( newRouteNode );
          Selected = newRouteNode;
        }
      }
      else if ( listOpNode != null && erasePressed ) {
        Selected = null;
        Route.Remove( listOpNode );
      }
    }

    private void CreateRouteNodeTool( NodeT node )
    {
      AddChild( new RouteNodeTool( node,
                                   Parent,
                                   Route,
                                   () => { return Selected; },
                                   ( selected ) => { Selected = selected as NodeT; },
                                   ( n ) => { return Route.Contains( n as NodeT ); },
                                   NodeVisualRadius,
                                   GetNodeColor ) );
    }

    private EditorDataEntry GetData( NodeT node, string identifier, Action<EditorDataEntry> onCreate = null )
    {
      return EditorData.Instance.GetData( Route, identifier + "_" + Route.IndexOf( node ), onCreate );
    }

    private EditorDataEntry GetFoldoutData( NodeT node )
    {
      return GetData( node, "foldout", ( entity ) => { entity.Bool = false; } );
    }
  }
}
