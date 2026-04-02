using Godot;

namespace CurveCanvas.Editor;

/// <summary>
/// Chunk definition backed by authored CurveCanvas JSON exports.
/// </summary>
[GlobalClass]
public partial class AuthoredChunkDefinition : TrackChunkDefinition
{
    [Export(PropertyHint.File, "*.curvecanvas.json")]
    public string JsonFilePath { get; set; } = string.Empty;

    public override Godot.Collections.Array<Godot.Collections.Dictionary> GenerateSplineData(Vector3 startPosition)
    {
        var rawPoints = CurveCanvasImporter.ExtractSplineData(JsonFilePath);
        if (rawPoints.Count == 0)
        {
            return rawPoints;
        }

        var translated = new Godot.Collections.Array<Godot.Collections.Dictionary>();
        var origin = rawPoints[0].TryGetValue("position", out var originVariant)
            ? originVariant.As<Vector3>()
            : Vector3.Zero;
        var offset = startPosition - origin;

        foreach (Godot.Collections.Dictionary entry in rawPoints)
        {
            var position = entry.TryGetValue("position", out var posVariant)
                ? posVariant.As<Vector3>()
                : Vector3.Zero;
            var pointIn = entry.TryGetValue("point_in", out var inVariant)
                ? inVariant.As<Vector3>()
                : Vector3.Zero;
            var pointOut = entry.TryGetValue("point_out", out var outVariant)
                ? outVariant.As<Vector3>()
                : Vector3.Zero;

            var dict = new Godot.Collections.Dictionary
            {
                ["position"] = position + offset,
                ["point_in"] = pointIn,
                ["point_out"] = pointOut
            };
            translated.Add(dict);
        }

        return translated;
    }
}
