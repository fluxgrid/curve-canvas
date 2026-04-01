using System.Collections.Generic;
using Godot;

namespace CurveCanvas.Editor;

/// <summary>
/// Camera helper that can compute intersections against the Z = 0 plane without physics raycasts.
/// </summary>
public partial class RuntimeCamera3D : Camera3D
{
    private const float MinZoomZ = 5.0f;
    private const float MaxZoomZ = 100.0f;
    private const float MouseZoomFactor = 1.2f;
    private const float MousePanSpeed = 0.002f;
    private const float TouchZoomSpeed = 0.05f;
    private const float TouchPanSpeed = 0.002f;

    private bool _isMousePanning;
    private readonly Dictionary<int, Vector2> _activeTouches = new();
    private float _lastPinchDistance;
    private Vector2 _lastPinchMidpoint = Vector2.Zero;

    public bool IsMultiTouchGestureActive => _activeTouches.Count > 1;

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
            case InputEventScreenTouch screenTouch:
                HandleScreenTouch(screenTouch);
                break;
            case InputEventScreenDrag screenDrag:
                HandleScreenDrag(screenDrag);
                break;
        }
    }

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

    private void HandleMouseButton(InputEventMouseButton mouseButton)
    {
        switch (mouseButton.ButtonIndex)
        {
            case MouseButton.WheelUp when mouseButton.Pressed:
                ZoomCamera(Position.Z / MouseZoomFactor);
                break;
            case MouseButton.WheelDown when mouseButton.Pressed:
                ZoomCamera(Position.Z * MouseZoomFactor);
                break;
            case MouseButton.Middle:
                _isMousePanning = mouseButton.Pressed;
                GetViewport()?.SetInputAsHandled();
                break;
        }
    }

    private void HandleMouseMotion(InputEventMouseMotion mouseMotion)
    {
        if (!_isMousePanning)
        {
            return;
        }

        var delta = mouseMotion.Relative;
        PanCamera(delta, MousePanSpeed);
        GetViewport()?.SetInputAsHandled();
    }

    private void HandleScreenTouch(InputEventScreenTouch screenTouch)
    {
        if (screenTouch.Pressed)
        {
            _activeTouches[screenTouch.Index] = screenTouch.Position;
            if (_activeTouches.Count == 2)
            {
                InitializePinchMetrics();
            }
        }
        else
        {
            _activeTouches.Remove(screenTouch.Index);
            if (_activeTouches.Count < 2)
            {
                _lastPinchDistance = 0f;
                _lastPinchMidpoint = Vector2.Zero;
            }
        }
    }

    private void HandleScreenDrag(InputEventScreenDrag drag)
    {
        _activeTouches[drag.Index] = drag.Position;

        if (_activeTouches.Count != 2)
        {
            return;
        }

        GetTouchPair(out var first, out var second);
        var distance = first.DistanceTo(second);
        var midpoint = (first + second) * 0.5f;

        if (_lastPinchDistance <= 0f)
        {
            _lastPinchDistance = distance;
            _lastPinchMidpoint = midpoint;
            return;
        }

        var distanceDelta = distance - _lastPinchDistance;
        ApplyTouchZoom(distanceDelta);

        var midpointDelta = midpoint - _lastPinchMidpoint;
        ApplyTouchPan(midpointDelta);

        _lastPinchDistance = distance;
        _lastPinchMidpoint = midpoint;
        GetViewport()?.SetInputAsHandled();
    }

    private void ZoomCamera(float targetZ)
    {
        var clamped = Mathf.Clamp(targetZ, MinZoomZ, MaxZoomZ);
        Position = new Vector3(Position.X, Position.Y, clamped);
        GetViewport()?.SetInputAsHandled();
    }

    private void PanCamera(Vector2 delta, float speed)
    {
        var scaled = Position.Z * speed;
        var offset = new Vector3(delta.X, -delta.Y, 0f) * scaled;
        Position -= offset;
    }

    private void ApplyTouchZoom(float delta)
    {
        if (Mathf.IsZeroApprox(delta))
        {
            return;
        }

        var newZ = Position.Z - delta * TouchZoomSpeed;
        ZoomCamera(newZ);
    }

    private void ApplyTouchPan(Vector2 delta)
    {
        if (delta == Vector2.Zero)
        {
            return;
        }

        PanCamera(delta, TouchPanSpeed);
    }

    private void InitializePinchMetrics()
    {
        GetTouchPair(out var first, out var second);
        _lastPinchDistance = first.DistanceTo(second);
        _lastPinchMidpoint = (first + second) * 0.5f;
    }

    private void GetTouchPair(out Vector2 first, out Vector2 second)
    {
        first = Vector2.Zero;
        second = Vector2.Zero;

        var i = 0;
        foreach (var entry in _activeTouches)
        {
            if (i == 0)
            {
                first = entry.Value;
            }
            else
            {
                second = entry.Value;
                break;
            }

            i++;
        }
    }
}
