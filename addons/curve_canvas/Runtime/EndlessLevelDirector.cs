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

    [Export(PropertyHint.Range, "0,1000,1")]
    public float SpawnLookahead { get; set; } = 150f;

    [Export(PropertyHint.Range, "0,1000,1")]
    public float DespawnDistance { get; set; } = 100f;

    private readonly List<ChunkInstance> _activeChunks = new();
    private readonly RandomNumberGenerator _rng = new();
    private ChunkLibrary? _chunkLibrary;
    private Vector3 _lastExitSocketPosition = Vector3.Zero;
    private string _currentSpeedState = "Any";
    private int _chunkCounter;
    private Vector3 _streamStartPosition = Vector3.Zero;
    private readonly List<ManualChunk> _manualChunks = new();
    private int _manualChunkCursor;

    public override void _Ready()
    {
        _rng.Randomize();
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

        while ((_lastExitSocketPosition.X - playerX) < SpawnLookahead)
        {
            if (!SpawnNextChunk(library))
            {
                break;
            }
        }

        DespawnTrailingChunks(playerX);
    }

    private bool SpawnNextChunk(ChunkLibrary library)
    {
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

        var skipFirstPoint = _activeChunks.Count > 0;
        var track = InstantiateChunkTrack(splinePoints, skipFirstPoint, segmentType);
        if (track == null)
        {
            return false;
        }

        Vector3 lastPoint;
        if (manualChunk != null)
        {
            var translation = _lastExitSocketPosition - manualChunk.Entrance;
            lastPoint = manualChunk.Exit + translation;
        }
        else
        {
            lastPoint = RuntimeSplineUtility.ExtractPosition(splinePoints, splinePoints.Count - 1);
        }

        _lastExitSocketPosition = lastPoint;

        if (!string.IsNullOrWhiteSpace(exitSpeed))
        {
            _currentSpeedState = exitSpeed;
        }

        _activeChunks.Add(new ChunkInstance(track, lastPoint));
        return true;
    }

    private TrackMeshGenerator? InstantiateChunkTrack(Array<Dictionary> points, bool skipFirstPoint, string segmentType)
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
        var (startPoint, lastPoint) = RuntimeSplineUtility.AppendPoints(track.Curve, points, skipFirstPoint, Vector3.Zero);
        if (startPoint.HasValue && _activeChunks.Count == 0)
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

    private void DespawnTrailingChunks(float playerX)
    {
        var threshold = playerX - DespawnDistance;
        for (var i = 0; i < _activeChunks.Count;)
        {
            var chunk = _activeChunks[i];
            if (chunk.ExitPosition.X < threshold)
            {
                chunk.Track.QueueFree();
                _activeChunks.RemoveAt(i);
                continue;
            }

            break;
        }
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
        public ChunkInstance(TrackMeshGenerator track, Vector3 exitPosition)
        {
            Track = track;
            ExitPosition = exitPosition;
        }

        public TrackMeshGenerator Track { get; }
        public Vector3 ExitPosition { get; }
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
