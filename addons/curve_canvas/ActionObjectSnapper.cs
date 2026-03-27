using Godot;

namespace CurveCanvas.Editor;

/// <summary>
/// Snaps interactive action objects to the procedural track spline and orients them downhill.
/// </summary>
[Tool]
public partial class ActionObjectSnapper : Node3D
{
    private Path3D? _trackPath;
    private float _curveOffset;
    private bool _transformDirty = true;

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
}
