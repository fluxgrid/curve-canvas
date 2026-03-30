using Godot;

namespace CurveCanvas.Editor;

/// <summary>
/// Editor utility that spawns a sandbox scene for quick testing of CurveCanvas tools.
/// </summary>
[Tool]
public partial class GenerateDemoScene : EditorScript
{
	public override void _Run()
	{
		var root = new Node3D
		{
			Name = "CurveCanvasDemo"
		};

		var trackGenerator = new TrackMeshGenerator
		{
			Name = "TrackGenerator"
		};
		var architectCamera = new Camera3D
		{
			Name = "ArchitectCamera"
		};
		var hostCharacter = new CharacterBody3D
		{
			Name = "TestKCC"
		};
		var actionCamera = new Camera3D
		{
			Name = "ActionCamera"
		};
		var stateManager = new CurveCanvasStateManager
		{
			Name = "StateManager"
		};

		// Simple sample curve for the generator so the scene displays geometry immediately.
		var curve = new Curve3D();
		curve.AddPoint(new Vector3(-20f, 0f, 0f));
		curve.AddPoint(new Vector3(0f, 5f, 0f));
		curve.AddPoint(new Vector3(30f, -2f, 0f));
		trackGenerator.Curve = curve;

		// Assemble hierarchy.
		root.AddChild(trackGenerator, true);
		root.AddChild(architectCamera, true);
		root.AddChild(hostCharacter, true);
		hostCharacter.AddChild(actionCamera, true);
		root.AddChild(stateManager, true);

		// Camera placement/orientation.
		architectCamera.Position = new Vector3(0f, 50f, 50f);
		architectCamera.LookAt(Vector3.Zero, Vector3.Up);

		actionCamera.Position = new Vector3(0f, 2f, 5f);
		actionCamera.LookAt(hostCharacter.Position, Vector3.Up);

		// Ensure PackedScene ownership is set for every node.
		AssignOwnerRecursive(root, root);

		// Wire state manager node paths.
		stateManager.ArchitectCameraPath = stateManager.GetPathTo(architectCamera);
		stateManager.ActionCameraPath = stateManager.GetPathTo(actionCamera);
		stateManager.TrackPathNodePath = stateManager.GetPathTo(trackGenerator);
		stateManager.HostCharacterPath = stateManager.GetPathTo(hostCharacter);

		var packedScene = new PackedScene();
		var packError = packedScene.Pack(root);
		if (packError != Error.Ok)
		{
			GD.PushError($"CurveCanvas demo scene pack failed: {packError}");
			root.Free();
			return;
		}

		var saveError = ResourceSaver.Save(packedScene, "res://CurveCanvasDemo.tscn");
		if (saveError != Error.Ok)
		{
			GD.PushError($"CurveCanvas demo scene save failed: {saveError}");
			root.Free();
			return;
		}

		GD.Print("CurveCanvas demo scene generated at res://CurveCanvasDemo.tscn");
		root.Free();
	}

	private static void AssignOwnerRecursive(Node parent, Node owner)
	{
		foreach (Node child in parent.GetChildren())
		{
			child.Owner = owner;
			AssignOwnerRecursive(child, owner);
		}
	}
}
