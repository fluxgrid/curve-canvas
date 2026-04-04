using Godot;
using Godot.Collections;

namespace CurveCanvas.Editor;

/// <summary>
/// Helper methods for translating serialized spline dictionaries into live Curve3D data.
/// </summary>
internal static class RuntimeSplineUtility
{
    public static (Vector3? firstPoint, Vector3? lastPoint) AppendPoints(
        Curve3D curve,
        Array<Dictionary> points,
        bool skipFirstPoint,
        Vector3 translation)
    {
        Vector3? first = null;
        Vector3? last = null;
        if (curve == null || points == null || points.Count == 0)
        {
            return (first, last);
        }

        for (var i = 0; i < points.Count; i++)
        {
            if (skipFirstPoint && i == 0)
            {
                continue;
            }

            var entry = points[i];
            var position = ReadVector(entry, "position") + translation;
            var inVec = ReadVector(entry, "point_in");
            var outVec = ReadVector(entry, "point_out");
            curve.AddPoint(position, inVec, outVec);
            first ??= position;
            last = position;
        }

        return (first, last);
    }

    public static Vector3 ExtractPosition(Array<Dictionary> points, int index)
    {
        if (points == null || points.Count == 0)
        {
            return Vector3.Zero;
        }

        var safeIndex = Mathf.Clamp(index, 0, points.Count - 1);
        return ReadVector(points[safeIndex], "position");
    }

    private static Vector3 ReadVector(Dictionary entry, string key)
    {
        if (entry != null && entry.TryGetValue(key, out var variant))
        {
            try
            {
                return variant.As<Vector3>();
            }
            catch
            {
                // ignored; fall through to zero.
            }
        }

        return Vector3.Zero;
    }
}
