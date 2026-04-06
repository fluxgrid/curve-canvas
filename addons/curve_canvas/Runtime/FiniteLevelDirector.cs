using System;
using CurveCanvas.AuthoringCore;
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
    public NodePath EndlessDirectorPath { get; set; } = new("../EndlessLevelDirector");

    [Export]
    public bool AutoLoadOnReady { get; set; } = true;

    [Export]
    public PackedScene? PlayerScene { get; set; }

    [Export]
    public PackedScene? FinishLineScene { get; set; }

    private TrackMeshGenerator? _trackGenerator;
    private Node3D? _playerInstance;
    private Area3D? _finishLine;
    private EndlessLevelDirector? _endlessDirector;
    private bool _isEndlessLevel;

    public override void _Ready()
    {
        if (!EndlessDirectorPath.IsEmpty)
        {
            _endlessDirector = GetNodeOrNull<EndlessLevelDirector>(EndlessDirectorPath);
        }

        if (AutoLoadOnReady)
        {
            var initialPath = LevelFilePath;
            if (RuntimeLevelSession.TryConsumePendingLevel(out var pendingPath))
            {
                initialPath = pendingPath;
            }

            if (!string.IsNullOrWhiteSpace(initialPath))
            {
                LoadLevel(initialPath);
            }
        }
    }

    public void LoadLevel(string levelPath)
    {
        if (string.IsNullOrWhiteSpace(levelPath))
        {
            GD.PushWarning("[FiniteLevelDirector] Cannot load an empty level path.");
            return;
        }

        _trackGenerator = ResolveTrackGenerator();
        if (_trackGenerator?.Curve == null)
        {
            return;
        }

        LevelFilePath = levelPath;
        _isEndlessLevel = IsEndlessLevel(levelPath);
        if (_isEndlessLevel)
        {
            if (TryStartEndlessLevel())
            {
                return;
            }

            _isEndlessLevel = false;
        }

        var curve = _trackGenerator.Curve;
        curve.ClearPoints();

        Vector3? startPoint;
        Vector3? endPoint;
        var lowerPath = levelPath.ToLowerInvariant();
        if (lowerPath.EndsWith(".curvesequence.json", StringComparison.Ordinal))
        {
            (startPoint, endPoint) = LoadSequence(levelPath: levelPath, curve);
        }
        else if (lowerPath.EndsWith(".curvecanvas.json", StringComparison.Ordinal))
        {
            (startPoint, endPoint) = LoadSingleChunk(levelPath, curve);
        }
        else
        {
            GD.PushWarning($"[FiniteLevelDirector] Unsupported file type '{levelPath}'.");
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

    private bool IsEndlessLevel(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return false;
        }

        var lower = path.ToLowerInvariant();
        if (lower.EndsWith(".curvesequence.json", StringComparison.Ordinal))
        {
            var sequence = CurveSequenceSerializer.Load(path);
            if (sequence == null || sequence.ChunkPaths.Count == 0)
            {
                return false;
            }

            foreach (var chunkVariant in sequence.ChunkPaths)
            {
                var chunkPath = chunkVariant?.ToString();
                if (string.IsNullOrWhiteSpace(chunkPath))
                {
                    continue;
                }

                if (IsEndlessLevel(chunkPath))
                {
                    return true;
                }

                // If one chunk says finite, keep checking others just in case
            }

            return false;
        }

        var metadata = CurveCanvasImporter.ExtractMetadata(path);
        return metadata?.LevelMode?.Equals("Endless", StringComparison.OrdinalIgnoreCase) == true;
    }

    private bool TryStartEndlessLevel()
    {
        if (_endlessDirector == null)
        {
            GD.PushWarning("[FiniteLevelDirector] Level marked Endless but no EndlessLevelDirector is assigned.");
            return false;
        }

        _trackGenerator?.Curve?.ClearPoints();
        var spawn = Vector3.Zero;
        _endlessDirector.ResetStream(spawn);
        _endlessDirector.ProcessEndlessGeneration(spawn.X);
        SpawnPlayer(spawn);
        RemoveFinishLine();
        return true;
    }

    private (Vector3? startPoint, Vector3? endPoint) LoadSingleChunk(string filePath, Curve3D curve)
    {
        ApplySegmentTypeFromChunk(filePath);
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

        ApplySegmentTypeFromSequence(data);
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
        Node3D node;
        if (_playerInstance == null)
        {
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
            _playerInstance = node;
        }
        else
        {
            node = _playerInstance;
        }

        node.GlobalPosition = position;
    }

    private void SpawnFinishLine(Vector3 position)
    {
        if (_isEndlessLevel)
        {
            RemoveFinishLine();
            return;
        }

        Area3D finish;
        if (_finishLine == null)
        {
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
            _finishLine = finish;
        }
        else
        {
            finish = _finishLine;
        }

        finish.GlobalPosition = position;
    }

    private void RemoveFinishLine()
    {
        if (_finishLine != null && IsInstanceValid(_finishLine))
        {
            _finishLine.QueueFree();
            _finishLine = null;
        }
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

    private void ApplySegmentTypeFromChunk(string? chunkPath)
    {
        if (_trackGenerator == null)
        {
            return;
        }

        var segmentType = CurveCanvasImporter.ExtractSegmentType(chunkPath);
        if (!string.IsNullOrWhiteSpace(segmentType))
        {
            _trackGenerator.SetSegmentType(segmentType);
        }
    }

    private void ApplySegmentTypeFromSequence(CurveSequenceData data)
    {
        if (_trackGenerator == null)
        {
            return;
        }

        foreach (var chunkPathVariant in data.ChunkPaths)
        {
            var chunkPath = chunkPathVariant?.ToString();
            if (string.IsNullOrWhiteSpace(chunkPath))
            {
                continue;
            }

            var segmentType = CurveCanvasImporter.ExtractSegmentType(chunkPath);
            if (!string.IsNullOrWhiteSpace(segmentType))
            {
                _trackGenerator.SetSegmentType(segmentType);
                break;
            }
        }
    }
}
