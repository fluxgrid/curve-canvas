using Godot;

namespace CurveCanvas.Editor;

/// <summary>
/// Generates interactive spline handles for runtime editing of a Path3D curve.
/// </summary>
public partial class RuntimeSplineHandles : Node3D
{
    public const uint HandleCollisionLayer = 1u << 15;

    private const string HandleGroupName = "CurveCanvasSplineHandle";

    [Export]
    public NodePath PathNode { get; set; } = new("..");

    [Export]
    public float HandleRadius { get; set; } = 0.6f;

    private Path3D? _path;
    private Curve3D? _curve;

    public override void _Ready()
    {
        _path = ResolvePath();
        _curve = _path?.Curve;
        if (_curve != null)
        {
            _curve.Changed += OnCurveChanged;
        }

        RebuildHandles();
    }

    public override void _ExitTree()
    {
        if (_curve != null)
        {
            _curve.Changed -= OnCurveChanged;
        }

        ClearHandles();
    }

    private Path3D? ResolvePath()
    {
        if (!PathNode.IsEmpty)
        {
            return GetNodeOrNull<Path3D>(PathNode);
        }

        return GetParent() as Path3D;
    }

    private void OnCurveChanged()
    {
        RebuildHandles();
    }

    private void RebuildHandles()
    {
        ClearHandles();

        if (_curve == null || _path == null)
        {
            return;
        }

        var owner = GetTree()?.CurrentScene ?? Owner;
        var pointCount = _curve.GetPointCount();
        for (var i = 0; i < pointCount; i++)
        {
            var position = _curve.GetPointPosition(i);
            CreateHandle(i, position, owner);
        }
    }

    private void ClearHandles()
    {
        foreach (Node child in GetChildren())
        {
            child.QueueFree();
        }
    }

    private void CreateHandle(int index, Vector3 position, Node? owner)
    {
        var handleRoot = new Node3D
        {
            Name = $"Handle_{index:D2}",
            Position = position
        };
        handleRoot.SetMeta("point_index", index);
        handleRoot.AddToGroup(HandleGroupName);
        AddChild(handleRoot, true);
        if (owner != null)
        {
            handleRoot.Owner = owner;
        }

        var area = new Area3D
        {
            Name = "PickerArea",
            CollisionLayer = HandleCollisionLayer,
            CollisionMask = 0
        };
        area.SetMeta("point_index", index);
        handleRoot.AddChild(area, true);
        if (owner != null)
        {
            area.Owner = owner;
        }

        var shape = new CollisionShape3D
        {
            Shape = new SphereShape3D
            {
                Radius = HandleRadius
            }
        };
        area.AddChild(shape, true);
        if (owner != null)
        {
            shape.Owner = owner;
        }

        var mesh = new MeshInstance3D
        {
            Name = "Visual",
            Mesh = new SphereMesh
            {
                Radius = HandleRadius * 0.8f,
                Height = HandleRadius * 1.6f
            }
        };
        var material = new StandardMaterial3D
        {
            AlbedoColor = new Color(0.95f, 0.72f, 0.2f, 0.85f),
            Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
            ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded
        };
        mesh.MaterialOverride = material;
        handleRoot.AddChild(mesh, true);
        if (owner != null)
        {
            mesh.Owner = owner;
        }
    }
}
