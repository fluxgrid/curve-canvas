using System;
using System.Collections.Generic;
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

    private const float HandleRayLength = 4096f;

    public SandboxState CurrentState => _currentState;

    private SandboxState _currentState = SandboxState.Select;
    private RuntimeCamera3D? _runtimeCamera;
    private GodotObject? _curveCanvas;
    private TrackMeshGenerator? _trackGenerator;
    private Node? _propContainer;
    private UndoRedo? _undoRedo;
    private bool _isPointerDown;
    private bool _propStrokeActive;
    private Vector3? _lastPropSpawnPosition;
    private readonly List<Node3D> _currentStrokeNodes = new();
    private readonly HashSet<Node3D> _strokeNodeSet = new();
    private bool _currentStrokeIsErase;
    private Node? _strokeContainer;
    private Node? _strokeOwner;
    private int _draggedPointIndex = -1;
    private Vector3 _dragStartPosition = Vector3.Zero;
    private Vector3 _dragCurrentPosition = Vector3.Zero;

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

    public void ConfigureUndoRedo(UndoRedo? undoRedo)
    {
        _undoRedo = undoRedo;
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
        // Selection behavior is driven through spline handles.
        _ = position;
    }

    private void HandleDrawSpline(Vector3 position)
    {
        var track = GetTrackGenerator();
        var curve = track?.Curve;
        if (curve == null)
        {
            return;
        }

        position.Z = 0f;
        var insertionIndex = curve.GetPointCount();

        if (_undoRedo == null)
        {
            curve.AddPoint(position);
            return;
        }

        var curveRef = curve;
        var point = position;
        _undoRedo.CreateAction("Add Track Point");
        _undoRedo.AddDoMethod(Callable.From(() => curveRef.AddPoint(point)));
        _undoRedo.AddUndoMethod(Callable.From(() => curveRef.RemovePoint(insertionIndex)));
        _undoRedo.CommitAction();
    }

    private void HandlePropBrush(Vector3 position, bool isDrag)
    {
        if (_currentStrokeIsErase)
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
            if (_currentState == SandboxState.PropBrush)
            {
                BeginPropStroke();
            }

            if (_currentState == SandboxState.Select && TryBeginHandleDrag(mouseButton.Position))
            {
                GetViewport()?.SetInputAsHandled();
                return;
            }

            ProcessPointerEvent(mouseButton.Position, isDrag: false);
        }
        else
        {
            _isPointerDown = false;
            _lastPropSpawnPosition = null;
            if (_currentState == SandboxState.PropBrush)
            {
                CommitStroke();
            }
            else if (_draggedPointIndex != -1)
            {
                CommitHandleDrag();
            }
            else
            {
                ClearStrokeState();
            }
        }
    }

    private void HandleMouseMotion(InputEventMouseMotion mouseMotion)
    {
        if (!_isPointerDown || (mouseMotion.ButtonMask & MouseButtonMask.Left) == 0)
        {
            return;
        }

        if (_currentState == SandboxState.Select && _draggedPointIndex != -1)
        {
            UpdateDraggedPoint(mouseMotion.Position);
            GetViewport()?.SetInputAsHandled();
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
            if (_currentState == SandboxState.PropBrush)
            {
                BeginPropStroke();
            }

            if (_currentState == SandboxState.Select && TryBeginHandleDrag(touchEvent.Position))
            {
                GetViewport()?.SetInputAsHandled();
                return;
            }

            ProcessPointerEvent(touchEvent.Position, isDrag: false);
        }
        else
        {
            _isPointerDown = false;
            _lastPropSpawnPosition = null;
            if (_currentState == SandboxState.PropBrush)
            {
                CommitStroke();
            }
            else if (_draggedPointIndex != -1)
            {
                CommitHandleDrag();
            }
            else
            {
                ClearStrokeState();
            }
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
        var node = CreatePropNode(position);
        if (node == null)
        {
            return false;
        }

        if (_strokeNodeSet.Add(node))
        {
            _currentStrokeNodes.Add(node);
        }

        return true;
    }

    private void ErasePropsNear(Vector3 position)
    {
        var container = _strokeContainer ?? GetPropContainer();
        if (container == null)
        {
            GD.PushWarning("[RuntimeInputMultiplexer] Prop container not assigned; cannot erase props.");
            return;
        }

        const float eraserRadius = 2.0f;
        var matches = new List<Node3D>();
        foreach (var child in container.GetChildren())
        {
            if (child is not Node3D node)
            {
                continue;
            }

            if (_strokeNodeSet.Contains(node))
            {
                continue;
            }

            if (node.GlobalPosition.DistanceTo(position) < eraserRadius)
            {
                matches.Add(node);
            }
        }

        foreach (var node in matches)
        {
            container.RemoveChild(node);
            if (_strokeNodeSet.Add(node))
            {
                _currentStrokeNodes.Add(node);
            }
        }
    }

    private void BeginPropStroke()
    {
        _propStrokeActive = true;
        _currentStrokeNodes.Clear();
        _strokeNodeSet.Clear();
        _currentStrokeIsErase = Input.IsKeyPressed(Key.Shift);
        _strokeContainer = GetPropContainer() ?? GetTrackGenerator();
        _strokeOwner = DetermineOwner(_strokeContainer);
        if (!_currentStrokeIsErase)
        {
            _lastPropSpawnPosition = null;
        }
    }

    private void CommitStroke()
    {
        if (!_propStrokeActive)
        {
            ClearStrokeState();
            return;
        }

        _propStrokeActive = false;

        if (_currentStrokeNodes.Count == 0)
        {
            ClearStrokeState();
            return;
        }

        var container = _strokeContainer ?? GetPropContainer();
        if (container == null)
        {
            GD.PushWarning("[RuntimeInputMultiplexer] Prop container not assigned; discarding stroke.");
            ClearStrokeState();
            return;
        }

        if (_undoRedo == null)
        {
            if (_currentStrokeIsErase)
            {
                foreach (var node in _currentStrokeNodes)
                {
                    node.QueueFree();
                }
            }
            else
            {
                foreach (var node in _currentStrokeNodes)
                {
                    container.AddChild(node);
                    if (_strokeOwner != null)
                    {
                        node.Owner = _strokeOwner;
                    }
                }
            }

            ClearStrokeState(false);
            return;
        }

        if (_currentStrokeIsErase)
        {
            foreach (var node in _currentStrokeNodes)
            {
                if (node.GetParent() == null)
                {
                    container.AddChild(node);
                }

                if (_strokeOwner != null)
                {
                    node.Owner = _strokeOwner;
                }
            }
        }

        var actionName = _currentStrokeIsErase ? "Erase Props" : "Paint Props";
        _undoRedo.CreateAction(actionName);

        if (_currentStrokeIsErase)
        {
            foreach (var node in _currentStrokeNodes)
            {
                _undoRedo.AddDoMethod(Callable.From(() => container.RemoveChild(node)));
                _undoRedo.AddDoReference(node);
                _undoRedo.AddUndoMethod(Callable.From(() => container.AddChild(node)));
                if (_strokeOwner != null)
                {
                    _undoRedo.AddUndoMethod(Callable.From(() => node.SetOwner(_strokeOwner)));
                }
            }
        }
        else
        {
            foreach (var node in _currentStrokeNodes)
            {
                _undoRedo.AddDoMethod(Callable.From(() => container.AddChild(node)));
                if (_strokeOwner != null)
                {
                    var owner = _strokeOwner;
                    _undoRedo.AddDoMethod(Callable.From(() => node.SetOwner(owner)));
                }
                _undoRedo.AddUndoMethod(Callable.From(() => container.RemoveChild(node)));
                _undoRedo.AddUndoReference(node);
            }
        }

        _undoRedo.CommitAction();
        ClearStrokeState(false);
    }

    private void ClearStrokeState(bool restoreNodes = true)
    {
        if (restoreNodes)
        {
            var container = _strokeContainer ?? GetPropContainer();
            if (container != null)
            {
                foreach (var node in _currentStrokeNodes)
                {
                    if (_currentStrokeIsErase && node.GetParent() == null)
                    {
                        container.AddChild(node);
                        if (_strokeOwner != null)
                        {
                            node.Owner = _strokeOwner;
                        }
                    }
                    else if (!_currentStrokeIsErase)
                    {
                        node.QueueFree();
                    }
                }
            }
            else
            {
                foreach (var node in _currentStrokeNodes)
                {
                    node.QueueFree();
                }
            }
        }

        _propStrokeActive = false;
        _currentStrokeNodes.Clear();
        _strokeNodeSet.Clear();
        _strokeContainer = null;
        _strokeOwner = null;
        _currentStrokeIsErase = false;
    }

    private static Node? DetermineOwner(Node? node)
    {
        if (node == null)
        {
            return null;
        }

        return node.Owner ?? node.GetTree()?.CurrentScene;
    }

    private bool TryBeginHandleDrag(Vector2 screenPosition)
    {
        var camera = GetRuntimeCamera();
        var track = GetTrackGenerator();
        var curve = track?.Curve;
        if (camera == null || curve == null)
        {
            return false;
        }

        var world = camera.GetWorld3D();
        if (world == null)
        {
            return false;
        }

        var origin = camera.ProjectRayOrigin(screenPosition);
        var direction = camera.ProjectRayNormal(screenPosition);
        var query = PhysicsRayQueryParameters3D.Create(origin, origin + direction * HandleRayLength);
        query.CollisionMask = RuntimeSplineHandles.HandleCollisionLayer;
        var hit = world.DirectSpaceState.IntersectRay(query);
        if (hit.Count == 0)
        {
            return false;
        }

        if (!hit.TryGetValue("collider", out var colliderVariant))
        {
            return false;
        }

        var collider = colliderVariant.AsGodotObject() as CollisionObject3D;
        if (collider == null)
        {
            return false;
        }

        Variant indexVariant = default;
        if (collider.HasMeta("point_index"))
        {
            indexVariant = collider.GetMeta("point_index");
        }
        else if (collider.GetParent() is Node parent && parent.HasMeta("point_index"))
        {
            indexVariant = parent.GetMeta("point_index");
        }

        if (indexVariant.VariantType != Variant.Type.Int)
        {
            return false;
        }

        var index = (int)indexVariant;
        if (index < 0 || index >= curve.GetPointCount())
        {
            return false;
        }

        _draggedPointIndex = index;
        _dragStartPosition = curve.GetPointPosition(index);
        _dragCurrentPosition = _dragStartPosition;
        return true;
    }

    private void UpdateDraggedPoint(Vector2 screenPosition)
    {
        if (_draggedPointIndex < 0)
        {
            return;
        }

        var camera = GetRuntimeCamera();
        var track = GetTrackGenerator();
        var curve = track?.Curve;
        if (camera == null || curve == null)
        {
            return;
        }

        var intersection = camera.GetZZeroIntersection(screenPosition);
        if (intersection == null)
        {
            return;
        }

        var position = intersection.Value;
        position.Z = 0f;
        curve.SetPointPosition(_draggedPointIndex, position);
        _dragCurrentPosition = position;
    }

    private void CommitHandleDrag()
    {
        if (_draggedPointIndex < 0)
        {
            return;
        }

        var track = GetTrackGenerator();
        var curve = track?.Curve;
        if (curve == null)
        {
            _draggedPointIndex = -1;
            return;
        }

        var index = _draggedPointIndex;
        var start = _dragStartPosition;
        var end = _dragCurrentPosition;
        _draggedPointIndex = -1;

        if (_undoRedo == null || start.IsEqualApprox(end))
        {
            return;
        }

        var curveRef = curve;
        _undoRedo.CreateAction("Move Track Point");
        _undoRedo.AddDoMethod(Callable.From(() => curveRef.SetPointPosition(index, end)));
        _undoRedo.AddUndoMethod(Callable.From(() => curveRef.SetPointPosition(index, start)));
        _undoRedo.CommitAction();
    }

    private Node3D? CreatePropNode(Vector3 planarPosition)
    {
        var track = GetTrackGenerator();
        if (track?.Curve == null)
        {
            GD.PushWarning("[RuntimeInputMultiplexer] Track generator or curve missing; cannot paint props.");
            return null;
        }

        var registry = track.PropBrushRegistry;
        if (registry == null)
        {
            GD.PushWarning("[RuntimeInputMultiplexer] PropBrushRegistry not assigned on TrackMeshGenerator.");
            return null;
        }

        if (string.IsNullOrWhiteSpace(track.PropBrushObjectId))
        {
            GD.PushWarning("[RuntimeInputMultiplexer] PropBrushObjectId is empty.");
            return null;
        }

        var snapper = new SceneryPlaneSnapper
        {
            Name = $"PropStroke_{Guid.NewGuid():N}",
            Registry = registry,
            ObjectID = track.PropBrushObjectId,
            TrackPath = track,
            DepthZ = track.PropBrushDepthZ,
            PlaneIndex = track.PropBrushPlaneIndex
        };

        var projected = ProjectBrushPosition(planarPosition, track);
        snapper.Position = projected;
        return snapper;
    }

    private static Vector3 ProjectBrushPosition(Vector3 position, TrackMeshGenerator track)
    {
        var curve = track.Curve;
        if (curve == null)
        {
            return position;
        }

        position.Z = 0f;
        var offset = CurveCanvasMath.GetClosestOffset(curve, position, out _);
        var snapped = curve.SampleBaked(offset);
        return new Vector3(position.X, snapped.Y, track.PropBrushDepthZ);
    }
}
