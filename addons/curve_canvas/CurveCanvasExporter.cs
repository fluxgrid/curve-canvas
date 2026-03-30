using System;
using System.Collections.Generic;
using System.Text.Json;
using CurveCanvas.AuthoringCore;
using Godot;
using IOPath = System.IO.Path;

namespace CurveCanvas.Editor;

/// <summary>
/// Serializes the active CurveCanvas scene graph into a portable JSON payload.
/// </summary>
[Tool]
public partial class CurveCanvasExporter : EditorScript
{
    private const string ExportFolder = "res://Exports";
    private const string FileExtension = ".curvecanvas.json";

    public override void _Run()
    {
        var editor = EditorInterface.Singleton;
        var sceneRoot = editor?.GetEditedSceneRoot();
        if (sceneRoot == null)
        {
            GD.PushError("[CurveCanvasExporter] No open scene to export.");
            return;
        }

        var track = CurveCanvasExportCommon.FindTrackGenerator(sceneRoot);
        if (track?.Curve == null)
        {
            GD.PushError("[CurveCanvasExporter] TrackMeshGenerator with a valid Curve is required.");
            return;
        }

        var exportData = BuildExportPayload(track, sceneRoot);
        if (!EnsureExportDirectory())
        {
            return;
        }

        var sceneName = CurveCanvasExportCommon.GetSceneName(sceneRoot);
        var exportPath = $"{ExportFolder}/{sceneName}{FileExtension}";

        try
        {
            var options = new JsonSerializerOptions { WriteIndented = true };
            var json = JsonSerializer.Serialize(exportData, options);
            using var file = Godot.FileAccess.Open(exportPath, Godot.FileAccess.ModeFlags.Write);
            file.StoreString(json);
            GD.Print($"[CurveCanvasExporter] Exported {exportData.Spline.Count} points, {exportData.ActionObjects.Count} action objects, and {exportData.SceneryObjects.Count} scenery items to {exportPath}");
        }
        catch (Exception ex)
        {
            GD.PushError($"[CurveCanvasExporter] Failed to write export file: {ex.Message}");
        }
    }

    private CurveCanvasExportData BuildExportPayload(TrackMeshGenerator track, Node sceneRoot)
    {
        var payload = new CurveCanvasExportData
        {
            Metadata =
            {
                SceneName = CurveCanvasExportCommon.GetSceneName(sceneRoot),
                ExportedAtUtc = DateTime.UtcNow.ToString("o"),
                ToolVersion = ResolveToolVersion()
            },
            Spline = CollectSplinePoints(track),
            ActionObjects = CollectActionObjects(sceneRoot),
            SceneryObjects = CollectSceneryObjects(sceneRoot)
            ,
            CameraTriggers = CollectCameraTriggers(sceneRoot)
        };

        return payload;
    }

    private static List<CurveCanvasSplinePoint> CollectSplinePoints(TrackMeshGenerator track)
    {
        var results = new List<CurveCanvasSplinePoint>();
        var curve = track.Curve;
        if (curve == null)
        {
            return results;
        }

        var pointCount = curve.GetPointCount();
        for (var i = 0; i < pointCount; i++)
        {
            var position = curve.GetPointPosition(i);
            position.Z = 0f;
            results.Add(new CurveCanvasSplinePoint(position.X, position.Y));
        }

        return results;
    }

    private static List<CurveCanvasActionObject> CollectActionObjects(Node sceneRoot)
    {
        var results = new List<CurveCanvasActionObject>();
        var tree = sceneRoot.GetTree();
        if (tree == null)
        {
            return results;
        }

        var members = tree.GetNodesInGroup(ActionObjectSnapper.ActionObjectGroup);
        foreach (var member in members)
        {
            if (member is ActionObjectSnapper snapper && !string.IsNullOrWhiteSpace(snapper.ObjectID))
            {
                results.Add(new CurveCanvasActionObject(snapper.ObjectID, snapper.CurveOffset));
            }
        }

        return results;
    }

    private static List<CurveCanvasSceneryObject> CollectSceneryObjects(Node sceneRoot)
    {
        var results = new List<CurveCanvasSceneryObject>();
        var tree = sceneRoot.GetTree();
        if (tree == null)
        {
            return results;
        }

        var members = tree.GetNodesInGroup(SceneryPlaneSnapper.SceneryGroup);
        foreach (var member in members)
        {
            if (member is not SceneryPlaneSnapper snapper)
            {
                continue;
            }

            if (string.IsNullOrWhiteSpace(snapper.ObjectID))
            {
                continue;
            }

            var position = snapper.GlobalPosition;
            results.Add(new CurveCanvasSceneryObject(snapper.ObjectID, position.X, position.Y, snapper.PlaneIndex, snapper.DepthZ));
        }

        return results;
    }

    private static List<CameraTriggerData> CollectCameraTriggers(Node sceneRoot)
    {
        var results = new List<CameraTriggerData>();
        var tree = sceneRoot.GetTree();
        if (tree == null)
        {
            return results;
        }

        var members = tree.GetNodesInGroup(CameraTriggerAuthor.TriggerGroup);
        foreach (var member in members)
        {
            if (member is not CameraTriggerAuthor trigger)
            {
                continue;
            }

            if (string.IsNullOrWhiteSpace(trigger.ObjectID))
            {
                continue;
            }

            var data = new CameraTriggerData
            {
                ObjectId = trigger.ObjectID,
                TargetCameraPath = trigger.TargetCameraPath.ToString(),
                VolumeTransform = CurveCanvasExportCommon.ToExportTransform(trigger.GlobalTransform)
            };
            results.Add(data);
        }

        return results;
    }

    private static bool EnsureExportDirectory()
    {
        var absolute = ProjectSettings.GlobalizePath(ExportFolder);
        var result = DirAccess.MakeDirRecursiveAbsolute(absolute);
        if (result != Error.Ok && result != Error.AlreadyInUse)
        {
            GD.PushError($"[CurveCanvasExporter] Failed to create export directory ({absolute}): {result}");
            return false;
        }

        return true;
    }

    private static string ResolveToolVersion()
    {
        var variant = ProjectSettings.GetSetting("application/config/version", "0.1.0");
        var versionText = variant.ToString();
        return string.IsNullOrWhiteSpace(versionText) ? "0.1.0" : versionText;
    }
}

/// <summary>
/// Simple importer to rebuild the Path3D points from a JSON export.
/// </summary>
[Tool]
public partial class CurveCanvasImporter : EditorScript
{
    public override void _Run()
    {
        var editor = EditorInterface.Singleton;
        var sceneRoot = editor?.GetEditedSceneRoot();
        if (sceneRoot == null)
        {
            GD.PushError("[CurveCanvasImporter] No open scene to import into.");
            return;
        }

        var sceneName = CurveCanvasExportCommon.GetSceneName(sceneRoot);
        var importPath = $"res://Exports/{sceneName}.curvecanvas.json";

        if (!Godot.FileAccess.FileExists(importPath))
        {
            GD.PushError($"[CurveCanvasImporter] No export found at {importPath}.");
            return;
        }

        try
        {
            var json = Godot.FileAccess.GetFileAsString(importPath);
            var data = JsonSerializer.Deserialize<CurveCanvasExportData>(json);
            if (data == null)
            {
                GD.PushError("[CurveCanvasImporter] File did not contain CurveCanvas data.");
                return;
            }

            ApplySplineData(sceneRoot, data);
            ReportCameraTriggers(data);
            GD.Print($"[CurveCanvasImporter] Imported {data.Spline.Count} spline points from {importPath}");
        }
        catch (Exception ex)
        {
            GD.PushError($"[CurveCanvasImporter] Failed to import: {ex.Message}");
        }
    }

    private static void ApplySplineData(Node sceneRoot, CurveCanvasExportData data)
    {
        var track = CurveCanvasExportCommon.FindTrackGenerator(sceneRoot);
        if (track == null)
        {
            track = new TrackMeshGenerator
            {
                Name = "TrackGenerator"
            };
            sceneRoot.AddChild(track, true);
            track.Owner = sceneRoot;
        }

        track.Curve ??= new Curve3D();
        ClearCurve(track.Curve);
        foreach (var point in data.Spline)
        {
            track.Curve.AddPoint(new Vector3(point.X, point.Y, 0f));
        }
    }

    private static void ClearCurve(Curve3D curve)
    {
        for (var i = curve.GetPointCount() - 1; i >= 0; i--)
        {
            curve.RemovePoint(i);
        }
    }

    private static void ReportCameraTriggers(CurveCanvasExportData data)
    {
        if (data.CameraTriggers.Count == 0)
        {
            return;
        }

        GD.Print($"[CurveCanvasImporter] Camera trigger data detected ({data.CameraTriggers.Count}). Runtime reconstruction coming in a future update.");
    }
}

internal static class CurveCanvasExportCommon
{
    public static TrackMeshGenerator? FindTrackGenerator(Node root)
    {
        if (root is TrackMeshGenerator track)
        {
            return track;
        }

        foreach (Node child in root.GetChildren())
        {
            var candidate = FindTrackGenerator(child);
            if (candidate != null)
            {
                return candidate;
            }
        }

        return null;
    }

    public static string GetSceneName(Node root)
    {
        if (!string.IsNullOrEmpty(root.SceneFilePath))
        {
            return IOPath.GetFileNameWithoutExtension(root.SceneFilePath);
        }

        return root.Name;
    }

    public static CurveCanvasTransform ToExportTransform(Transform3D transform)
    {
        var position = transform.Origin;
        var basis = transform.Basis;
        var scale = basis.Scale;
        var rotation = basis.GetEuler();

        return new CurveCanvasTransform
        {
            Position = new CurveCanvasVector3(position.X, position.Y, position.Z),
            RotationDegrees = new CurveCanvasVector3(
                Mathf.RadToDeg(rotation.X),
                Mathf.RadToDeg(rotation.Y),
                Mathf.RadToDeg(rotation.Z)),
            Scale = new CurveCanvasVector3(scale.X, scale.Y, scale.Z)
        };
    }
}
