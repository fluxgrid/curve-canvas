using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using CurveCanvas.AuthoringCore;
using Godot;

namespace CurveCanvas.Editor;

/// <summary>
/// Helper methods for saving and loading CurveSequenceData payloads.
/// </summary>
public static class CurveSequenceSerializer
{
    private sealed class CurveSequenceFileModel
    {
        public List<string> ChunkPaths { get; set; } = new();
    }

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true
    };

    public static bool Save(string filePath, CurveSequenceData data)
    {
        if (data == null)
        {
            GD.PushError("[CurveSequenceSerializer] Data cannot be null.");
            return false;
        }

        try
        {
            var model = new CurveSequenceFileModel
            {
                ChunkPaths = data.ChunkPaths?.ToList() ?? new List<string>()
            };

            if (!EnsureDirectoryExists(filePath))
            {
                return false;
            }

            var json = JsonSerializer.Serialize(model, JsonOptions);
            using var file = Godot.FileAccess.Open(filePath, Godot.FileAccess.ModeFlags.Write);
            if (file == null)
            {
                GD.PushError($"[CurveSequenceSerializer] Failed to open '{filePath}' for writing: {Godot.FileAccess.GetOpenError()}");
                return false;
            }

            file.StoreString(json);
            GD.Print($"[CurveSequenceSerializer] Saved sequence to {filePath}");
            return true;
        }
        catch (Exception ex)
        {
            GD.PushError($"[CurveSequenceSerializer] Failed to save '{filePath}': {ex.Message}");
            return false;
        }
    }

    public static CurveSequenceData? Load(string filePath)
    {
        if (!File.Exists(ProjectSettings.GlobalizePath(filePath)))
        {
            GD.PushError($"[CurveSequenceSerializer] File '{filePath}' not found.");
            return null;
        }

        try
        {
            var json = Godot.FileAccess.GetFileAsString(filePath);
            var model = JsonSerializer.Deserialize<CurveSequenceFileModel>(json, JsonOptions);
            if (model == null)
            {
                GD.PushError($"[CurveSequenceSerializer] '{filePath}' contained invalid data.");
                return null;
            }

            var result = new CurveSequenceData();
            result.ChunkPaths.AddRange(model.ChunkPaths ?? new List<string>());
            return result;
        }
        catch (Exception ex)
        {
            GD.PushError($"[CurveSequenceSerializer] Failed to read '{filePath}': {ex.Message}");
            return null;
        }
    }
    private static bool EnsureDirectoryExists(string filePath)
    {
        var globalPath = ProjectSettings.GlobalizePath(filePath);
        var directory = Path.GetDirectoryName(globalPath);
        if (string.IsNullOrEmpty(directory))
        {
            return true;
        }

        var error = DirAccess.MakeDirRecursiveAbsolute(directory);
        if (error != Error.Ok && error != Error.AlreadyExists)
        {
            GD.PushError($"[CurveSequenceSerializer] Failed to create directory '{directory}': {error}");
            return false;
        }

        return true;
    }
}
