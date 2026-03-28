using System.Diagnostics.CodeAnalysis;
using Godot;

namespace CurveCanvas.Editor;

[GlobalClass]
[Tool]
public partial class CurveCanvasRegistry : Resource
{
    [GlobalClass]
    public partial class RegistryItem : Resource
    {
        [Export]
        public PackedScene Prefab { get; set; } = default!;

        [Export]
        public Texture2D Thumbnail { get; set; } = default!;
    }

    [Export]
    public Godot.Collections.Dictionary<string, RegistryItem> AssetPalette { get; set; } = new();

    [return: MaybeNull]
    public PackedScene GetPrefab(string objectId)
    {
        if (string.IsNullOrWhiteSpace(objectId))
        {
            GD.PushError("[CurveCanvasRegistry] ObjectID is null or empty when requesting prefab.");
            return null;
        }

        if (!AssetPalette.TryGetValue(objectId, out var entry) || entry?.Prefab == null)
        {
            GD.PushError($"[CurveCanvasRegistry] Prefab not found for ObjectID '{objectId}'.");
            return null;
        }

        return entry.Prefab;
    }

    [return: MaybeNull]
    public Texture2D GetThumbnail(string objectId)
    {
        if (string.IsNullOrWhiteSpace(objectId))
        {
            GD.PushError("[CurveCanvasRegistry] ObjectID is null or empty when requesting thumbnail.");
            return null;
        }

        if (!AssetPalette.TryGetValue(objectId, out var entry) || entry?.Thumbnail == null)
        {
            GD.PushError($"[CurveCanvasRegistry] Thumbnail not found for ObjectID '{objectId}'.");
            return null;
        }

        return entry.Thumbnail;
    }
}
