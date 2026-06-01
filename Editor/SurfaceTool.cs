using Sandbox;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;

namespace Editor;

public enum GizmoOriginMode
{
	[Description( "Use the geometric bounding box center as the gizmo pivot" )]
	ObjectCenter,
	[Description( "Use the object's WorldPosition as the gizmo pivot" )]
	WorldPosition
}

[EditorTool( "tools.surface-snap-tool" )]
[Title( "Surface Tool" )]
[Icon( "view_module" )]
[Alias( "surface-tool" )]
[Group( "Scene" )]
[Order( 100 )]
public sealed class SurfaceSnapPositionTool : EditorTool
{
	public static bool RespectBoundingBox { get; set; } = true;
	public static GizmoOriginMode OriginMode { get; set; } = GizmoOriginMode.ObjectCenter;
	public static bool HideColliderGizmos { get; set; }
	public static float AnchorSize { get; set; } = 1f;
	public static float ArrowSize { get; set; } = 1f;

	public static bool UseWorldPositionOrigin => OriginMode == GizmoOriginMode.WorldPosition;

	static bool _settingsLoaded;
	static void LoadSettings()
	{
		if ( _settingsLoaded ) return;
		try
		{
			RespectBoundingBox = Game.Cookies.Get( "surfacetools.respect_bbox", true );
			OriginMode = (GizmoOriginMode)Game.Cookies.Get( "surfacetools.origin_mode", (int)GizmoOriginMode.ObjectCenter );
			HideColliderGizmos = Game.Cookies.Get( "surfacetools.hide_colliders", false );
			AnchorSize = Game.Cookies.Get( "surfacetools.anchor_size", 1f );
			ArrowSize = Game.Cookies.Get( "surfacetools.arrow_size", 1f );
			_settingsLoaded = true;
		}
		catch { }
	}

	public static void SaveSettings()
	{
		Game.Cookies.Set( "surfacetools.respect_bbox", RespectBoundingBox );
		Game.Cookies.Set( "surfacetools.origin_mode", (int)OriginMode );
		Game.Cookies.Set( "surfacetools.hide_colliders", HideColliderGizmos );
		Game.Cookies.Set( "surfacetools.anchor_size", AnchorSize );
		Game.Cookies.Set( "surfacetools.arrow_size", ArrowSize );
	}

	[WideMode] public bool BoundingBox
	{
		get => RespectBoundingBox;
		set { RespectBoundingBox = value; SaveSettings(); }
	}

	[WideMode] public GizmoOriginMode Origin
	{
		get => OriginMode;
		set { OriginMode = value; SaveSettings(); }
	}

	[WideMode] public bool HideColliders
	{
		get => HideColliderGizmos;
		set { HideColliderGizmos = value; SaveSettings(); ApplyColliderGizmoState(); }
	}

	[WideMode, Range( 0.3f, 3f ), Step( 0.1f )] public float AnchorSizeValue
	{
		get => AnchorSize;
		set { AnchorSize = value; SaveSettings(); }
	}

	[WideMode, Range( 0.3f, 3f ), Step( 0.1f )] public float ArrowSizeValue
	{
		get => ArrowSize;
		set { ArrowSize = value; SaveSettings(); }
	}

	readonly Dictionary<GameObject, Transform> _startPoints = new();
	Vector3 _axisDelta;
	Vector3 _grabOffset;
	Vector3 _grabCenter;
	BBox _selectionBBox;
	IDisposable _undoScope;
	HashSet<GameObject> _cachedSelection = new();
	bool _isAxisDragging;
	bool _isSurfaceDragging;
	bool _isFirstSurfaceFrame;

	public SurfaceSnapPositionTool()
	{
		AllowGameObjectSelection = true;
	}

	public override void OnEnabled()
	{
		LoadSettings();
		ApplyColliderGizmoState();
	}

	public override void OnSelectionChanged()
	{
		if ( HideColliderGizmos )
			ApplyColliderGizmoState();
	}

	public override void OnDisabled()
	{
		ResetDrag();
	}

	public override void OnUpdate()
	{
		var gos = Selection.OfType<GameObject>()
			.Where( go => go.GetType() != typeof( Scene ) )
			.ToArray();

		if ( _cachedSelection.Count > 0 )
		{
			var currentSet = new HashSet<GameObject>( gos.Where( go => go.IsValid() ) );
			if ( !_cachedSelection.SetEquals( currentSet ) )
			{
				if ( _cachedSelection.Any( go => go.IsDestroyed ) )
				{
					_cachedSelection.Clear();
					EditorScene.Selection.Clear();
					return;
				}
			}
		}
		_cachedSelection = new HashSet<GameObject>( gos.Where( go => go.IsValid() ) );

		if ( HideColliderGizmos )
			RemoveColliderTools();

		if ( !Gizmo.HasMouseFocus )
			return;

		if ( gos.Length == 0 )
			return;

		var bbox = GetWorldBounds( gos );
		var center = UseWorldPositionOrigin
			? BBox.FromPoints( gos.Select( x => x.WorldPosition ) ).Center
			: bbox.Center;
		var handleRotation = Gizmo.Settings.GlobalSpace
			? Rotation.Identity
			: gos.FirstOrDefault()?.WorldRotation ?? Rotation.Identity;

		if ( !Gizmo.IsLeftMouseDown )
		{
			if ( _isAxisDragging || _isSurfaceDragging )
			{
				ResetDrag();
				_startPoints.Clear();
				_axisDelta = Vector3.Zero;
				return;
			}
			_startPoints.Clear();
			_axisDelta = Vector3.Zero;
		}

		using ( Gizmo.Scope( "SurfaceSnapTool", new Transform( center, handleRotation ) ) )
		{
			Gizmo.Hitbox.DepthBias = 0.01f;

			using ( Gizmo.GizmoControls.PushFixedScale() )
			{
				if ( Gizmo.Settings.GlobalSpace )
					Gizmo.Transform = Gizmo.Transform.WithRotation( Rotation.Identity );

				Gizmo.Draw.IgnoreDepth = true;

				var movement = Vector3.Zero;
				var arrowLength = 14f * ArrowSize;
				var arrowGirth = 3.5f * ArrowSize;
				var arrowOffset = 6f * ArrowSize;

				Gizmo.Draw.Color = Gizmo.Colors.Up;
				if ( Gizmo.Control.Arrow( "up", Vector3.Up, out var upDist, arrowLength, arrowGirth, arrowOffset ) )
					movement += Vector3.Up * upDist;

				Gizmo.Draw.Color = Gizmo.Colors.Left;
				if ( Gizmo.Control.Arrow( "left", Vector3.Left, out var leftDist, arrowLength, arrowGirth, arrowOffset ) )
					movement += Vector3.Left * leftDist;

				Gizmo.Draw.Color = Gizmo.Colors.Forward;
				if ( Gizmo.Control.Arrow( "forward", Vector3.Forward, out var fwdDist, arrowLength, arrowGirth, arrowOffset ) )
					movement += Vector3.Forward * fwdDist;

				movement = handleRotation * movement;

				{
					var camRot = Gizmo.Transform.RotationToLocal( Gizmo.Camera.Rotation );
					using ( Gizmo.Scope( "surface-drag", new Transform( Vector3.Zero, camRot ) ) )
					{
						Gizmo.Hitbox.DepthBias *= 0.4f;

						var anchorRadius = 7f * AnchorSize;
						var circleRadius = 1f * AnchorSize;
						var hoverCircleRadius = 1.1f * AnchorSize;

						Gizmo.Hitbox.BBox( new BBox(
							new Vector3( -0.01f, -anchorRadius, -anchorRadius ),
							new Vector3( 0.01f, anchorRadius, anchorRadius ) ) );

						Gizmo.Draw.Color = Gizmo.IsHovered
							? Color.White.WithAlpha( 1.1f )
							: Color.White.WithAlpha( 0.85f );
						Gizmo.Draw.LineThickness = 2.5f * AnchorSize;
						Gizmo.Draw.LineCircle( 0, Gizmo.IsHovered ? hoverCircleRadius : circleRadius, 12 );

						if ( Gizmo.Pressed.This )
						{
							if ( !_isSurfaceDragging && !_isAxisDragging )
							{
								if ( TryTraceCursor( gos, out var initHit ) )
								{
									_isSurfaceDragging = true;
									_isFirstSurfaceFrame = true;
									_selectionBBox = bbox;
									_grabCenter = center;

									var rawOffset = _grabCenter - initHit.HitPosition;
									var n = initHit.Normal;
									var he = _selectionBBox.Extents;
									var heDist = MathF.Abs( n.x ) * he.x + MathF.Abs( n.y ) * he.y + MathF.Abs( n.z ) * he.z;
									var rawAlongNormal = Vector3.Dot( rawOffset, n );

									if ( RespectBoundingBox && MathF.Abs( rawAlongNormal ) <= heDist + 4f )
										_grabOffset = (rawOffset - n * rawAlongNormal) + n * heDist;
									else
										_grabOffset = n * heDist;

									BeginDrag( gos );
								}
							}
						}
					}
				}

				if ( _isSurfaceDragging && Gizmo.IsLeftMouseDown )
				{
					if ( _isFirstSurfaceFrame )
					{
						_isFirstSurfaceFrame = false;
					}
					else
					{
						SurfaceMoveObjects( gos );
					}
				}

				if ( !movement.IsNearlyZero( 0.0001f ) )
				{
					if ( !_isAxisDragging && !_isSurfaceDragging )
					{
						_isAxisDragging = true;
						BeginDrag( gos );
					}

					if ( _isAxisDragging )
					{
						_axisDelta += movement;
						var snapped = Gizmo.Snap( _axisDelta, movement );

						foreach ( var kvp in _startPoints )
						{
							var go = kvp.Key;
							if ( !go.IsValid() ) continue;
							ApplyTransform( go, kvp.Value.Add( snapped, true ) );
						}
					}
				}
			}
		}
	}

	private void SurfaceMoveObjects( GameObject[] gos )
	{
		if ( !TryTraceCursor( gos, out var tr ) )
			return;

		var targetPos = tr.HitPosition + _grabOffset;

		if ( RespectBoundingBox )
		{
			var n = tr.Normal;
			var he = _selectionBBox.Extents;
			var bboxDist = MathF.Abs( n.x ) * he.x + MathF.Abs( n.y ) * he.y + MathF.Abs( n.z ) * he.z;
			var distFromSurface = Vector3.Dot( targetPos - tr.HitPosition, n );
			if ( distFromSurface < bboxDist )
				targetPos += n * (bboxDist - distFromSurface);

			if ( Gizmo.Settings.SnapToGrid ^ Gizmo.IsCtrlPressed )
			{
				var facePos = targetPos - n * bboxDist;
				facePos = facePos.SnapToGrid( Gizmo.Settings.GridSpacing );
				targetPos = facePos + n * bboxDist;
			}
		}
		else
		{
			if ( Gizmo.Settings.SnapToGrid ^ Gizmo.IsCtrlPressed )
				targetPos = targetPos.SnapToGrid( Gizmo.Settings.GridSpacing );
		}

		var delta = targetPos - _grabCenter;

		foreach ( var kvp in _startPoints )
		{
			var go = kvp.Key;
			if ( !go.IsValid() ) continue;
			ApplyTransform( go, kvp.Value.Add( delta, true ) );
		}
	}

	private void QuickRotateSelected( GameObject[] gos )
	{
		var angleStep = EditorScene.GizmoSettings.AngleSpacing;
		var rotDelta = Rotation.FromAxis( Vector3.Up, angleStep );

		foreach ( var go in gos )
		{
			if ( !go.IsValid() ) continue;
			if ( _startPoints.TryGetValue( go, out var start ) )
			{
				_startPoints[go] = new Transform( go.WorldPosition, rotDelta * start.Rotation, start.Scale );
			}
			go.WorldRotation = rotDelta * go.WorldRotation;
			go.DispatchEdited( nameof( GameObject.LocalRotation ) );
		}
	}

	private void ResetDrag()
	{
		_isAxisDragging = false;
		_isSurfaceDragging = false;
		_isFirstSurfaceFrame = false;
		_undoScope?.Dispose();
		_undoScope = null;
	}

	private static BBox GetWorldBounds( IEnumerable<GameObject> gos )
	{
		var bbox = BBox.FromPoints( gos.Select( x => x.WorldPosition ) );
		foreach ( var go in gos )
			if ( go.IsValid() )
				bbox = bbox.AddBBox( go.GetBounds() );
		return bbox;
	}

	private bool TryTraceCursor( IEnumerable<GameObject> ignore, out SceneTraceResult result )
	{
		var builder = Scene.Trace
			.Ray( Gizmo.CurrentRay, Gizmo.RayDepth )
			.UseRenderMeshes( true, EditorPreferences.BackfaceSelection )
			.WithoutTags( "hidden", "trigger" )
			.UsePhysicsWorld( false );

		foreach ( var go in ignore )
			builder = builder.IgnoreGameObjectHierarchy( go );

		result = builder.Run();
		return result.Hit;
	}

	private void BeginDrag( IEnumerable<GameObject> gos )
	{
		if ( _startPoints.Count > 0 ) return;

		if ( Gizmo.IsShiftPressed )
		{
			_undoScope = SceneEditorSession.Active.UndoScope( "Duplicate Object(s)" ).WithGameObjectCreations().Push();
			DuplicateSelection();

			var clones = Selection.OfType<GameObject>()
				.Where( go => go.GetType() != typeof( Scene ) ).ToArray();
			clones.DispatchPreEdited( nameof( GameObject.LocalPosition ) );

			foreach ( var go in clones )
				if ( go.IsValid() )
					_startPoints[go] = go.WorldTransform;
		}
		else
		{
			_undoScope = SceneEditorSession.Active.UndoScope( "Transform Object(s)" )
				.WithGameObjectChanges( gos, GameObjectUndoFlags.Properties ).Push();
			gos.DispatchPreEdited( nameof( GameObject.LocalPosition ) );

			foreach ( var go in gos )
				if ( go.IsValid() )
					_startPoints[go] = go.WorldTransform;
		}
	}

	private void ApplyTransform( GameObject go, Transform transform )
	{
		if ( !Scene.IsEditor )
		{
			var rb = go.GetComponent<Rigidbody>();
			if ( rb.IsValid() && rb.MotionEnabled )
			{
				rb.SetTargetTransform( transform );
				return;
			}
		}

		go.BreakProceduralBone();
		go.WorldTransform = transform;
		go.DispatchEdited( nameof( GameObject.LocalPosition ) );
	}

	static void ApplyColliderGizmoState()
	{
		foreach ( var t in ColliderTypes )
			EditorScene.GizmoSettings.SetGizmoEnabled( t, !HideColliderGizmos );
	}

	static readonly Type[] ColliderTypes = { typeof(BoxCollider), typeof(SphereCollider), typeof(CapsuleCollider) };

	void RemoveColliderTools()
	{
		if ( Manager?.ComponentTools is not { Count: > 0 } list ) return;
		for ( int i = list.Count - 1; i >= 0; i-- )
		{
			var tool = list[i];
			if ( tool is null ) continue;
			if ( IsColliderTool( tool.GetType() ) )
			{
				list.RemoveAt( i );
				tool.Dispose();
			}
		}
	}

	static bool IsColliderTool( Type type )
	{
		while ( type is not null && type != typeof( EditorTool ) )
		{
			if ( type.IsGenericType && type.GetGenericTypeDefinition() == typeof( EditorTool<> ) )
			{
				var componentType = type.GetGenericArguments()[0];
				return componentType.IsAssignableTo( typeof( Collider ) );
			}
			type = type.BaseType;
		}
		return false;
	}

	public override bool HasBoxSelectionMode() => true;

	protected override void OnBoxSelect( Frustum frustum, Rect screenRect, bool isFinal )
	{
		bool removing = Gizmo.IsCtrlPressed;
		bool appending = Gizmo.IsShiftPressed;

		var inBox = new HashSet<GameObject>();

		foreach ( var mr in Scene.GetAllComponents<ModelRenderer>() )
		{
			if ( !frustum.IsInside( mr.Bounds, true ) )
				continue;
			inBox.Add( mr.GameObject );
		}

		foreach ( var go in Scene.GetAllObjects( true ) )
		{
			if ( inBox.Contains( go ) ) continue;
			if ( !go.HasGizmoHandle ) continue;
			if ( !frustum.IsInside( go.WorldPosition ) )
				continue;
			inBox.Add( go );
		}

		foreach ( var go in inBox )
		{
			if ( removing )
				Selection.Remove( go );
			else if ( !Selection.Contains( go ) )
				Selection.Add( go );
		}

		if ( !removing && !appending )
		{
			var toRemove = new List<object>();
			foreach ( var obj in Selection )
			{
				if ( obj is GameObject go && !inBox.Contains( go ) )
					toRemove.Add( obj );
			}
			foreach ( var obj in toRemove )
				Selection.Remove( obj );
		}
	}

	public override Widget CreateToolSidebar()
	{
		return new SurfaceSnapSidebar( this );
	}

	public class SurfaceSnapSidebar : ToolSidebarWidget
	{
		readonly SurfaceSnapPositionTool _tool;

		public SurfaceSnapSidebar( SurfaceSnapPositionTool tool )
		{
			_tool = tool;
			var so = tool.GetSerialized();

			AddTitle( "Object Selection", "layers" );

			{
				var group = AddGroup( "Move Mode" );
				group.Spacing = 4;

				var modeRow = group.AddRow();
				modeRow.Spacing = 4;
				CreateButton( "Move/Position", "control_camera", null, () => { }, true, modeRow, active: true );

				AddPropertyRow( group, so, nameof( BoundingBox ), "Bounding Box",
					"Offset objects so their bounding box sits on the surface" );

				var originLabel = new Label( "Anchor Point" );
				originLabel.SetStyles( "color: #888; font-size: 11px; margin-left: 4px; margin-top: 6px;" );
				originLabel.ToolTip = "Choose whether to pivot on the oobject's WorldPosition or its geometric bounding box center";
				group.Add( originLabel );

				var originControl = ControlWidget.Create( so.GetProperty( nameof( Origin ) ) );
				originControl.ToolTip = "Gizmo pivot: World Position or geometric bounding box center";
				group.Add( originControl );
			}

			{
				var group = AddGroup( "Gizmos" );
				group.Spacing = 6;

				AddPropertyRow( group, so, nameof( HideColliders ), "Hide Collider Gizmos",
					"Hide the persistent scaling gizmos on collider components" );
				var anchorLabel = new Label( "Anchor Size" );
				anchorLabel.SetStyles( "color: #888; font-size: 11px; margin-left: 4px; margin-top: 6px;" );
				anchorLabel.ToolTip = "Adjust the size of the center anchor gizmo";
				group.Add( anchorLabel );
				group.Add( ControlWidget.Create( so.GetProperty( nameof( AnchorSizeValue ) ) ) );
				var arrowLabel = new Label( "Arrow Size" );
				arrowLabel.SetStyles( "color: #888; font-size: 11px; margin-left: 4px; margin-top: 6px;" );
				arrowLabel.ToolTip = "Adjust the size of the axis arrows gizmo";
				group.Add( arrowLabel );
				group.Add( ControlWidget.Create( so.GetProperty( nameof( ArrowSizeValue ) ) ) );
			}

			{
				var group = AddGroup( "Operations" );
				var row = group.AddRow();
				row.Spacing = 4;
				CreateButton( "Quick Rotate", "rotate_90_degrees_cw", "SurfaceTools.quick-rotate", QuickRotate.Execute, true, row );
			}

			Layout.AddStretchCell();
		}

		static void AddPropertyRow( Layout group, SerializedObject so, string propertyName, string label, string tooltip )
		{
			var row = group.AddRow();
			row.Spacing = 6;
			row.Margin = new Sandbox.UI.Margin( 4, 0, 0, 0 );

			var lbl = new Label( label );
			lbl.SetStyles( "vertical-align: middle;" );
			lbl.ToolTip = tooltip;
			row.Add( lbl, 1 );

			var control = ControlWidget.Create( so.GetProperty( propertyName ) );
			control.ToolTip = tooltip;
			row.Add( control );
		}
	}

	[Shortcut( "tools.surface-snap-tool", "Y", typeof( SceneViewWidget ) )]
	public static void ActivateTool()
	{
		EditorToolManager.SetTool( nameof( SurfaceSnapPositionTool ) );
	}

	[Shortcut( "surface.quick-rotate", "R", typeof( SceneViewWidget ) )]
	public void QuickRotateShortcut()
	{
		using var scope = SceneEditorSession.Scope();
		var gos = EditorScene.Selection.OfType<GameObject>().Where( go => go.GetType() != typeof( Scene ) ).ToArray();
		if ( gos.Length == 0 ) return;

		var selectedSet = new HashSet<GameObject>( gos );
		var topLevel = gos.Where( go => !selectedSet.Contains( go.Parent ) ).ToArray();
		var isDragging = _isAxisDragging || _isSurfaceDragging;

		if ( !isDragging )
		{
			topLevel.DispatchPreEdited( nameof( GameObject.LocalRotation ) );
			using ( SceneEditorSession.Active.UndoScope( "Quick Rotate Object(s)" )
				.WithGameObjectChanges( topLevel, GameObjectUndoFlags.Properties ).Push() )
			{
				QuickRotateSelected( topLevel );
			}
			topLevel.DispatchEdited( nameof( GameObject.LocalRotation ) );
		}
		else
		{
			QuickRotateSelected( topLevel );
		}
	}
}
