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
        var splinePoints = definition.GenerateSplineData(spawnOrigin);
        if (splinePoints.Count == 0)
        {
            GD.PushWarning("[EndlessLevelDirector] Selected chunk definition returned no spline data.");
            return false;
        }

        var skipFirstPoint = _activeChunks.Count > 0;
        var track = InstantiateChunkTrack(splinePoints, skipFirstPoint);
        if (track == null)
        {
            return false;
        }

        var lastPoint = RuntimeSplineUtility.ExtractPosition(splinePoints, splinePoints.Count - 1);
        _lastExitSocketPosition = lastPoint;

        if (!string.IsNullOrWhiteSpace(definition.ExitSpeed))
        {
            _currentSpeedState = definition.ExitSpeed;
        }

        _activeChunks.Add(new ChunkInstance(track, lastPoint));
        return true;
    }

    private TrackMeshGenerator? InstantiateChunkTrack(Array<Dictionary> points, bool skipFirstPoint)
    {
        var track = new TrackMeshGenerator
        {
            Name = $"EndlessChunk_{_chunkCounter++}",
            Curve = new Curve3D()
        };

        AddChild(track, true);
        var (_, lastPoint) = RuntimeSplineUtility.AppendPoints(track.Curve, points, skipFirstPoint, Vector3.Zero);
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
}
