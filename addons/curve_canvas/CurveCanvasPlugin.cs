using Godot;

namespace CurveCanvas.Editor;

[Tool]
public partial class CurveCanvasPlugin : EditorPlugin
{
    private const float SelectionRadius = 2.0f;
    private TrackMeshGenerator? _currentTrack;
    private int _selectedPointIndex = -1;
    private Vector3 _selectedPointOriginalPosition = Vector3.Zero;
    private bool _isDraggingPoint;
    private bool _pointMovedDuringDrag;

    private bool _propBrushMode;
    private readonly PropBrushTool _propBrushTool = new();

    public override void _EnterTree()
    {
        GD.Print("CurveCanvas Initialized");
    }

    public override void _ExitTree()
    {
        _currentTrack = null;
        _selectedPointIndex = -1;
        _propBrushTool.CancelStroke();
        _propBrushMode = false;
    }

    public override bool _Handles(GodotObject @object)
    {
        return @object is TrackMeshGenerator;
    }

    public override void _Edit(GodotObject @object)
    {
        _currentTrack = @object as TrackMeshGenerator;
        _selectedPointIndex = -1;
        _isDraggingPoint = false;
        _pointMovedDuringDrag = false;
        _propBrushTool.ConfigureFromTrack(_currentTrack);
    }

    public override int _Forward3DGuiInput(Camera3D camera, InputEvent @event)
    {
        if (_currentTrack?.Curve == null || camera == null || @event == null)
        {
            return (int)AfterGuiInput.Pass;
        }

        if (@event is InputEventKey keyEvent && keyEvent.Pressed && !keyEvent.Echo)
        {
            if (keyEvent.Keycode == Key.B)
            {
                _propBrushMode = !_propBrushMode;
                GD.Print(_propBrushMode ? "Prop Brush enabled (drag with LMB)" : "Prop Brush disabled");
                if (!_propBrushMode)
                {
                    _propBrushTool.CancelStroke();
                }

                return (int)AfterGuiInput.Stop;
            }

            if (keyEvent.Keycode == Key.Escape && _propBrushTool.IsStrokeActive)
            {
                _propBrushTool.CancelStroke();
                return (int)AfterGuiInput.Stop;
            }
        }

        if (_propBrushMode)
        {
            return HandlePropBrushInput(camera, @event);
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
                        _selectedPointOriginalPosition = _currentTrack.Curve.GetPointPosition(index);
                        _isDraggingPoint = true;
                        _pointMovedDuringDrag = false;
                        return (int)AfterGuiInput.Stop;
                    }
                }
                else
                {
                    if (_isDraggingPoint && _pointMovedDuringDrag && _selectedPointIndex >= 0)
                    {
                        var newPosition = _currentTrack.Curve.GetPointPosition(_selectedPointIndex);
                        CommitMoveUndo(_selectedPointIndex, _selectedPointOriginalPosition, newPosition);
                    }

                    _isDraggingPoint = false;
                    _pointMovedDuringDrag = false;
                    _selectedPointIndex = -1;
                    return (int)AfterGuiInput.Stop;
                }
            }
            else if (mouseButton.ButtonIndex == MouseButton.Right && mouseButton.Pressed)
            {
                var index = FindNearbyPoint(_currentTrack.Curve, planarHitPoint, SelectionRadius);
                if (index >= 0)
                {
                    CommitDeleteUndo(index);
                    _selectedPointIndex = -1;
                    return (int)AfterGuiInput.Stop;
                }
            }
        }
        else if (@event is InputEventMouseMotion mouseMotion)
        {
            if (_isDraggingPoint && _selectedPointIndex >= 0 && (mouseMotion.ButtonMask & MouseButtonMask.Left) != 0)
            {
                var planarHitPoint = GetPlanarHit(camera, mouseMotion.Position);
                _currentTrack.Curve.SetPointPosition(_selectedPointIndex, planarHitPoint);
                _pointMovedDuringDrag = true;
                return (int)AfterGuiInput.Stop;
            }
        }

        return (int)AfterGuiInput.Pass;
    }

    private int HandlePropBrushInput(Camera3D camera, InputEvent @event)
    {
        if (@event is InputEventMouseButton mouseButton)
        {
            var planarPoint = GetPlanarHit(camera, mouseButton.Position);
            if (mouseButton.ButtonIndex == MouseButton.Left)
            {
                if (mouseButton.Pressed)
                {
                    if (_propBrushTool.BeginStroke(planarPoint))
                    {
                        return (int)AfterGuiInput.Stop;
                    }
                }
                else
                {
                    _propBrushTool.EndStroke(GetUndoRedo());
                    return (int)AfterGuiInput.Stop;
                }
            }

            if (mouseButton.ButtonIndex == MouseButton.Right && mouseButton.Pressed)
            {
                _propBrushTool.CancelStroke();
                return (int)AfterGuiInput.Stop;
            }
        }
        else if (@event is InputEventMouseMotion mouseMotion)
        {
            if (_propBrushTool.IsStrokeActive && (mouseMotion.ButtonMask & MouseButtonMask.Left) != 0)
            {
                var planarPoint = GetPlanarHit(camera, mouseMotion.Position);
                _propBrushTool.AccumulateStroke(planarPoint);
                return (int)AfterGuiInput.Stop;
            }
        }

        return (int)AfterGuiInput.Pass;
    }

    private void CommitMoveUndo(int index, Vector3 originalPosition, Vector3 newPosition)
    {
        if (_currentTrack?.Curve == null)
        {
            return;
        }

        if (originalPosition.IsEqualApprox(newPosition))
        {
            return;
        }

        var undoRedo = GetUndoRedo();
        undoRedo.CreateAction("Move Track Point");
        undoRedo.AddDoMethod(_currentTrack.Curve, Curve3D.MethodName.SetPointPosition, index, newPosition);
        undoRedo.AddUndoMethod(_currentTrack.Curve, Curve3D.MethodName.SetPointPosition, index, originalPosition);
        undoRedo.CommitAction();
    }

    private void CommitDeleteUndo(int index)
    {
        if (_currentTrack?.Curve == null)
        {
            return;
        }

        var curve = _currentTrack.Curve;
        var position = curve.GetPointPosition(index);
        var inHandle = curve.GetPointIn(index);
        var outHandle = curve.GetPointOut(index);

        var undoRedo = GetUndoRedo();
        undoRedo.CreateAction("Delete Track Point");
        undoRedo.AddDoMethod(curve, Curve3D.MethodName.RemovePoint, index);
        undoRedo.AddUndoMethod(curve, Curve3D.MethodName.AddPoint, position, inHandle, outHandle, index);
        undoRedo.CommitAction();
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
