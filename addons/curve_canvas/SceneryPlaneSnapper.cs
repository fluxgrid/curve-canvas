using System;
using Godot;

namespace CurveCanvas.Editor;

/// <summary>
/// Locks scenery prefabs to designer-defined parallax planes while matching the track's elevation profile.
/// </summary>
[Tool]
public partial class SceneryPlaneSnapper : Node3D
{
    public const string SceneryGroup = "curve_canvas_scenery_objects";

    private Path3D? _trackPath;
    private CurveCanvasRegistry? _registry;
    private string _objectId = string.Empty;
    private Node3D? _spawnedInstance;
    private float _depthZ = -10f;
    private bool _transformDirty = true;

    [Export]
    public CurveCanvasRegistry? Registry
    {
        get => _registry;
        set
        {
            if (_registry == value)
            {
                return;
            }

            _registry = value;
            RefreshAssetInstance();
        }
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
            RefreshAssetInstance();
        }
    }

    [Export(PropertyHint.Range, "-200,200,0.1")]
    public float DepthZ
    {
        get => _depthZ;
        set
        {
            var sanitized = EnsureNonZeroDepth(value);
            if (Mathf.IsEqualApprox(_depthZ, sanitized))
            {
                return;
            }

            _depthZ = sanitized;
            MarkTransformDirty();
        }
    }

    [Export]
    public int PlaneIndex { get; set; }

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

    public override void _Ready()
    {
        base._Ready();
        AddToGroup(SceneryGroup, persistent: true);
        RefreshAssetInstance();
        SetProcess(Engine.IsEditorHint());
        UpdateSnapTransform();
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
    }

    private void RefreshAssetInstance()
    {
        _spawnedInstance?.QueueFree();
        _spawnedInstance = null;

        if (_registry == null || string.IsNullOrWhiteSpace(_objectId))
        {
            return;
        }

        var prefab = _registry.GetPrefab(_objectId);
        if (prefab == null)
        {
            return;
        }

        if (prefab.Instantiate() is not Node3D nodeInstance)
        {
            GD.PushError($"[SceneryPlaneSnapper] Prefab '{_objectId}' must inherit Node3D.");
            return;
        }

        nodeInstance.Name = $"{_objectId}_Prefab";
        AddChild(nodeInstance);

        if (Engine.IsEditorHint())
        {
            var owner = Owner ?? GetTree()?.CurrentScene;
            if (owner != null)
            {
                nodeInstance.Owner = owner;
            }
        }

        _spawnedInstance = nodeInstance;
        MarkTransformDirty();
    }

    private void UpdateSnapTransform()
    {
        if (_trackPath?.Curve == null)
        {
            _transformDirty = false;
            return;
        }

        var target = GlobalPosition;
        target.Z = _depthZ;

        var planarProbe = new Vector3(target.X, target.Y, 0f);
        var closest = _trackPath.Curve.GetClosestPoint(planarProbe);
        target.Y = closest.Y;

        GlobalPosition = target;
        _transformDirty = false;
    }

    private void MarkTransformDirty()
    {
        _transformDirty = true;
        if (Engine.IsEditorHint())
        {
            SetProcess(true);
        }
    }

    private static float EnsureNonZeroDepth(float value)
    {
        if (Mathf.IsZeroApprox(value))
        {
            return MathF.CopySign(0.01f, value == 0f ? 1f : value);
        }

        return value;
    }
}
