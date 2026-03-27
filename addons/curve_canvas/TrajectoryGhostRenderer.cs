using System.Collections.Generic;
using Godot;

namespace CurveCanvas.Editor;

/// <summary>
/// Records the host character trajectory during Action mode and draws a ghost line when returning to Architect mode.
/// </summary>
[Tool]
public partial class TrajectoryGhostRenderer : Node3D
{
    [Export]
    public NodePath HostCharacterPath { get; set; } = NodePath.Empty;

    [Export]
    public NodePath StateManagerPath { get; set; } = NodePath.Empty;

    private readonly List<Vector3> _recordedPositions = new();

    private Node3D? _hostCharacter;
    private CurveCanvasStateManager? _stateManager;
    private MeshInstance3D? _ghostMesh;

    public override void _Ready()
    {
        base._Ready();
        EnsureGhostMeshInstance();
        ResolveDependencies();
        SetPhysicsProcess(true);
    }

    public override void _ExitTree()
    {
        base._ExitTree();
        DetachStateManager();
    }

    public override void _PhysicsProcess(double delta)
    {
        if (_stateManager == null || _hostCharacter == null)
        {
            ResolveDependencies();
            return;
        }

        if (_stateManager.CurrentState != CurveCanvasStateManager.EditorState.Action)
        {
            return;
        }

        RecordSample(_hostCharacter.GlobalPosition);
    }

    public void HandleActionStateEntered()
    {
        _recordedPositions.Clear();
        if (_ghostMesh != null)
        {
            _ghostMesh.Mesh = null;
        }
    }

    public void HandleArchitectStateEntered()
    {
        GenerateGhostLine();
    }

    private void RecordSample(Vector3 position)
    {
        if (_recordedPositions.Count > 0 && position.DistanceTo(_recordedPositions[^1]) <= 0.5f)
        {
            return;
        }

        _recordedPositions.Add(position);
    }

    private void GenerateGhostLine()
    {
        if (_ghostMesh == null)
        {
            EnsureGhostMeshInstance();
        }

        if (_ghostMesh == null)
        {
            return;
        }

        if (_recordedPositions.Count < 2)
        {
            _ghostMesh.Mesh = null;
            return;
        }

        var surfaceTool = new SurfaceTool();
        surfaceTool.Begin(Mesh.PrimitiveType.LineStrip);
        surfaceTool.SetColor(new Color(1f, 0.2f, 0.2f));

        foreach (var point in _recordedPositions)
        {
            surfaceTool.AddVertex(point);
        }

        var mesh = surfaceTool.Commit();
        _ghostMesh.Mesh = mesh;
        _ghostMesh.MaterialOverride = BuildGhostMaterial();
    }

    private static StandardMaterial3D BuildGhostMaterial()
    {
        var material = new StandardMaterial3D
        {
            ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
            AlbedoColor = new Color(1f, 0.1f, 0.1f, 1f),
            EmissionEnabled = true,
            Emission = new Color(1f, 0.3f, 0.3f, 1f),
            EmissionEnergyMultiplier = 1.5f
        };
        return material;
    }

    private void ResolveDependencies()
    {
        if ((_hostCharacter == null || !IsInstanceValid(_hostCharacter)) && !HostCharacterPath.IsEmpty)
        {
            _hostCharacter = GetNodeOrNull<Node3D>(HostCharacterPath);
        }

        if ((_stateManager == null || !IsInstanceValid(_stateManager)) && !StateManagerPath.IsEmpty)
        {
            AttachStateManager(GetNodeOrNull<CurveCanvasStateManager>(StateManagerPath));
        }
    }

    private void AttachStateManager(CurveCanvasStateManager? manager)
    {
        if (manager == null)
        {
            return;
        }

        if (_stateManager == manager)
        {
            return;
        }

        DetachStateManager();
        _stateManager = manager;
        _stateManager.StateChanged += OnStateChanged;

        // Ensure we align with the current state immediately.
        OnStateChanged(_stateManager.CurrentState);
    }

    private void DetachStateManager()
    {
        if (_stateManager != null)
        {
            _stateManager.StateChanged -= OnStateChanged;
            _stateManager = null;
        }
    }

    private void OnStateChanged(CurveCanvasStateManager.EditorState state)
    {
        if (state == CurveCanvasStateManager.EditorState.Action)
        {
            HandleActionStateEntered();
        }
        else
        {
            HandleArchitectStateEntered();
        }
    }

    private void EnsureGhostMeshInstance()
    {
        _ghostMesh = GetNodeOrNull<MeshInstance3D>("TrajectoryGhost");
        if (_ghostMesh != null)
        {
            return;
        }

        _ghostMesh = new MeshInstance3D
        {
            Name = "TrajectoryGhost"
        };
        AddChild(_ghostMesh, true);

        if (Engine.IsEditorHint())
        {
            var owner = GetTree()?.EditedSceneRoot ?? GetOwner();
            if (owner != null)
            {
                _ghostMesh.Owner = owner;
            }
        }
    }
}
