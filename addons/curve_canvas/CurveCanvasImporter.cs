using System;
using System.Collections.Generic;
using System.Text.Json;
using CurveCanvas.AuthoringCore;
using Godot;

namespace CurveCanvas.Editor;

/// <summary>
/// Imports serialized CurveCanvas payloads back into the active Godot scene.
/// </summary>
public static class CurveCanvasImporter
{
    private const string TriggerContainerName = "CameraTriggers";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    /// <summary>
    /// Rebuilds camera trigger nodes from a serialized CurveCanvas payload stored on disk.
    /// </summary>
    public static void LoadCanvas(string filePath, Node rootNode, PackedScene triggerPrefab, UndoRedo? undoRedo = null, LevelMetadataPanel? metadataPanel = null)
    {
        if (string.IsNullOrWhiteSpace(filePath))
        {
            GD.PushError("[CurveCanvasImporter] filePath cannot be null or empty.");
            return;
        }

        if (!FileAccess.FileExists(filePath))
        {
            GD.PushError($"[CurveCanvasImporter] File '{filePath}' was not found.");
            return;
        }

        string json;
        try
        {
            json = FileAccess.GetFileAsString(filePath);
        }
        catch (Exception ex)
        {
            GD.PushError($"[CurveCanvasImporter] Failed to read '{filePath}': {ex.Message}");
            return;
        }

        LoadCanvasFromString(json, rootNode, triggerPrefab, undoRedo, metadataPanel, filePath);
    }

    /// <summary>
    /// Rebuilds camera trigger nodes from an in-memory JSON payload.
    /// </summary>
    public static void LoadCanvasFromString(string json, Node rootNode, PackedScene triggerPrefab, UndoRedo? undoRedo = null, LevelMetadataPanel? metadataPanel = null, string? sourceName = null)
    {
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

        if (string.IsNullOrWhiteSpace(json))
        {
            GD.PushError("[CurveCanvasImporter] JSON payload is empty; nothing to load.");
            return;
        }

        var data = DeserializeCanvasData(json);
        if (data == null)
        {
            var label = string.IsNullOrEmpty(sourceName) ? "payload" : sourceName;
            GD.PushError($"[CurveCanvasImporter] '{label}' could not be parsed into CurveCanvas data.");
            return;
        }
        ApplyMetadata(metadataPanel, data.Metadata);
        ApplySpline(rootNode, data.Spline);

        var owner = ResolveOwner(rootNode);
        var container = EnsureTriggerContainer(rootNode, owner);
        ClearExistingTriggers(container);

        var sourceLabel = string.IsNullOrEmpty(sourceName) ? "payload" : sourceName;
        var triggers = data.CameraTriggers ?? new List<CameraTriggerData>();
        if (triggers.Count == 0)
        {
            GD.Print($"[CurveCanvasImporter] No camera triggers found in '{sourceLabel}'.");
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
            undoRedo.AddDoMethod(Callable.From(() => container.AddChild(triggerNode)));
            if (owner != null)
            {
                undoRedo.AddDoMethod(Callable.From(() => triggerNode.SetOwner(owner)));
            }
            undoRedo.AddDoMethod(Callable.From(() => triggerNode.SetGlobalTransform(transform)));
            undoRedo.AddUndoMethod(Callable.From(() => container.RemoveChild(triggerNode)));
            undoRedo.AddUndoReference(triggerNode);
                undoRedo.CommitAction();
        }

        GD.Print($"[CurveCanvasImporter] Rebuilt {triggers.Count} camera trigger(s) from {sourceLabel}.");
    }

    private static void ApplySpline(Node rootNode, List<CurveCanvasSplinePoint>? splinePoints)
    {
        var track = CurveCanvasExportCommon.FindTrackGenerator(rootNode);
        var curve = track?.Curve;
        if (curve == null)
        {
            GD.PushWarning("[CurveCanvasImporter] TrackMeshGenerator with a valid Curve is required to rebuild the spline.");
            return;
        }

        curve.ClearPoints();
        if (splinePoints == null || splinePoints.Count == 0)
        {
            GD.Print("[CurveCanvasImporter] Spline payload was empty; cleared existing curve points.");
            return;
        }

        foreach (var point in splinePoints)
        {
            var position = new Vector3(point.X, point.Y, 0f);
            curve.AddPoint(position);
        }
    }

    private static void ApplyMetadata(LevelMetadataPanel? metadataPanel, CurveCanvasMetadata? metadata)
    {
        if (metadataPanel == null || metadata == null)
        {
            return;
        }

        var levelName = string.IsNullOrWhiteSpace(metadata.LevelName) ? metadata.SceneName : metadata.LevelName;
        var author = string.IsNullOrWhiteSpace(metadata.Author) ? "Anonymous" : metadata.Author;
        var parTime = metadata.ParTimeSeconds <= 0f ? 60f : metadata.ParTimeSeconds;
        metadataPanel.ApplyMetadata(levelName, author, parTime);
    }

    private static CurveCanvasExportData? DeserializeCanvasData(string json)
    {
        try
        {
            return JsonSerializer.Deserialize<CurveCanvasExportData>(json, JsonOptions);
        }
        catch (Exception ex)
        {
            GD.PushError($"[CurveCanvasImporter] Failed to parse CurveCanvas payload: {ex.Message}");
            return null;
        }
    }

    private static Node EnsureTriggerContainer(Node rootNode, Node? owner)
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
        if (tree?.CurrentScene != null)
        {
            return tree.CurrentScene;
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
