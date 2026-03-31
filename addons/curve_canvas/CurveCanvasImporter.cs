using System;
using System.Collections.Generic;
using System.Text.Json;
using CurveCanvas.AuthoringCore;
using Godot;

namespace CurveCanvas.Editor;

/// <summary>
/// Imports serialized CurveCanvas payloads back into the active Godot scene.
/// </summary>
[Tool]
public partial class CurveCanvasImporter : EditorScript
{
    private const string TriggerContainerName = "CameraTriggers";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

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

        if (!FileAccess.FileExists(importPath))
        {
            GD.PushError($"[CurveCanvasImporter] No export found at {importPath}.");
            return;
        }

        var data = DeserializeCanvasData(importPath);
        if (data == null)
        {
            GD.PushError("[CurveCanvasImporter] File did not contain CurveCanvas data.");
            return;
        }

        ApplySplineData(sceneRoot, data);
        ReportCameraTriggers(data);
        GD.Print($"[CurveCanvasImporter] Imported {data.Spline.Count} spline points from {importPath}");
    }

    /// <summary>
    /// Rebuilds camera trigger nodes from a serialized CurveCanvas payload.
    /// </summary>
    /// <param name="filePath">The JSON export path (res:// preferred).</param>
    /// <param name="rootNode">Scene graph node that should own the imported triggers.</param>
    /// <param name="triggerPrefab">Prefab instantiated for each trigger entry.</param>
    /// <param name="undoRedo">Optional undo stack to wrap trigger placement.</param>
    public static void LoadCanvas(string filePath, Node rootNode, PackedScene triggerPrefab, EditorUndoRedoManager? undoRedo = null)
    {
        if (string.IsNullOrWhiteSpace(filePath))
        {
            GD.PushError("[CurveCanvasImporter] filePath cannot be null or empty.");
            return;
        }

        if (rootNode == null)
        {
            GD.PushError("[CurveCanvasImporter] rootNode is required to load CurveCanvas data.");
            return;
        }

        if (triggerPrefab == null)
        {
            GD.PushError("[CurveCanvasImporter] triggerPrefab must be provided to reconstruct camera triggers.");
            return;
        }

        if (!FileAccess.FileExists(filePath))
        {
            GD.PushError($"[CurveCanvasImporter] File '{filePath}' was not found.");
            return;
        }

        var data = DeserializeCanvasData(filePath);
        if (data == null)
        {
            GD.PushError($"[CurveCanvasImporter] '{filePath}' could not be parsed into CurveCanvas data.");
            return;
        }

        var owner = ResolveOwner(rootNode);
        var container = EnsureTriggerContainer(rootNode, owner);
        ClearExistingTriggers(container);

        var triggers = data.CameraTriggers ?? new List<CameraTriggerData>();
        if (triggers.Count == 0)
        {
            GD.Print($"[CurveCanvasImporter] No camera triggers found in '{filePath}'.");
            return;
        }

        for (var i = 0; i < triggers.Count; i++)
        {
            var triggerData = triggers[i];
            Node3D? triggerNode = null;
            try
            {
                triggerNode = triggerPrefab.Instantiate<Node3D>();
            }
            catch (Exception ex)
            {
                GD.PushError($"[CurveCanvasImporter] Trigger prefab instantiation failed: {ex.Message}");
                continue;
            }

            if (triggerNode == null)
            {
                GD.PushError("[CurveCanvasImporter] Trigger prefab did not instantiate a Node3D.");
                continue;
            }

            triggerNode.Name = DetermineTriggerName(triggerData, i);

            ApplyTransform(triggerNode, triggerData.VolumeTransform);
            ApplyTriggerMetadata(triggerNode, triggerData);

            if (undoRedo == null)
            {
                container.AddChild(triggerNode, true);
                if (owner != null)
                {
                    triggerNode.Owner = owner;
                }

                continue;
            }

            var transform = triggerNode.GlobalTransform;

            undoRedo.CreateAction($"Place Camera Trigger ({triggerNode.Name})");
            undoRedo.AddDoMethod(container, Node.MethodName.AddChild, triggerNode);
            if (owner != null)
            {
                undoRedo.AddDoMethod(triggerNode, Node.MethodName.SetOwner, owner);
            }
            undoRedo.AddDoMethod(triggerNode, Node3D.MethodName.SetGlobalTransform, transform);
            undoRedo.AddUndoMethod(container, Node.MethodName.RemoveChild, triggerNode);
            undoRedo.AddUndoReference(triggerNode);
            undoRedo.CommitAction();
        }

        GD.Print($"[CurveCanvasImporter] Rebuilt {triggers.Count} camera trigger(s) from {filePath}.");
    }

    private static CurveCanvasExportData? DeserializeCanvasData(string filePath)
    {
        try
        {
            var json = FileAccess.GetFileAsString(filePath);
            return JsonSerializer.Deserialize<CurveCanvasExportData>(json, JsonOptions);
        }
        catch (Exception ex)
        {
            GD.PushError($"[CurveCanvasImporter] Failed to read '{filePath}': {ex.Message}");
            return null;
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
        var triggerCount = data.CameraTriggers?.Count ?? 0;
        if (triggerCount == 0)
        {
            return;
        }

        GD.Print($"[CurveCanvasImporter] Camera trigger data detected ({triggerCount}). Call LoadCanvas() with a trigger prefab to rebuild them.");
    }

    private static Node EnsureTriggerContainer(Node rootNode, Node owner)
    {
        Node? container = null;
        foreach (Node child in rootNode.GetChildren())
        {
            if (child.Name == TriggerContainerName)
            {
                container = child;
                break;
            }
        }

        if (container != null)
        {
            if (container.Owner == null)
            {
                container.Owner = owner;
            }

            return container;
        }

        var newContainer = new Node3D
        {
            Name = TriggerContainerName
        };
        rootNode.AddChild(newContainer, true);
        newContainer.Owner = owner;
        return newContainer;
    }

    private static void ClearExistingTriggers(Node container)
    {
        foreach (Node child in container.GetChildren())
        {
            child.Owner = null;
            child.QueueFree();
        }
    }

    private static Node ResolveOwner(Node rootNode)
    {
        var tree = rootNode.GetTree();
        if (tree?.EditedSceneRoot != null)
        {
            return tree.EditedSceneRoot;
        }

        return rootNode.Owner ?? rootNode;
    }

    private static void ApplyTransform(Node3D node, CurveCanvasTransform? transform)
    {
        var safeTransform = transform ?? CurveCanvasTransform.Identity;
        node.GlobalPosition = ToVector3(safeTransform.Position, Vector3.Zero);
        node.RotationDegrees = ToVector3(safeTransform.RotationDegrees, Vector3.Zero);
        node.Scale = ToVector3(safeTransform.Scale, Vector3.One);
    }

    private static Vector3 ToVector3(CurveCanvasVector3? dto, Vector3 fallback)
    {
        if (dto == null)
        {
            return fallback;
        }

        return new Vector3(dto.X, dto.Y, dto.Z);
    }

    private static void ApplyTriggerMetadata(Node3D triggerNode, CameraTriggerData data)
    {
        var objectId = data.ObjectId ?? string.Empty;
        var targetPath = CreateNodePath(data.TargetCameraPath);

        if (triggerNode is CameraTriggerAuthor author)
        {
            author.ObjectID = objectId;
            author.TargetCameraPath = targetPath;
            return;
        }

        TrySetProperty(triggerNode, nameof(CameraTriggerAuthor.ObjectID), objectId);
        TrySetProperty(triggerNode, nameof(CameraTriggerAuthor.TargetCameraPath), targetPath);
    }

    private static NodePath CreateNodePath(string? path)
    {
        return string.IsNullOrWhiteSpace(path) ? new NodePath() : new NodePath(path);
    }

    private static void TrySetProperty(object target, string propertyName, object value)
    {
        var property = target.GetType().GetProperty(propertyName);
        if (property == null || !property.CanWrite)
        {
            return;
        }

        try
        {
            if (property.PropertyType == typeof(string))
            {
                property.SetValue(target, value?.ToString() ?? string.Empty);
                return;
            }

            if (property.PropertyType == typeof(NodePath))
            {
                if (value is NodePath nodePath)
                {
                    property.SetValue(target, nodePath);
                }
                else
                {
                    property.SetValue(target, CreateNodePath(value?.ToString()));
                }

                return;
            }

            property.SetValue(target, value);
        }
        catch (Exception ex)
        {
            GD.PushWarning($"[CurveCanvasImporter] Failed to set property '{propertyName}' on '{target.GetType().Name}': {ex.Message}");
        }
    }

    private static string DetermineTriggerName(CameraTriggerData data, int index)
    {
        if (!string.IsNullOrWhiteSpace(data.ObjectId))
        {
            return data.ObjectId;
        }

        return $"CameraTrigger_{index + 1:D2}";
    }
}
