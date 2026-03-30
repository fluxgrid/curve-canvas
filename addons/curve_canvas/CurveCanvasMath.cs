using Godot;

namespace CurveCanvas.Editor;

/// <summary>
/// Helper utilities for sampling the baked Curve3D data without relying on the editor-only API surface.
/// </summary>
public static class CurveCanvasMath
{
    public static float GetClosestOffset(Curve3D curve, Vector3 target, out Vector3 closestPoint)
    {
        var bakedPoints = curve.GetBakedPoints();
        closestPoint = bakedPoints.Length > 0 ? bakedPoints[0] : target;

        if (bakedPoints.Length < 2)
        {
            return 0f;
        }

        var bestDistanceSq = float.MaxValue;
        var bestOffset = 0f;
        var accumulated = 0f;

        for (int i = 0; i < bakedPoints.Length - 1; i++)
        {
            var start = bakedPoints[i];
            var end = bakedPoints[i + 1];
            var segment = end - start;
            var segmentLength = segment.Length();
            if (segmentLength <= Mathf.Epsilon)
            {
                continue;
            }

            var toTarget = target - start;
            var projection = Mathf.Clamp(toTarget.Dot(segment) / (segmentLength * segmentLength), 0f, 1f);
            var projectedPoint = start + segment * projection;
            var distanceSq = projectedPoint.DistanceSquaredTo(target);

            if (distanceSq < bestDistanceSq)
            {
                bestDistanceSq = distanceSq;
                closestPoint = projectedPoint;
                bestOffset = accumulated + segmentLength * projection;
            }

            accumulated += segmentLength;
        }

        return bestOffset;
    }
}
