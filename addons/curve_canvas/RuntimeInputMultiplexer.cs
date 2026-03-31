using System;
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

    [Export]
    public NodePath TrackGeneratorPath { get; set; } = new();

    [Export]
    public NodePath PropContainerPath { get; set; } = new();

    [Export(PropertyHint.Range, "0.25,20,0.25")]
    public float PropBrushFallbackInterval { get; set; } = 3.0f;

    public SandboxState CurrentState => _currentState;

    private SandboxState _currentState = SandboxState.Select;
    private RuntimeCamera3D? _runtimeCamera;
    private GodotObject? _curveCanvas;
    private TrackMeshGenerator? _trackGenerator;
    private Node? _propContainer;
    private bool _isPointerDown;
    private Vector3? _lastPropSpawnPosition;

    public override void _Ready()
    {
        ResolveDependencies();
    }

    public override void _UnhandledInput(InputEvent @event)
    {
        switch (@event)
        {
            case InputEventMouseButton mouseButton:
                HandleMouseButton(mouseButton);
                break;
            case InputEventMouseMotion mouseMotion:
                HandleMouseMotion(mouseMotion);
                break;
            case InputEventScreenTouch touchEvent:
                HandleTouchEvent(touchEvent);
                break;
        }
    }

    /// <summary>
    /// Allows external systems to update the sandbox tool mode.
    /// </summary>
    public void SetSandboxState(SandboxState newState)
    {
        _currentState = newState;
    }

    private void DispatchInteraction(Vector3 hitPosition, bool isDrag)
    {
        switch (_currentState)
        {
            case SandboxState.Select:
                if (!isDrag)
                {
                    HandleSelect(hitPosition);
                }
                break;
            case SandboxState.DrawSpline:
                if (!isDrag)
                {
                    HandleDrawSpline(hitPosition);
                }
                break;
            case SandboxState.PropBrush:
                HandlePropBrush(hitPosition, isDrag);
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

    private void HandlePropBrush(Vector3 position, bool isDrag)
    {
        if (Input.IsKeyPressed(Key.Shift))
        {
            ErasePropsNear(position);
            return;
        }

        if (isDrag && !ShouldSpawnProp(position))
        {
            return;
        }

        if (TrySpawnProp(position))
        {
            _lastPropSpawnPosition = position;
        }
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

        if (!TrackGeneratorPath.IsEmpty)
        {
            _trackGenerator = GetNodeOrNull<TrackMeshGenerator>(TrackGeneratorPath);
        }

        if (!PropContainerPath.IsEmpty)
        {
            _propContainer = GetNodeOrNull(PropContainerPath);
        }
    }

    private TrackMeshGenerator? GetTrackGenerator()
    {
        if (_trackGenerator != null && IsInstanceValid(_trackGenerator))
        {
            return _trackGenerator;
        }

        if (!TrackGeneratorPath.IsEmpty)
        {
            _trackGenerator = GetNodeOrNull<TrackMeshGenerator>(TrackGeneratorPath);
        }

        return _trackGenerator;
    }

    private Node? GetPropContainer()
    {
        if (_propContainer != null && IsInstanceValid(_propContainer))
        {
            return _propContainer;
        }

        if (!PropContainerPath.IsEmpty)
        {
            _propContainer = GetNodeOrNull(PropContainerPath);
        }

        return _propContainer;
    }

    private float GetSpawnInterval()
    {
        var track = GetTrackGenerator();
        if (track?.PropBrushSampleInterval > 0f)
        {
            return track.PropBrushSampleInterval;
        }

        return Math.Max(0.1f, PropBrushFallbackInterval);
    }

    private void HandleMouseButton(InputEventMouseButton mouseButton)
    {
        if (mouseButton.ButtonIndex != MouseButton.Left)
        {
            return;
        }

        if (mouseButton.Pressed)
        {
            _isPointerDown = true;
            ProcessPointerEvent(mouseButton.Position, isDrag: false);
        }
        else
        {
            _isPointerDown = false;
            _lastPropSpawnPosition = null;
        }
    }

    private void HandleMouseMotion(InputEventMouseMotion mouseMotion)
    {
        if (!_isPointerDown || (mouseMotion.ButtonMask & MouseButtonMask.Left) == 0)
        {
            return;
        }

        ProcessPointerEvent(mouseMotion.Position, isDrag: true);
    }

    private void HandleTouchEvent(InputEventScreenTouch touchEvent)
    {
        if (touchEvent.Index != 0)
        {
            return;
        }

        if (touchEvent.Pressed)
        {
            _isPointerDown = true;
            ProcessPointerEvent(touchEvent.Position, isDrag: false);
        }
        else
        {
            _isPointerDown = false;
            _lastPropSpawnPosition = null;
        }
    }

    private void ProcessPointerEvent(Vector2 screenPosition, bool isDrag)
    {
        var camera = GetRuntimeCamera();
        if (camera == null)
        {
            return;
        }

        var hit = camera.GetZZeroIntersection(screenPosition);
        if (hit == null)
        {
            return;
        }

        if (isDrag && _currentState != SandboxState.PropBrush)
        {
            return;
        }

        DispatchInteraction(hit.Value, isDrag);
        GetViewport()?.SetInputAsHandled();
    }

    private bool ShouldSpawnProp(Vector3 position)
    {
        if (_lastPropSpawnPosition == null)
        {
            return true;
        }

        var distance = position.DistanceTo(_lastPropSpawnPosition.Value);
        return distance >= GetSpawnInterval();
    }

    private bool TrySpawnProp(Vector3 position)
    {
        var canvas = GetCurveCanvas();
        if (canvas != null)
        {
            if (canvas.HasMethod("SpawnProp"))
            {
                canvas.Call("SpawnProp", position);
                return true;
            }

            if (canvas.HasMethod("HandlePropBrush"))
            {
                canvas.Call("HandlePropBrush", position);
                return true;
            }
        }

        GD.Print($"[RuntimeInputMultiplexer] PropBrush placeholder at {position}");
        return true;
    }

    private void ErasePropsNear(Vector3 position)
    {
        var container = GetPropContainer();
        if (container == null)
        {
            GD.PushWarning("[RuntimeInputMultiplexer] Prop container not assigned; cannot erase props.");
            return;
        }

        const float eraserRadius = 2.0f;
        foreach (var child in container.GetChildren())
        {
            if (child is not Node3D node)
            {
                continue;
            }

            if (node.GlobalPosition.DistanceTo(position) < eraserRadius)
            {
                node.QueueFree();
            }
        }
    }
}
