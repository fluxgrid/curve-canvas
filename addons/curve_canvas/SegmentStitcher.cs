using Godot;

namespace CurveCanvas.Editor;

/// <summary>
/// Utility helpers for aligning curve segments via entrance/exit sockets.
/// </summary>
public static class SegmentStitcher
{
    /// <summary>
    /// Calculates the translation required to align the entrance of the next curve with the exit of the previous curve.
    /// </summary>
    public static Transform3D CalculateSocketAlignment(Curve3D? previousCurve, Curve3D? nextCurve)
    {
        if (previousCurve == null || nextCurve == null)
        {
            return Transform3D.Identity;
        }

        var previousCount = previousCurve.GetPointCount();
        if (previousCount == 0 || nextCurve.GetPointCount() == 0)
        {
            return Transform3D.Identity;
        }

        var targetPos = previousCurve.GetPointPosition(previousCount - 1);
        var originPos = nextCurve.GetPointPosition(0);
        var offset = targetPos - originPos;
        return new Transform3D(Basis.Identity, offset);
    }
}
