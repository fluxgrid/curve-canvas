using System;
using Godot;

namespace CurveCanvas.Editor;

/// <summary>
/// Registry of available track chunk definitions with filtering helpers.
/// </summary>
[GlobalClass]
public partial class ChunkLibrary : Node
{
    [Export]
    public Godot.Collections.Array<TrackChunkDefinition> AvailableChunks { get; set; } = new();

    public Godot.Collections.Array<TrackChunkDefinition> GetValidChunks(string currentSpeedState)
    {
        var results = new Godot.Collections.Array<TrackChunkDefinition>();
        var normalizedSpeed = string.IsNullOrWhiteSpace(currentSpeedState) ? "Any" : currentSpeedState;

        foreach (var chunk in AvailableChunks)
        {
            if (chunk == null)
            {
                continue;
            }

            var required = string.IsNullOrWhiteSpace(chunk.RequiredEntrySpeed)
                ? "Any"
                : chunk.RequiredEntrySpeed;

            if (required.Equals("Any", StringComparison.OrdinalIgnoreCase) ||
                required.Equals(normalizedSpeed, StringComparison.OrdinalIgnoreCase))
            {
                results.Add(chunk);
            }
        }

        return results;
    }
}
