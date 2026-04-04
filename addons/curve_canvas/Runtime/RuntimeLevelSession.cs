using Godot;

namespace CurveCanvas.Editor;

/// <summary>
/// Simple static session used to pass pending level selections between scenes.
/// </summary>
public static class RuntimeLevelSession
{
    private static string _pendingLevelPath = string.Empty;

    public static void SetPendingLevel(string? levelPath)
    {
        _pendingLevelPath = levelPath ?? string.Empty;
    }

    public static bool TryConsumePendingLevel(out string levelPath)
    {
        levelPath = _pendingLevelPath;
        if (string.IsNullOrWhiteSpace(levelPath))
        {
            return false;
        }

        _pendingLevelPath = string.Empty;
        return true;
    }
}
