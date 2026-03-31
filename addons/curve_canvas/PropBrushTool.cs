using System;
using System.Collections.Generic;
using Godot;

namespace CurveCanvas.Editor;

/// <summary>
/// Handles Prop Brush authoring strokes by sampling the active track at fixed intervals and instantiating scenery snappers.
/// </summary>
[Tool]
public partial class PropBrushTool : RefCounted
{
    private readonly List<float> _pendingSamples = new();
    private readonly FastNoiseLite _noise = new();

    private Path3D? _trackPath;
    private Curve3D? _activeCurve;
    private CurveCanvasRegistry? _registry;
    private Node? _spawnParent;
    private string _objectId = string.Empty;

    private float _sampleInterval = 3f;
    private float _offsetJitter = 1f;
    private float _depthPlaneZ = -10f;
    private int _planeIndex;
    private ulong _noiseSeed = 1337;

    private float _curveLength;
    private float _lastEmitOffset;
    private bool _strokeActive;

    public bool IsStrokeActive => _strokeActive;

    public void ConfigureFromTrack(TrackMeshGenerator? track)
    {
        _trackPath = track;
        _activeCurve = track?.Curve;
        _registry = track?.PropBrushRegistry;
        _objectId = track?.PropBrushObjectId ?? string.Empty;
        _sampleInterval = Math.Max(0.1f, track?.PropBrushSampleInterval ?? 3f);
        _depthPlaneZ = SanitizeDepth(track?.PropBrushDepthZ ?? -12f);
        _offsetJitter = Math.Max(0f, track?.PropBrushOffsetJitter ?? 0.75f);
        _planeIndex = track?.PropBrushPlaneIndex ?? 0;
        _noiseSeed = track?.PropBrushNoiseSeed ?? 1337;
        _spawnParent = track?.GetParent() ?? track;

        _noise.Seed = (int)Math.Clamp((long)_noiseSeed, int.MinValue, int.MaxValue);
        _curveLength = _activeCurve?.GetBakedLength() ?? 0f;
        CancelStroke();
    }

    public bool BeginStroke(Vector3 planarPoint)
    {
        if (!CanPaint())
        {
            GD.PushWarning("[PropBrushTool] Cannot start brush stroke; ensure registry, object ID, and track curve are assigned.");
            return false;
        }

        var offset = GetOffsetAlongCurve(planarPoint);
        _pendingSamples.Clear();
        _strokeActive = true;
        _lastEmitOffset = offset;
        AddSample(offset);
        return true;
    }

    public void AccumulateStroke(Vector3 planarPoint)
    {
        if (!_strokeActive || _activeCurve == null)
        {
            return;
        }

        var offset = GetOffsetAlongCurve(planarPoint);
        while (Mathf.Abs(offset - _lastEmitOffset) >= _sampleInterval)
        {
            var direction = MathF.Sign(offset - _lastEmitOffset);
            if (MathF.Abs(direction) <= float.Epsilon)
            {
                break;
            }

            _lastEmitOffset = Mathf.Clamp(_lastEmitOffset + direction * _sampleInterval, 0f, _curveLength);
            AddSample(_lastEmitOffset);

            if ((_lastEmitOffset <= 0f && direction < 0f) || (_lastEmitOffset >= _curveLength && direction > 0f))
            {
                break;
            }
        }
    }

    public void EndStroke(UndoRedo undoRedo)
    {
        if (!_strokeActive)
        {
            return;
        }

        _strokeActive = false;
        SpawnSamplesWithUndo(undoRedo);
        _pendingSamples.Clear();
    }

    public void CancelStroke()
    {
        _strokeActive = false;
        _pendingSamples.Clear();
    }

    private bool CanPaint()
    {
        return _activeCurve != null
            && _registry != null
            && _trackPath != null
            && !string.IsNullOrWhiteSpace(_objectId);
    }

    private float GetOffsetAlongCurve(Vector3 planarPoint)
    {
        if (_activeCurve == null)
        {
            return 0f;
        }

        planarPoint.Z = 0f;
        var offset = CurveCanvasMath.GetClosestOffset(_activeCurve, planarPoint, out _);
        _curveLength = _activeCurve.GetBakedLength();
        return Mathf.Clamp(offset, 0f, _curveLength);
    }

    private void AddSample(float offset)
    {
        if (_pendingSamples.Count > 0 && Mathf.IsEqualApprox(_pendingSamples[^1], offset))
        {
            return;
        }

        _pendingSamples.Add(offset);
    }

    private void SpawnSamplesWithUndo(UndoRedo undoRedo)
    {
        if (_trackPath == null || _trackPath.Curve == null || _registry == null || _spawnParent == null)
        {
            return;
        }

        if (_pendingSamples.Count == 0)
        {
            return;
        }

        var owner = _spawnParent.Owner ?? _spawnParent.GetTree()?.CurrentScene;
        var nodesToSpawn = new List<SceneryPlaneSnapper>(_pendingSamples.Count);

        foreach (var offset in _pendingSamples)
        {
            var snappedPoint = _trackPath.Curve.SampleBaked(offset);
            var jitter = ComputeJitter(offset);
            var snapper = new SceneryPlaneSnapper
            {
                Name = BuildNodeName(_spawnParent, _objectId),
                Registry = _registry,
                ObjectID = _objectId,
                TrackPath = _trackPath,
                DepthZ = _depthPlaneZ,
                PlaneIndex = _planeIndex
            };

            snapper.Position = new Vector3(snappedPoint.X + jitter.X, snappedPoint.Y + jitter.Y, _depthPlaneZ);
            nodesToSpawn.Add(snapper);
        }

        if (nodesToSpawn.Count == 0)
        {
            return;
        }

        undoRedo.CreateAction("Prop Brush Stroke");
        foreach (var snapper in nodesToSpawn)
        {
            undoRedo.AddDoMethod(Callable.From(() => _spawnParent.AddChild(snapper)));
            if (owner != null)
            {
                undoRedo.AddDoMethod(Callable.From(() => snapper.SetOwner(owner)));
            }
            undoRedo.AddUndoMethod(Callable.From(() => _spawnParent.RemoveChild(snapper)));
            undoRedo.AddUndoMethod(Callable.From(() => snapper.QueueFree()));
        }
        undoRedo.CommitAction();
    }

    private Vector2 ComputeJitter(float offset)
    {
        if (_offsetJitter <= Mathf.Epsilon)
        {
            return Vector2.Zero;
        }

        var sample = offset * 0.1f;
        var jitterX = _noise.GetNoise2D(sample, 0f) * _offsetJitter;
        var jitterY = _noise.GetNoise2D(0f, sample) * (_offsetJitter * 0.5f);
        return new Vector2(jitterX, jitterY);
    }

    private static string BuildNodeName(Node parent, string objectId)
    {
        var baseName = $"{objectId}_Scenery";
        if (parent.GetNodeOrNull<Node>(baseName) != null)
        {
            return $"{baseName}_{Guid.NewGuid().ToString()[..8]}";
        }

        return baseName;
    }

    private static float SanitizeDepth(float value)
    {
        if (Mathf.IsZeroApprox(value))
        {
            return MathF.CopySign(0.01f, value == 0f ? 1f : value);
        }

        return value;
    }
}
