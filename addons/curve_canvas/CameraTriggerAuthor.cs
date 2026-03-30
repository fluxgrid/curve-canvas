using System;
using Godot;

namespace CurveCanvas.Editor;

/// <summary>
/// Authoring-time trigger volume that aligns itself to the spline and targets a camera for set-piece shots.
/// </summary>
[Tool]
public partial class CameraTriggerAuthor : Area3D
{
    public const string TriggerGroup = "curve_canvas_camera_triggers";

    private CurveCanvasRegistry? _registry;
    private string _objectId = string.Empty;
    private Path3D? _trackPath;
    private NodePath _targetCameraPath = new();
    private float _curveOffset;
    private Vector3 _boxSize = new(8f, 4f, 20f);

    private bool _transformDirty = true;
    private bool _linkDirty = true;

    private CollisionShape3D? _collisionShape;
    private BoxShape3D? _boxShape;
    private MeshInstance3D? _linkMesh;

    [Export]
    public CurveCanvasRegistry? Registry
    {
        get => _registry;
        set => _registry = value;
    }

    [Export]
    public string ObjectID
    {
        get => _objectId;
        set
        {
            var sanitized = value?.Trim() ?? string.Empty;
            if (_objectId == sanitized)
            {
                return;
            }

            _objectId = sanitized;
        }
    }

    [Export]
    public Path3D? TrackPath
    {
        get => _trackPath;
        set
        {
            if (_trackPath == value)
            {
                return;
            }

            _trackPath = value;
            MarkTransformDirty();
        }
    }

    [Export]
    public NodePath TargetCameraPath
    {
        get => _targetCameraPath;
        set
        {
            if (_targetCameraPath == value)
            {
                return;
            }

            _targetCameraPath = value;
            MarkLinkDirty();
        }
    }

    [Export(PropertyHint.Range, "0,10000,0.1")]
    public float CurveOffset
    {
        get => _curveOffset;
        set
        {
            var clamped = Mathf.Clamp(value, 0f, 10000f);
            if (Mathf.IsEqualApprox(_curveOffset, clamped))
            {
                return;
            }

            _curveOffset = clamped;
            MarkTransformDirty();
        }
    }

    [Export]
    public Vector3 BoxSize
    {
        get => _boxSize;
        set
        {
            if (_boxSize.IsEqualApprox(value))
            {
                return;
            }

            _boxSize = new Vector3(Mathf.Max(0.1f, value.X), Mathf.Max(0.1f, value.Y), Mathf.Max(0.1f, value.Z));
            UpdateShapeSize();
        }
    }

    public override void _Ready()
    {
        base._Ready();
        AddToGroup(TriggerGroup, persistent: true);
        EnsureCollisionShape();
        EnsureLinkMesh();
        MarkTransformDirty();
        MarkLinkDirty();
        SetProcess(Engine.IsEditorHint());
    }

    public override void _Process(double delta)
    {
        if (!Engine.IsEditorHint())
        {
            SetProcess(false);
            return;
        }

        if (_transformDirty)
        {
            UpdateSnapTransform();
        }

        if (_linkDirty)
        {
            UpdateLinkVisualization();
        }
    }

    private void MarkTransformDirty()
    {
        _transformDirty = true;
        if (Engine.IsEditorHint())
        {
            SetProcess(true);
        }
    }

    private void MarkLinkDirty()
    {
        _linkDirty = true;
        if (Engine.IsEditorHint())
        {
            SetProcess(true);
        }
    }

    private void UpdateSnapTransform()
    {
        if (_trackPath?.Curve == null)
        {
            _transformDirty = false;
            return;
        }

        var sampled = _trackPath.Curve.SampleBakedWithRotation(_curveOffset);
        var origin = sampled.Origin;
        origin.Z = 0f;
        GlobalTransform = new Transform3D(sampled.Basis, origin);
        _transformDirty = false;
        MarkLinkDirty();
    }

    private void EnsureCollisionShape()
    {
        _collisionShape = GetNodeOrNull<CollisionShape3D>("TriggerShape");
        if (_collisionShape == null)
        {
            _collisionShape = new CollisionShape3D
            {
                Name = "TriggerShape"
            };
            AddChild(_collisionShape, true);
            if (Engine.IsEditorHint())
            {
                var owner = Owner ?? GetTree()?.EditedSceneRoot;
                if (owner != null)
                {
                    _collisionShape.Owner = owner;
                }
            }
        }

        if (_boxShape == null)
        {
            _boxShape = new BoxShape3D();
        }

        _collisionShape.Shape = _boxShape;
        UpdateShapeSize();
    }

    private void UpdateShapeSize()
    {
        if (_boxShape == null)
        {
            return;
        }

        _boxShape.Size = _boxSize;
    }

    private void EnsureLinkMesh()
    {
        _linkMesh = GetNodeOrNull<MeshInstance3D>("TriggerLink");
        if (_linkMesh != null)
        {
            return;
        }

        _linkMesh = new MeshInstance3D
        {
            Name = "TriggerLink"
        };
        AddChild(_linkMesh, true);

        if (Engine.IsEditorHint())
        {
            var owner = Owner ?? GetTree()?.EditedSceneRoot;
            if (owner != null)
            {
                _linkMesh.Owner = owner;
            }
        }
    }

    private void UpdateLinkVisualization()
    {
        EnsureLinkMesh();
        if (_linkMesh == null)
        {
            _linkDirty = false;
            return;
        }

        if (_targetCameraPath.IsEmpty)
        {
            _linkMesh.Mesh = null;
            _linkDirty = false;
            return;
        }

        var targetCamera = GetNodeOrNull<Node3D>(_targetCameraPath);
        if (targetCamera == null)
        {
            _linkMesh.Mesh = null;
            _linkDirty = false;
            return;
        }

        var mesh = new ImmediateMesh();
        mesh.SurfaceBegin(Mesh.PrimitiveType.Lines);
        mesh.SurfaceSetColor(new Color(0.95f, 0.6f, 0.1f));
        mesh.SurfaceAddVertex(Vector3.Zero);
        var localTarget = GlobalTransform.AffineInverse() * targetCamera.GlobalPosition;
        mesh.SurfaceAddVertex(localTarget);
        mesh.SurfaceEnd();

        _linkMesh.Mesh = mesh;
        _linkDirty = false;
    }
}
