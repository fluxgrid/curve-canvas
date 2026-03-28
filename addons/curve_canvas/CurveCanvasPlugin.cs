using Godot;

namespace CurveCanvas.Editor;

[Tool]
public partial class CurveCanvasPlugin : EditorPlugin
{
    private const float SelectionRadius = 2.0f;
    private TrackMeshGenerator? _currentTrack;
    private int _selectedPointIndex = -1;

    public override void _EnterTree()
    {
        GD.Print("CurveCanvas Initialized");
    }

    public override void _ExitTree()
    {
        _currentTrack = null;
        _selectedPointIndex = -1;
    }

    public override bool _Handles(GodotObject @object)
    {
        return @object is TrackMeshGenerator;
    }

    public override void _Edit(GodotObject @object)
    {
        _currentTrack = @object as TrackMeshGenerator;
        _selectedPointIndex = -1;
    }

    public override int _Forward3DGuiInput(Camera3D camera, InputEvent @event)
    {
        if (_currentTrack?.Curve == null || camera == null || @event == null)
        {
            return (int)AfterGuiInput.Pass;
        }

        if (@event is InputEventMouseButton mouseButton)
        {
            var planarHitPoint = GetPlanarHit(camera, mouseButton.Position);
            if (mouseButton.ButtonIndex == MouseButton.Left)
            {
                if (mouseButton.Pressed)
                {
                    var index = FindNearbyPoint(_currentTrack.Curve, planarHitPoint, SelectionRadius);
                    if (index >= 0)
                    {
                        _selectedPointIndex = index;
                        return (int)AfterGuiInput.Stop;
                    }
                }
                else
                {
                    _selectedPointIndex = -1;
                }
            }
            else if (mouseButton.ButtonIndex == MouseButton.Right && mouseButton.Pressed)
            {
                var index = FindNearbyPoint(_currentTrack.Curve, planarHitPoint, SelectionRadius);
                if (index >= 0)
                {
                    _currentTrack.Curve.RemovePoint(index);
                    _selectedPointIndex = -1;
                    return (int)AfterGuiInput.Stop;
                }
            }
        }
        else if (@event is InputEventMouseMotion mouseMotion)
        {
            if (_selectedPointIndex >= 0 && (mouseMotion.ButtonMask & MouseButtonMask.Left) != 0)
            {
                var planarHitPoint = GetPlanarHit(camera, mouseMotion.Position);
                _currentTrack.Curve.SetPointPosition(_selectedPointIndex, planarHitPoint);
                return (int)AfterGuiInput.Stop;
            }
        }

        return (int)AfterGuiInput.Pass;
    }

    private static Vector3 GetPlanarHit(Camera3D camera, Vector2 screenPosition)
    {
        var origin = camera.ProjectRayOrigin(screenPosition);
        var direction = camera.ProjectRayNormal(screenPosition);
        return ProjectRayToPlane(origin, direction);
    }

    private static Vector3 ProjectRayToPlane(Vector3 origin, Vector3 direction)
    {
        const float planeZ = 0f;
        if (Mathf.IsZeroApprox(direction.Z))
        {
            return new Vector3(origin.X, origin.Y, planeZ);
        }

        var t = (planeZ - origin.Z) / direction.Z;
        var hit = origin + direction * t;
        hit.Z = planeZ;
        return hit;
    }

    private static int FindNearbyPoint(Curve3D curve, Vector3 target, float radius)
    {
        var closestIndex = -1;
        var closestDistance = radius;
        var pointCount = curve.GetPointCount();

        for (var i = 0; i < pointCount; i++)
        {
            var position = curve.GetPointPosition(i);
            position.Z = 0f;
            var distance = position.DistanceTo(target);
            if (distance <= closestDistance)
            {
                closestDistance = distance;
                closestIndex = i;
            }
        }

        return closestIndex;
    }
}
