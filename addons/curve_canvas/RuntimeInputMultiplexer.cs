using Godot;

namespace CurveCanvas.Editor;

/// <summary>
/// Routes runtime input events to the appropriate sandbox tool without interfering with UI Controls.
/// </summary>
public partial class RuntimeInputMultiplexer : Node
{
    public enum SandboxState
    {
        Select,
        DrawSpline,
        PropBrush
    }

    [Export]
    public NodePath RuntimeCameraPath { get; set; } = new();

    [Export]
    public NodePath CurveCanvasPath { get; set; } = new();

    public SandboxState CurrentState => _currentState;

    private SandboxState _currentState = SandboxState.Select;
    private RuntimeCamera3D? _runtimeCamera;
    private GodotObject? _curveCanvas;

    public override void _Ready()
    {
        ResolveDependencies();
    }

    public override void _UnhandledInput(InputEvent @event)
    {
        var pointer = GetPointerEvent(@event);
        if (pointer == null)
        {
            return;
        }

        var camera = GetRuntimeCamera();
        if (camera == null)
        {
            return;
        }

        var hit = camera.GetZZeroIntersection(pointer.Value.Position);
        if (hit == null)
        {
            return;
        }

        DispatchInteraction(hit.Value);
        GetViewport()?.SetInputAsHandled();
    }

    /// <summary>
    /// Allows external systems to update the sandbox tool mode.
    /// </summary>
    public void SetSandboxState(SandboxState newState)
    {
        _currentState = newState;
    }

    private void DispatchInteraction(Vector3 hitPosition)
    {
        switch (_currentState)
        {
            case SandboxState.Select:
                HandleSelect(hitPosition);
                break;
            case SandboxState.DrawSpline:
                HandleDrawSpline(hitPosition);
                break;
            case SandboxState.PropBrush:
                HandlePropBrush(hitPosition);
                break;
        }
    }

    private void HandleSelect(Vector3 position)
    {
        GD.Print($"[RuntimeInputMultiplexer] Select placeholder at {position}");
    }

    private void HandleDrawSpline(Vector3 position)
    {
        var canvas = GetCurveCanvas();
        if (canvas != null && canvas.HasMethod("AddPoint"))
        {
            canvas.Call("AddPoint", position);
            return;
        }

        GD.Print($"[RuntimeInputMultiplexer] CurveCanvas.AddPoint placeholder at {position}");
    }

    private void HandlePropBrush(Vector3 position)
    {
        GD.Print($"[RuntimeInputMultiplexer] PropBrush placeholder at {position}");
    }

    private RuntimeCamera3D? GetRuntimeCamera()
    {
        if (_runtimeCamera != null && IsInstanceValid(_runtimeCamera))
        {
            return _runtimeCamera;
        }

        if (!RuntimeCameraPath.IsEmpty)
        {
            _runtimeCamera = GetNodeOrNull<RuntimeCamera3D>(RuntimeCameraPath);
        }

        return _runtimeCamera;
    }

    private GodotObject? GetCurveCanvas()
    {
        if (_curveCanvas != null && IsInstanceValid(_curveCanvas))
        {
            return _curveCanvas;
        }

        if (!CurveCanvasPath.IsEmpty)
        {
            _curveCanvas = GetNodeOrNull(CurveCanvasPath);
        }

        return _curveCanvas;
    }

    private void ResolveDependencies()
    {
        if (!RuntimeCameraPath.IsEmpty)
        {
            _runtimeCamera = GetNodeOrNull<RuntimeCamera3D>(RuntimeCameraPath);
        }

        if (!CurveCanvasPath.IsEmpty)
        {
            _curveCanvas = GetNodeOrNull(CurveCanvasPath);
        }
    }

    private static PointerEventData? GetPointerEvent(InputEvent @event)
    {
        if (@event is InputEventMouseButton mouseButton)
        {
            if (!mouseButton.Pressed || mouseButton.ButtonIndex != MouseButton.Left)
            {
                return null;
            }

            return new PointerEventData(mouseButton.Position);
        }

        if (@event is InputEventScreenTouch touchEvent)
        {
            if (!touchEvent.Pressed || touchEvent.Index != 0)
            {
                return null;
            }

            return new PointerEventData(touchEvent.Position);
        }

        return null;
    }

    private readonly record struct PointerEventData(Vector2 Position);
}
