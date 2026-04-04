using System;
using Godot;
using Godot.Collections;

namespace CurveCanvas.Editor;

/// <summary>
/// Loads a finite authored level (single chunk or macro sequence) and spawns the runtime entities needed to play it.
/// </summary>
public partial class FiniteLevelDirector : Node
{
    [Export(PropertyHint.File, "*.curvecanvas.json, *.curvesequence.json")]
    public string LevelFilePath { get; set; } = string.Empty;

    [Export]
    public NodePath TrackGeneratorPath { get; set; } = new();

    [Export]
    public PackedScene? PlayerScene { get; set; }

    [Export]
    public PackedScene? FinishLineScene { get; set; }

    private TrackMeshGenerator? _trackGenerator;
    private Node3D? _playerInstance;
    private Area3D? _finishLine;

    public override void _Ready()
    {
        CallDeferred(nameof(InitializeLevel));
    }

    private void InitializeLevel()
    {
        if (string.IsNullOrWhiteSpace(LevelFilePath))
        {
            GD.PushWarning("[FiniteLevelDirector] LevelFilePath is not set.");
            return;
        }

        _trackGenerator = ResolveTrackGenerator();
        if (_trackGenerator == null)
        {
            return;
        }

        var curve = _trackGenerator.Curve ?? new Curve3D();
        curve.ClearPoints();
        _trackGenerator.Curve = curve;

        Vector3? startPoint;
        Vector3? endPoint;
        var lowerPath = LevelFilePath.ToLowerInvariant();
        if (lowerPath.EndsWith(".curvesequence.json", StringComparison.Ordinal))
        {
            (startPoint, endPoint) = LoadSequence(levelPath: LevelFilePath, curve);
        }
        else if (lowerPath.EndsWith(".curvecanvas.json", StringComparison.Ordinal))
        {
            (startPoint, endPoint) = LoadSingleChunk(LevelFilePath, curve);
        }
        else
        {
            GD.PushWarning($"[FiniteLevelDirector] Unsupported file type '{LevelFilePath}'.");
            return;
        }

        if (startPoint == null || endPoint == null)
        {
            GD.PushWarning("[FiniteLevelDirector] Level data did not contain enough spline points.");
            return;
        }

        SpawnPlayer(startPoint.Value);
        SpawnFinishLine(endPoint.Value);
    }

    private (Vector3? startPoint, Vector3? endPoint) LoadSingleChunk(string filePath, Curve3D curve)
    {
        var splinePoints = CurveCanvasImporter.ExtractSplineData(filePath);
        if (splinePoints.Count == 0)
        {
            GD.PushError($"[FiniteLevelDirector] Chunk '{filePath}' did not contain any spline data.");
            return (null, null);
        }

        return RuntimeSplineUtility.AppendPoints(curve, splinePoints, skipFirstPoint: false, translation: Vector3.Zero);
    }

    private (Vector3? startPoint, Vector3? endPoint) LoadSequence(string levelPath, Curve3D curve)
    {
        var data = CurveSequenceSerializer.Load(levelPath);
        if (data == null || data.ChunkPaths.Count == 0)
        {
            GD.PushError($"[FiniteLevelDirector] Sequence '{levelPath}' did not list any chunk files.");
            return (null, null);
        }

        Vector3? globalStart = null;
        Vector3? lastAppended = null;

        foreach (var chunkPathVariant in data.ChunkPaths)
        {
            var chunkPath = chunkPathVariant?.ToString();
            if (string.IsNullOrWhiteSpace(chunkPath))
            {
                continue;
            }

            var splinePoints = CurveCanvasImporter.ExtractSplineData(chunkPath);
            if (splinePoints.Count == 0)
            {
                GD.PushWarning($"[FiniteLevelDirector] Chunk '{chunkPath}' was empty.");
                continue;
            }

            var chunkEntrance = RuntimeSplineUtility.ExtractPosition(splinePoints, 0);
            var translation = lastAppended.HasValue ? lastAppended.Value - chunkEntrance : Vector3.Zero;
            var (chunkStart, chunkEnd) = RuntimeSplineUtility.AppendPoints(
                curve,
                splinePoints,
                skipFirstPoint: lastAppended.HasValue,
                translation: translation);

            globalStart ??= chunkStart;
            if (chunkEnd.HasValue)
            {
                lastAppended = chunkEnd;
            }
        }

        return (globalStart, lastAppended);
    }

    private TrackMeshGenerator? ResolveTrackGenerator()
    {
        if (!TrackGeneratorPath.IsEmpty)
        {
            var existing = GetNodeOrNull<TrackMeshGenerator>(TrackGeneratorPath);
            if (existing != null)
            {
                return existing;
            }
            GD.PushWarning($"[FiniteLevelDirector] TrackGeneratorPath '{TrackGeneratorPath}' could not be resolved; creating a runtime instance instead.");
        }

        return CreateTrackGenerator();
    }

    private TrackMeshGenerator CreateTrackGenerator()
    {
        var generator = new TrackMeshGenerator
        {
            Name = "RuntimeTrack",
            Curve = new Curve3D()
        };

        AddChild(generator, true);
        return generator;
    }

    private void SpawnPlayer(Vector3 position)
    {
        if (_playerInstance != null)
        {
            _playerInstance.GlobalPosition = position;
            return;
        }

        Node3D node;
        try
        {
            node = PlayerScene?.Instantiate<Node3D>() ?? new CharacterBody3D();
        }
        catch (Exception ex)
        {
            GD.PushError($"[FiniteLevelDirector] PlayerScene instantiation failed: {ex.Message}");
            node = new CharacterBody3D();
        }

        if (string.IsNullOrWhiteSpace(node.Name))
        {
            node.Name = "PlayerCharacter";
        }

        AddChild(node, true);
        node.GlobalPosition = position;
        _playerInstance = node;
    }

    private void SpawnFinishLine(Vector3 position)
    {
        if (_finishLine != null)
        {
            _finishLine.GlobalPosition = position;
            return;
        }

        Area3D finish;
        try
        {
            finish = FinishLineScene?.Instantiate<Area3D>() ?? CreateDefaultFinishArea();
        }
        catch (Exception ex)
        {
            GD.PushError($"[FiniteLevelDirector] FinishLineScene instantiation failed: {ex.Message}");
            finish = CreateDefaultFinishArea();
        }

        finish.Name = string.IsNullOrWhiteSpace(finish.Name) ? "FinishLine" : finish.Name;
        AddChild(finish, true);
        finish.GlobalPosition = position;
        _finishLine = finish;
    }

    private static Area3D CreateDefaultFinishArea()
    {
        var area = new Area3D
        {
            Monitoring = true,
            Monitorable = true
        };

        var collider = new CollisionShape3D
        {
            Shape = new BoxShape3D
            {
                Size = new Vector3(4f, 4f, 4f)
            }
        };

        area.AddChild(collider, true);
        return area;
    }
}
