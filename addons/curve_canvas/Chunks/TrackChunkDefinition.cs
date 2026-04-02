using Godot;

namespace CurveCanvas.Editor;

/// <summary>
/// Base resource describing a track chunk that can inject spline data into the runtime curve.
/// </summary>
[GlobalClass]
public partial class TrackChunkDefinition : Resource
{
    [Export]
    public string SegmentType { get; set; } = string.Empty;

    [Export]
    public string RequiredEntrySpeed { get; set; } = "Any";

    [Export]
    public string ExitSpeed { get; set; } = "Any";

    /// <summary>
    /// Generates spline dictionaries rooted at the given start position.
    /// </summary>
    public virtual Godot.Collections.Array<Godot.Collections.Dictionary> GenerateSplineData(Vector3 startPosition)
    {
        return new Godot.Collections.Array<Godot.Collections.Dictionary>();
    }
}
