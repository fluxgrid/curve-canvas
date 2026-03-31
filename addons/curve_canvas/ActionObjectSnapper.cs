using System;
using Godot;

namespace CurveCanvas.Editor;

/// <summary>
/// Snaps interactive action objects to the procedural track spline and orients them downhill.
/// </summary>
[Tool]
public partial class ActionObjectSnapper : Node3D
{
    public const string ActionObjectGroup = "curve_canvas_action_objects";

    public enum SpecialObjectRole
    {
        None,
        SpawnPoint,
        GoalLine
    }

    private Path3D? _trackPath;
    private float _curveOffset;
    private bool _transformDirty = true;
    private CurveCanvasRegistry? _registry;
    private string _objectId = string.Empty;
    private Node3D? _spawnedInstance;
    private SpecialObjectRole _specialRole = SpecialObjectRole.None;

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
            UpdateSpecialRoleFromId();
        }
    }

    [Export(PropertyHint.Enum, "None,SpawnPoint,GoalLine")]
    public SpecialObjectRole SpecialRole
    {
        get => _specialRole;
        set => _specialRole = value;
    }

    [Export]
    public bool AutoDetectSpecialRole { get; set; } = true;

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

    public override void _Ready()
    {
        base._Ready();
        AddToGroup(ActionObjectGroup, persistent: true);
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

    private void MarkTransformDirty()
    {
        _transformDirty = true;
        if (Engine.IsEditorHint())
        {
            SetProcess(true);
        }
    }

    private void UpdateSnapTransform()
    {
        if (!Engine.IsEditorHint())
        {
            _transformDirty = false;
            return;
        }

        if (_trackPath?.Curve == null)
        {
            _transformDirty = false;
            return;
        }

        var sampled = _trackPath.Curve.SampleBakedWithRotation(_curveOffset);
        var snappedOrigin = sampled.Origin;
        snappedOrigin.Z = 0f;

        GlobalTransform = new Transform3D(sampled.Basis, snappedOrigin);
        _transformDirty = false;
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
            GD.PushError($"ActionObjectSnapper requires prefab '{_objectId}' to inherit Node3D.");
            return;
        }

        nodeInstance.Name = $"{_objectId}_Prefab";
        AddChild(nodeInstance);
        if (Engine.IsEditorHint())
        {
            var owner = Owner ?? GetTree()?.CurrentScene;
            nodeInstance.Owner = owner;
        }

        _spawnedInstance = nodeInstance;
        MarkTransformDirty();
    }

    private void UpdateSpecialRoleFromId()
    {
        if (!AutoDetectSpecialRole)
        {
            return;
        }

        if (string.Equals(_objectId, "SpawnPoint", StringComparison.OrdinalIgnoreCase))
        {
            _specialRole = SpecialObjectRole.SpawnPoint;
        }
        else if (string.Equals(_objectId, "GoalLine", StringComparison.OrdinalIgnoreCase))
        {
            _specialRole = SpecialObjectRole.GoalLine;
        }
        else
        {
            _specialRole = SpecialObjectRole.None;
        }
    }
}
