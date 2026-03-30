using Godot;

namespace CurveCanvas.Editor;

/// <summary>
/// Camera helper that can compute intersections against the Z = 0 plane without physics raycasts.
/// </summary>
public partial class RuntimeCamera3D : Camera3D
{
    /// <summary>
    /// Calculates where the viewport ray under <paramref name="mousePosition"/> hits the Z = 0 plane.
    /// Returns null when the ray is parallel to the plane to prevent divide-by-zero errors.
    /// </summary>
    public Vector3? GetZZeroIntersection(Vector2 mousePosition)
    {
        var rayOrigin = ProjectRayOrigin(mousePosition);
        var rayDirection = ProjectRayNormal(mousePosition);

        if (Mathf.IsZeroApprox(rayDirection.Z))
        {
            return null;
        }

        const float planeZ = 0f;
        var t = (planeZ - rayOrigin.Z) / rayDirection.Z;
        var intersection = rayOrigin + rayDirection * t;
        intersection.Z = planeZ;
        return intersection;
    }
}
