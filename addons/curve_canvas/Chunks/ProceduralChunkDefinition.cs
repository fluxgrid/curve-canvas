using Godot;

namespace CurveCanvas.Editor;

/// <summary>
/// Chunk definition that synthesizes spline data procedurally using noise.
/// </summary>
[GlobalClass]
public partial class ProceduralChunkDefinition : TrackChunkDefinition
{
    [Export]
    public float ChunkLength { get; set; } = 100f;

    [Export(PropertyHint.Range, "0.1,100,0.1")]
    public float PointSpacing { get; set; } = 5f;

    [Export]
    public float Amplitude { get; set; } = 10f;

    [Export]
    public FastNoiseLite NoiseGenerator { get; set; } = null!;

    public override Godot.Collections.Array<Godot.Collections.Dictionary> GenerateSplineData(Vector3 startPosition)
    {
        var points = new Godot.Collections.Array<Godot.Collections.Dictionary>();
        if (PointSpacing <= 0f || ChunkLength <= 0f)
        {
            return points;
        }

        var noise = NoiseGenerator;
        var spacing = Mathf.Max(0.1f, PointSpacing);
        for (float distance = 0f; distance <= ChunkLength + 0.01f; distance += spacing)
        {
            var noiseValue = noise != null ? noise.GetNoise1D(distance) : 0f;
            var y = noiseValue * Amplitude;
            var position = startPosition + new Vector3(distance, y, 0f);

            var pointDict = new Godot.Collections.Dictionary
            {
                ["position"] = position,
                ["point_in"] = Vector3.Zero,
                ["point_out"] = Vector3.Zero
            };
            points.Add(pointDict);
        }

        return points;
    }
}
