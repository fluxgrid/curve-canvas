using System;
using System.Collections.Generic;
using Godot;
using Godot.Collections;

namespace CurveCanvas.Editor;

/// <summary>
/// Streams authored/procedural chunks at runtime to simulate an endless level.
/// </summary>
public partial class EndlessLevelDirector : Node
{
    [Export]
    public NodePath GlobalLibraryPath { get; set; } = new();

    [Export(PropertyHint.TypeString, "Leave blank for a random seed each time")]
    public string LevelSeed { get; set; } = string.Empty;

    [Export(PropertyHint.Range, "0,1000,1")]
    public float SpawnLookahead { get; set; } = 200f;

    [Export(PropertyHint.Range, "0,2000,1")]
    public float DespawnDistance { get; set; } = 400f;

    private readonly List<ChunkInstance> _activeChunks = new();
    private readonly Stack<ChunkBlueprint> _prunedBackwardChunks = new();
    private readonly Stack<ChunkBlueprint> _prunedForwardChunks = new();
    private bool _startBoundaryWarned;
    private readonly RandomNumberGenerator _rng = new();
    private ulong _activeSeed;
    private ChunkLibrary? _chunkLibrary;
    private Vector3 _lastExitSocketPosition = Vector3.Zero;
    private string _currentSpeedState = "Any";
    private int _chunkCounter;
    private Vector3 _streamStartPosition = Vector3.Zero;
    private readonly List<ManualChunk> _manualChunks = new();
    private int _manualChunkCursor;

    public override void _Ready()
    {
        InitializeRNG();
    }

    public void InitializeRNG()
    {
        if (string.IsNullOrWhiteSpace(LevelSeed))
        {
            _rng.Randomize();
            _activeSeed = _rng.Seed;
            GD.Print($"[EndlessLevelDirector] Started with RANDOM seed: {_activeSeed}");
        }
        else
        {
            _activeSeed = (ulong)LevelSeed.Hash();
            _rng.Seed = _activeSeed;
            GD.Print($"[EndlessLevelDirector] Started with FIXED seed: '{LevelSeed}' ({_activeSeed})");
        }
    }

    /// <summary>
    /// Call from the main loop to keep the endless stream populated relative to the player.
    /// </summary>
    public void ProcessEndlessGeneration(float playerX)
    {
        var library = ResolveLibrary();
        if (library == null)
        {
            return;
        }

        // 0. RECALCULATE TRUE HORIZON
        if (_activeChunks.Count > 0)
        {
            _lastExitSocketPosition = _activeChunks[_activeChunks.Count - 1].ExitPosition;
        }
        else
        {
            _lastExitSocketPosition = _streamStartPosition;
        }

        // 1. FILL THE HORIZON
        var failSafe = 0;
        while ((_lastExitSocketPosition.X - playerX) < SpawnLookahead && failSafe < 10)
        {
            var success = SpawnNextChunk(library);
            if (!success)
            {
                GD.PushWarning("[EndlessLevelDirector] Failed to spawn chunk. Aborting fill loop to prevent infinite hang.");
                break;
            }
            failSafe++;
        }

        // 2. PRUNE THE WAKE
        var leftPruneThreshold = playerX - DespawnDistance;
        var rightPruneThreshold = playerX + DespawnDistance;
        for (var i = _activeChunks.Count - 1; i >= 0; i--)
        {
            var chunk = _activeChunks[i];
            var pruneLeft = chunk.ExitPosition.X < leftPruneThreshold;
            var pruneRight = chunk.EntrancePosition.X > rightPruneThreshold;
            if (pruneLeft || pruneRight)
            {
                if (chunk.Track != null && IsInstanceValid(chunk.Track))
                {
                    chunk.Track.QueueFree();
                }

                if (chunk.Blueprint != null)
                {
                    if (pruneLeft)
                    {
                        _prunedBackwardChunks.Push(chunk.Blueprint);
                    }

                    if (pruneRight)
                    {
                        _prunedForwardChunks.Push(chunk.Blueprint);
                    }
                }

                _activeChunks.RemoveAt(i);
            }
        }

        // 3. BACKFILL IF THE CAMERA MOVES BACKWARD
        var earliestEntrance = _activeChunks.Count > 0 ? _activeChunks[0].EntrancePosition.X : _streamStartPosition.X;
        while ((playerX - earliestEntrance) < SpawnLookahead)
        {
            if (!SpawnPreviousChunk())
            {
                break;
            }

            earliestEntrance = _activeChunks[0].EntrancePosition.X;
        }
    }

    private bool SpawnNextChunk(ChunkLibrary library)
    {
        if (TrySpawnForwardHistoryChunk())
        {
            return true;
        }

        Array<Dictionary>? splinePoints = null;
        string segmentType = "Flow";
        string? exitSpeed = null;
        ManualChunk? manualChunk = null;

        if (_manualChunks.Count > 0)
        {
            manualChunk = _manualChunks[_manualChunkCursor];
            var translation = _lastExitSocketPosition - manualChunk.Entrance;
            splinePoints = CloneChunkPoints(manualChunk.Points, translation);
            segmentType = manualChunk.SegmentType;
            exitSpeed = manualChunk.ExitSpeed;
            _manualChunkCursor = (_manualChunkCursor + 1) % _manualChunks.Count;
        }
        else
        {
            var pool = library.GetValidChunks(_currentSpeedState);
            if (pool.Count == 0)
            {
                pool = library.AvailableChunks;
            }

            if (pool.Count == 0)
            {
                GD.PushWarning("[EndlessLevelDirector] ChunkLibrary has no available definitions.");
                return false;
            }

            var index = (int)_rng.RandiRange(0, pool.Count - 1);
            var definition = pool[index];
            if (definition == null)
            {
                return false;
            }

            var spawnOrigin = _lastExitSocketPosition;
            splinePoints = definition.GenerateSplineData(spawnOrigin);
            if (splinePoints.Count == 0)
            {
                GD.PushWarning("[EndlessLevelDirector] Selected chunk definition returned no spline data.");
                return false;
            }

            segmentType = string.IsNullOrWhiteSpace(definition.SegmentType) ? "Flow" : definition.SegmentType;
            exitSpeed = definition.ExitSpeed;
        }

        if (splinePoints == null || splinePoints.Count == 0)
        {
            return false;
        }

        var track = InstantiateChunkTrack(splinePoints, segmentType, out var startPoint, out var lastPointVector);
        if (track == null)
        {
            return false;
        }

        Vector3 entrancePoint;
        Vector3 lastPoint;
        if (manualChunk != null)
        {
            var translation = _lastExitSocketPosition - manualChunk.Entrance;
            entrancePoint = manualChunk.Entrance + translation;
            lastPoint = manualChunk.Exit + translation;
        }
        else
        {
            entrancePoint = startPoint ?? _lastExitSocketPosition;
            lastPoint = lastPointVector ?? RuntimeSplineUtility.ExtractPosition(splinePoints, splinePoints.Count - 1);
        }

        var relativePoints = CloneChunkPoints(splinePoints, -entrancePoint);
        var relativeExit = lastPoint - entrancePoint;
        var blueprint = new ChunkBlueprint(relativePoints, segmentType, exitSpeed ?? "Any", relativeExit);

        _lastExitSocketPosition = lastPoint;

        if (!string.IsNullOrWhiteSpace(exitSpeed))
        {
            _currentSpeedState = exitSpeed;
        }

        _activeChunks.Add(new ChunkInstance(track, entrancePoint, lastPoint, blueprint));
        return true;
    }

    private TrackMeshGenerator? InstantiateChunkTrack(Array<Dictionary> points, string segmentType, out Vector3? startPoint, out Vector3? lastPoint)
    {
        var track = new TrackMeshGenerator
        {
            Name = $"EndlessChunk_{_chunkCounter++}",
            Curve = new Curve3D()
        };

        if (!string.IsNullOrWhiteSpace(segmentType))
        {
            track.SetSegmentType(segmentType);
        }

        AddChild(track, true);
        var appendResult = RuntimeSplineUtility.AppendPoints(track.Curve, points, skipFirstPoint: false, translation: Vector3.Zero);
        startPoint = appendResult.firstPoint;
        lastPoint = appendResult.lastPoint;
        if (startPoint.HasValue && (_activeChunks.Count == 0 || startPoint.Value.X < _streamStartPosition.X))
        {
            _streamStartPosition = startPoint.Value;
        }
        if (lastPoint == null)
        {
            track.QueueFree();
            return null;
        }

        return track;
    }

    private ChunkLibrary? ResolveLibrary()
    {
        if (_chunkLibrary != null && IsInstanceValid(_chunkLibrary))
        {
            return _chunkLibrary;
        }

        if (GlobalLibraryPath.IsEmpty)
        {
            GD.PushWarning("[EndlessLevelDirector] GlobalLibraryPath is not assigned.");
            return null;
        }

        _chunkLibrary = GetNodeOrNull<ChunkLibrary>(GlobalLibraryPath);
        if (_chunkLibrary == null)
        {
            GD.PushWarning($"[EndlessLevelDirector] Could not find ChunkLibrary at '{GlobalLibraryPath}'.");
        }

        return _chunkLibrary;
    }

    public void ResetStream(Vector3 startPosition)
    {
        ClearActiveChunks();
        _lastExitSocketPosition = startPosition;
        _currentSpeedState = "Any";
        _chunkCounter = 0;
        _streamStartPosition = startPosition;
        _prunedBackwardChunks.Clear();
        _prunedForwardChunks.Clear();
        _startBoundaryWarned = false;
    }

    private void ClearActiveChunks()
    {
        foreach (var chunk in _activeChunks)
        {
            if (chunk.Track != null && IsInstanceValid(chunk.Track))
            {
                chunk.Track.QueueFree();
            }
        }

        _activeChunks.Clear();
    }

    private bool SpawnPreviousChunk()
    {
        if (_prunedBackwardChunks.Count == 0)
        {
            if (!_startBoundaryWarned &&
                (_activeChunks.Count == 0 || _activeChunks[0].EntrancePosition.X <= _streamStartPosition.X + 0.01f))
            {
                GD.PushWarning("[EndlessLevelDirector] Reached the beginning of the stream; no more chunks exist to the left.");
                _startBoundaryWarned = true;
            }
            return false;
        }

        var blueprint = _prunedBackwardChunks.Pop();
        var anchorEntrance = _activeChunks.Count > 0 ? _activeChunks[0].EntrancePosition : _streamStartPosition;
        var desiredEntrance = anchorEntrance - blueprint.ExitOffset;
        var translatedPoints = CloneChunkPoints(blueprint.Points, desiredEntrance);
        var track = InstantiateChunkTrack(translatedPoints, blueprint.SegmentType, out var startPoint, out var lastPoint);
        if (track == null || !startPoint.HasValue || !lastPoint.HasValue)
        {
            return false;
        }

        var entrance = startPoint.Value;
        var exit = lastPoint.Value;
        _activeChunks.Insert(0, new ChunkInstance(track, entrance, exit, blueprint));

        if (entrance.X < _streamStartPosition.X)
        {
            _streamStartPosition = entrance;
        }

        _startBoundaryWarned = false;
        return true;
    }

    private bool TrySpawnForwardHistoryChunk()
    {
        while (_prunedForwardChunks.Count > 0)
        {
            var blueprint = _prunedForwardChunks.Pop();
            var desiredEntrance = _lastExitSocketPosition;
            var translatedPoints = CloneChunkPoints(blueprint.Points, desiredEntrance);
            var track = InstantiateChunkTrack(translatedPoints, blueprint.SegmentType, out var startPoint, out var lastPoint);
            if (track == null || !startPoint.HasValue || !lastPoint.HasValue)
            {
                continue;
            }

            var entrance = startPoint.Value;
            var exit = lastPoint.Value;
            _activeChunks.Add(new ChunkInstance(track, entrance, exit, blueprint));
            _lastExitSocketPosition = exit;

            if (!string.IsNullOrWhiteSpace(blueprint.ExitSpeed))
            {
                _currentSpeedState = blueprint.ExitSpeed;
            }

            return true;
        }

        return false;
    }

    public Vector3 GetStreamStartPosition()
    {
        return _streamStartPosition;
    }

    public void SetManualChunks(List<Array<Dictionary>> chunks, List<string> segmentTypes, List<string>? exitSpeeds = null)
    {
        _manualChunks.Clear();
        _manualChunkCursor = 0;

        if (chunks == null || chunks.Count == 0)
        {
            return;
        }

        for (var i = 0; i < chunks.Count; i++)
        {
            var points = chunks[i];
            if (points == null || points.Count == 0)
            {
                continue;
            }

            var type = (segmentTypes != null && i < segmentTypes.Count && !string.IsNullOrWhiteSpace(segmentTypes[i]))
                ? segmentTypes[i]
                : "Flow";

            var speed = exitSpeeds != null && i < exitSpeeds.Count ? exitSpeeds[i] : "Any";

            _manualChunks.Add(new ManualChunk(points, type, speed));
        }
    }

    public void ClearManualChunks()
    {
        _manualChunks.Clear();
        _manualChunkCursor = 0;
    }

    private static Array<Dictionary> CloneChunkPoints(Array<Dictionary> source, Vector3 translation)
    {
        var translated = new Array<Dictionary>();
        foreach (Dictionary entry in source)
        {
            var position = entry.TryGetValue("position", out var posVar) ? posVar.As<Vector3>() : Vector3.Zero;
            var inVec = entry.TryGetValue("point_in", out var inVar) ? inVar.As<Vector3>() : Vector3.Zero;
            var outVec = entry.TryGetValue("point_out", out var outVar) ? outVar.As<Vector3>() : Vector3.Zero;

            var dict = new Dictionary
            {
                ["position"] = position + translation,
                ["point_in"] = inVec,
                ["point_out"] = outVec
            };
            translated.Add(dict);
        }

        return translated;
    }

    private sealed class ChunkInstance
    {
        public ChunkInstance(TrackMeshGenerator track, Vector3 entrancePosition, Vector3 exitPosition, ChunkBlueprint blueprint)
        {
            Track = track;
            EntrancePosition = entrancePosition;
            ExitPosition = exitPosition;
            Blueprint = blueprint;
        }

        public TrackMeshGenerator Track { get; }
        public Vector3 EntrancePosition { get; }
        public Vector3 ExitPosition { get; }
        public ChunkBlueprint Blueprint { get; }
    }

    private sealed class ChunkBlueprint
    {
        public ChunkBlueprint(Array<Dictionary> points, string segmentType, string exitSpeed, Vector3 exitOffset)
        {
            Points = points;
            SegmentType = segmentType;
            ExitSpeed = exitSpeed;
            ExitOffset = exitOffset;
        }

        public Array<Dictionary> Points { get; }
        public string SegmentType { get; }
        public string ExitSpeed { get; }
        public Vector3 ExitOffset { get; }
    }

    private sealed class ManualChunk
    {
        public ManualChunk(Array<Dictionary> points, string segmentType, string exitSpeed = "Any")
        {
            Points = points;
            SegmentType = string.IsNullOrWhiteSpace(segmentType) ? "Flow" : segmentType;
            ExitSpeed = string.IsNullOrWhiteSpace(exitSpeed) ? "Any" : exitSpeed;
            Entrance = RuntimeSplineUtility.ExtractPosition(points, 0);
            Exit = RuntimeSplineUtility.ExtractPosition(points, points.Count - 1);
        }

        public Array<Dictionary> Points { get; }
        public string SegmentType { get; }
        public string ExitSpeed { get; }
        public Vector3 Entrance { get; }
        public Vector3 Exit { get; }
    }
}
