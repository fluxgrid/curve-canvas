using System.Collections.Generic;

namespace CurveCanvas.AuthoringCore;

/// <summary>
/// Serializable list of chunk files used to assemble a macro-level sequence.
/// </summary>
public sealed class CurveSequenceData
{
    public Godot.Collections.Array<string> ChunkPaths { get; set; } = new();
}
