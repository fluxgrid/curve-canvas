using System;
using System.Collections.Generic;
using System.Text.Json;
using CurveCanvas.AuthoringCore;
using Godot;

namespace CurveCanvas.Editor;

public partial class SequenceVisualizer : Node3D
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    public void SetVisualizationEnabled(bool enabled)
    {
        Visible = enabled;
        SetProcess(enabled);
    }

    public void RebuildVisuals(Godot.Collections.Array<string> chunkPaths)
    {
        ClearChunks();

        if (chunkPaths == null || chunkPaths.Count == 0)
        {
            return;
        }

        Node3D? previousChunk = null;
        Curve3D? previousCurve = null;

        for (var i = 0; i < chunkPaths.Count; i++)
        {
            var path = chunkPaths[i];
            var data = LoadChunk(path);
            if (data == null)
            {
                continue;
            }

            var (chunkNode, curve) = BuildChunkNode(data, i);
            AddChild(chunkNode);

            if (previousCurve != null && previousChunk != null)
            {
                var alignment = SegmentStitcher.CalculateSocketAlignment(previousCurve, curve);
                chunkNode.GlobalTransform = previousChunk.GlobalTransform * alignment;
            }

            previousChunk = chunkNode;
            previousCurve = curve;
        }
    }

    private void ClearChunks()
    {
        foreach (Node child in GetChildren())
        {
            child.QueueFree();
        }
    }

    private static CurveCanvasExportData? LoadChunk(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || !FileAccess.FileExists(path))
        {
            GD.PushWarning($"[SequenceVisualizer] Chunk '{path}' does not exist.");
            return null;
        }

        try
        {
            var json = FileAccess.GetFileAsString(path);
            return JsonSerializer.Deserialize<CurveCanvasExportData>(json, JsonOptions);
        }
        catch (Exception ex)
        {
            GD.PushError($"[SequenceVisualizer] Failed to parse chunk '{path}': {ex.Message}");
            return null;
        }
    }

    private static (Node3D chunkNode, Curve3D curve) BuildChunkNode(CurveCanvasExportData data, int index)
    {
        var chunkNode = new Node3D
        {
            Name = $"SequenceChunk_{index:D2}"
        };

        var track = new TrackMeshGenerator
        {
            Name = "TrackGeneratorPreview",
            ProcessMode = ProcessModeEnum.Disabled
        };

        var curve = new Curve3D();
        ApplySpline(curve, data.Spline);
        track.Curve = curve;

        chunkNode.AddChild(track);
        return (chunkNode, curve);
    }

    private static void ApplySpline(Curve3D curve, List<CurveCanvasSplinePoint>? splinePoints)
    {
        curve.ClearPoints();
        if (splinePoints == null)
        {
            return;
        }

        foreach (var point in splinePoints)
        {
            var position = new Vector3(point.X, point.Y, 0f);
            var inVec = new Vector3(point.InX, point.InY, 0f);
            var outVec = new Vector3(point.OutX, point.OutY, 0f);
            curve.AddPoint(position, inVec, outVec);
        }
    }
}
