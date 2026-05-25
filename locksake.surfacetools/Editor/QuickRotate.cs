using Sandbox;
using System.Collections.Generic;
using System.Linq;

namespace Editor;

/// <summary>
/// Quick Rotate
///
/// Rotates each selected GameObject around the world Z (up) axis by the
/// editor's current Angle Snap setting.
///
/// Shortcut : Ctrl+R
/// </summary>
public static class QuickRotate
{
	[Shortcut( "SurfaceTools.quick-rotate", "Ctrl+R" )]
	public static void Execute()
	{
		using var scope = SceneEditorSession.Scope();

		if ( EditorScene.Selection.Count == 0 )
			return;

		var gos = EditorScene.Selection.OfType<GameObject>().ToArray();
		if ( gos.Length == 0 )
			return;

		var angleStep = EditorScene.GizmoSettings.AngleSpacing;

		var selectedSet = new HashSet<GameObject>( gos );
		var topLevel = gos.Where( go => !selectedSet.Contains( go.Parent ) ).ToArray();

		topLevel.DispatchPreEdited( nameof( GameObject.LocalRotation ) );

		using ( SceneEditorSession.Active
			.UndoScope( "Quick Rotate Object(s)" )
			.WithGameObjectChanges( topLevel, GameObjectUndoFlags.Properties )
			.Push() )
		{
			var rotDelta = Rotation.FromAxis( Vector3.Up, angleStep );

			foreach ( var go in topLevel )
			{
				go.WorldRotation = rotDelta * go.WorldRotation;
			}
		}

		topLevel.DispatchEdited( nameof( GameObject.LocalRotation ) );
	}
}
