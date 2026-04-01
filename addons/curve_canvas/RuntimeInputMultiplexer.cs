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

    [Export]
    public NodePath SplineHandlesPath { get; set; } = new();

    [Export(PropertyHint.Range, "0.25,20,0.25")]
    public float PropBrushFallbackInterval { get; set; } = 3.0f;

    public PackedScene? ActivePropPrefab
    {
        get => _activePropPrefab;
        set => _activePropPrefab = value;
    }

    private const float HandleRayLength = 4096f;
    private const float HandleTapThreshold = 0.01f;

    private enum DraggedHandleType
    {
        None,
        Point,
        TangentIn,
        TangentOut
    }

    public SandboxState CurrentState => _currentState;

    private SandboxState _currentState = SandboxState.Select;
    private RuntimeCamera3D? _runtimeCamera;
    private GodotObject? _curveCanvas;
    private TrackMeshGenerator? _trackGenerator;
    private Node? _propContainer;
    private RuntimeSplineHandles? _splineHandles;
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
    private bool _handleDragMoved;
    private DraggedHandleType _draggedHandleType = DraggedHandleType.None;
    private Vector3 _tangentStartValue = Vector3.Zero;
    private int _selectedPointIndex = -1;
    private SplineContextMenu? _splineContextMenu;
    private PackedScene? _activePropPrefab;

    public override void _Ready()
    {
        ResolveDependencies();
    }

    public override void _UnhandledInput(InputEvent @event)
    {
        if ((@event is InputEventScreenTouch || @event is InputEventScreenDrag) && CameraConsumingMultiTouch())
        {
            return;
        }

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

    public void ConfigureSplineContextMenu(SplineContextMenu? menu)
    {
        if (_splineContextMenu == menu)
        {
            return;
        }

        if (_splineContextMenu != null)
        {
            _splineContextMenu.DeletePointRequested -= OnDeletePointRequested;
            _splineContextMenu.InsertPointRequested -= OnInsertPointRequested;
            _splineContextMenu.SmoothPointRequested -= OnSmoothPointRequested;
            _splineContextMenu.SharpenPointRequested -= OnSharpenPointRequested;
        }

        _splineContextMenu = menu;

        if (_splineContextMenu != null)
        {
            _splineContextMenu.HideMenu();
            _splineContextMenu.DeletePointRequested += OnDeletePointRequested;
            _splineContextMenu.InsertPointRequested += OnInsertPointRequested;
            _splineContextMenu.SmoothPointRequested += OnSmoothPointRequested;
            _splineContextMenu.SharpenPointRequested += OnSharpenPointRequested;
        }
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

    private RuntimeSplineHandles? GetSplineHandles()
    {
        if (_splineHandles != null && IsInstanceValid(_splineHandles))
        {
            return _splineHandles;
        }

        if (!SplineHandlesPath.IsEmpty)
        {
            _splineHandles = GetNodeOrNull<RuntimeSplineHandles>(SplineHandlesPath);
        }

        return _splineHandles;
    }

    private void UpdateSelectedPoint(int index)
    {
        _selectedPointIndex = index;
        GetSplineHandles()?.SetSelectedPoint(index);
    }

    private bool CameraConsumingMultiTouch()
    {
        var camera = GetRuntimeCamera();
        return camera != null && camera.IsMultiTouchGestureActive;
    }

    private void ShowSplineContextMenu(int pointIndex)
    {
        var menu = _splineContextMenu;
        var camera = GetRuntimeCamera();
        var curve = GetTrackGenerator()?.Curve;
        if (menu == null || camera == null || curve == null || pointIndex < 0 || pointIndex >= curve.GetPointCount())
        {
            return;
        }

        var worldPosition = curve.GetPointPosition(pointIndex);
        var screenPosition = camera.UnprojectPosition(worldPosition);
        menu.ShowAt(screenPosition, pointIndex);
    }

    private void HideSplineContextMenu()
    {
        _splineContextMenu?.HideMenu();
    }

    private void OnDeletePointRequested(int pointIndex)
    {
        HideSplineContextMenu();
        var curve = GetTrackGenerator()?.Curve;
        if (curve == null || pointIndex < 0 || pointIndex >= curve.GetPointCount())
        {
            return;
        }

        if (_selectedPointIndex == pointIndex)
        {
            UpdateSelectedPoint(-1);
        }

        var position = curve.GetPointPosition(pointIndex);
        var inVec = curve.GetPointIn(pointIndex);
        var outVec = curve.GetPointOut(pointIndex);
        var command = new DeletePointCommand(curve, pointIndex, position, inVec, outVec);

        if (_undoRedo == null)
        {
            command.Do();
            return;
        }

        _undoRedo.CreateAction("Delete Spline Point");
        _undoRedo.AddDoMethod(new Callable(command, nameof(DeletePointCommand.Do)));
        _undoRedo.AddUndoMethod(new Callable(command, nameof(DeletePointCommand.Undo)));
        _undoRedo.AddDoReference(command);
        _undoRedo.AddUndoReference(command);
        _undoRedo.CommitAction();
    }

    private void OnInsertPointRequested(int pointIndex)
    {
        HideSplineContextMenu();
        var curve = GetTrackGenerator()?.Curve;
        if (curve == null)
        {
            return;
        }

        var count = curve.GetPointCount();
        if (count < 2 || pointIndex < 0 || pointIndex >= count)
        {
            return;
        }

        Vector3 neighbor;
        int insertIndex;
        var basePosition = curve.GetPointPosition(pointIndex);
        if (pointIndex < count - 1)
        {
            neighbor = curve.GetPointPosition(pointIndex + 1);
            insertIndex = pointIndex + 1;
        }
        else
        {
            neighbor = curve.GetPointPosition(pointIndex - 1);
            insertIndex = pointIndex;
        }

        var newPosition = (basePosition + neighbor) * 0.5f;
        var command = new InsertPointCommand(curve, insertIndex, newPosition);

        if (_undoRedo == null)
        {
            command.Do();
            return;
        }

        _undoRedo.CreateAction("Insert Spline Point");
        _undoRedo.AddDoMethod(new Callable(command, nameof(InsertPointCommand.Do)));
        _undoRedo.AddUndoMethod(new Callable(command, nameof(InsertPointCommand.Undo)));
        _undoRedo.AddDoReference(command);
        _undoRedo.AddUndoReference(command);
        _undoRedo.CommitAction();
    }

    private void OnSmoothPointRequested(int pointIndex)
    {
        HideSplineContextMenu();
        var curve = GetTrackGenerator()?.Curve;
        if (curve == null || pointIndex < 0 || pointIndex >= curve.GetPointCount())
        {
            return;
        }

        var currentPos = curve.GetPointPosition(pointIndex);
        var pointCount = curve.GetPointCount();
        var computedIn = new Vector3(-1f, 0f, 0f);
        var computedOut = new Vector3(1f, 0f, 0f);
        if (pointIndex > 0)
        {
            var prevPos = curve.GetPointPosition(pointIndex - 1);
            computedIn = (prevPos - currentPos) * 0.25f;
        }

        if (pointIndex < pointCount - 1)
        {
            var nextPos = curve.GetPointPosition(pointIndex + 1);
            computedOut = (nextPos - currentPos) * 0.25f;
        }

        var prevIn = curve.GetPointIn(pointIndex);
        var prevOut = curve.GetPointOut(pointIndex);

        if (_undoRedo == null)
        {
            curve.SetPointIn(pointIndex, computedIn);
            curve.SetPointOut(pointIndex, computedOut);
            return;
        }

        var curveRef = curve;
        var inValue = computedIn;
        var outValue = computedOut;
        _undoRedo.CreateAction("Smooth Tangents");
        _undoRedo.AddDoMethod(Callable.From(() => curveRef.SetPointIn(pointIndex, inValue)));
        _undoRedo.AddDoMethod(Callable.From(() => curveRef.SetPointOut(pointIndex, outValue)));
        _undoRedo.AddUndoMethod(Callable.From(() => curveRef.SetPointIn(pointIndex, prevIn)));
        _undoRedo.AddUndoMethod(Callable.From(() => curveRef.SetPointOut(pointIndex, prevOut)));
        _undoRedo.CommitAction();
    }

    private void OnSharpenPointRequested(int pointIndex)
    {
        HideSplineContextMenu();
        var curve = GetTrackGenerator()?.Curve;
        if (curve == null || pointIndex < 0 || pointIndex >= curve.GetPointCount())
        {
            return;
        }

        var prevIn = curve.GetPointIn(pointIndex);
        var prevOut = curve.GetPointOut(pointIndex);

        if (_undoRedo == null)
        {
            curve.SetPointIn(pointIndex, Vector3.Zero);
            curve.SetPointOut(pointIndex, Vector3.Zero);
            return;
        }

        var curveRef = curve;
        _undoRedo.CreateAction("Sharpen Tangents");
        _undoRedo.AddDoMethod(Callable.From(() => curveRef.SetPointIn(pointIndex, Vector3.Zero)));
        _undoRedo.AddDoMethod(Callable.From(() => curveRef.SetPointOut(pointIndex, Vector3.Zero)));
        _undoRedo.AddUndoMethod(Callable.From(() => curveRef.SetPointIn(pointIndex, prevIn)));
        _undoRedo.AddUndoMethod(Callable.From(() => curveRef.SetPointOut(pointIndex, prevOut)));
        _undoRedo.CommitAction();
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

        if (!SplineHandlesPath.IsEmpty)
        {
            _splineHandles = GetNodeOrNull<RuntimeSplineHandles>(SplineHandlesPath);
        }

        UpdateSelectedPoint(_selectedPointIndex);
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
            HideSplineContextMenu();
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
            HideSplineContextMenu();
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
            HideSplineContextMenu();
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

        if (!isDrag)
        {
            HideSplineContextMenu();
            if (_currentState == SandboxState.Select)
            {
                UpdateSelectedPoint(-1);
            }
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

        var spaceState = world.DirectSpaceState;
        var origin = camera.ProjectRayOrigin(screenPosition);
        var direction = camera.ProjectRayNormal(screenPosition);
        var query = PhysicsRayQueryParameters3D.Create(origin, origin + direction * 1000f);
        query.CollideWithAreas = true;
        query.CollideWithBodies = false;
        query.CollisionMask = RuntimeSplineHandles.HANDLE_COLLISION_LAYER;
        var hit = spaceState.IntersectRay(query);
        if (hit.Count == 0)
        {
            return false;
        }

        if (!hit.TryGetValue("collider", out var colliderVariant))
        {
            return false;
        }

        var collider = colliderVariant.AsGodotObject() as Area3D;
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

        string? tangentType = null;
        if (collider.HasMeta("tangent_type"))
        {
            var tangentVariant = collider.GetMeta("tangent_type");
            tangentType = tangentVariant.VariantType switch
            {
                Variant.Type.String => tangentVariant.AsString(),
                Variant.Type.StringName => tangentVariant.AsStringName().ToString(),
                _ => null
            };
        }

        var index = (int)indexVariant;
        if (index < 0 || index >= curve.GetPointCount())
        {
            return false;
        }

        _draggedPointIndex = index;
        UpdateSelectedPoint(index);
        _handleDragMoved = false;
        HideSplineContextMenu();

        if (!string.IsNullOrEmpty(tangentType))
        {
            _draggedHandleType = tangentType == "in" ? DraggedHandleType.TangentIn : DraggedHandleType.TangentOut;
            _tangentStartValue = _draggedHandleType == DraggedHandleType.TangentIn
                ? curve.GetPointIn(index)
                : curve.GetPointOut(index);
            return true;
        }

        _draggedHandleType = DraggedHandleType.Point;
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

        if (_draggedHandleType == DraggedHandleType.TangentIn || _draggedHandleType == DraggedHandleType.TangentOut)
        {
            UpdateTangentHandle(screenPosition);
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
        if (!_handleDragMoved && (_dragStartPosition - position).LengthSquared() > HandleTapThreshold * HandleTapThreshold)
        {
            _handleDragMoved = true;
        }
    }

    private void UpdateTangentHandle(Vector2 screenPosition)
    {
        var camera = GetRuntimeCamera();
        var track = GetTrackGenerator();
        var curve = track?.Curve;
        if (camera == null || curve == null || _draggedPointIndex < 0)
        {
            return;
        }

        var intersection = camera.GetZZeroIntersection(screenPosition);
        if (intersection == null)
        {
            return;
        }

        var mainPoint = curve.GetPointPosition(_draggedPointIndex);
        var relative = intersection.Value - mainPoint;
        if (_draggedHandleType == DraggedHandleType.TangentIn)
        {
            curve.SetPointIn(_draggedPointIndex, relative);
        }
        else if (_draggedHandleType == DraggedHandleType.TangentOut)
        {
            curve.SetPointOut(_draggedPointIndex, relative);
        }

        if (!_handleDragMoved && (_tangentStartValue - relative).LengthSquared() > HandleTapThreshold * HandleTapThreshold)
        {
            _handleDragMoved = true;
        }
    }

    private void CommitHandleDrag()
    {
        if (_draggedHandleType == DraggedHandleType.TangentIn || _draggedHandleType == DraggedHandleType.TangentOut)
        {
            CommitTangentHandleDrag();
            return;
        }

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
        _dragStartPosition = Vector3.Zero;
        _dragCurrentPosition = Vector3.Zero;
        var wasTap = !_handleDragMoved || (start - end).LengthSquared() <= HandleTapThreshold * HandleTapThreshold;
        _handleDragMoved = false;
        _draggedHandleType = DraggedHandleType.None;

        if (wasTap)
        {
            ShowSplineContextMenu(index);
            return;
        }

        if (_undoRedo == null)
        {
            return;
        }

        var curveRef = curve;
        _undoRedo.CreateAction("Move Track Point");
        var doAction = new CurvePointAction(curveRef, index, end);
        var undoAction = new CurvePointAction(curveRef, index, start);
        _undoRedo.AddDoMethod(new Callable(doAction, nameof(CurvePointAction.Apply)));
        _undoRedo.AddUndoMethod(new Callable(undoAction, nameof(CurvePointAction.Apply)));
        _undoRedo.AddDoReference(doAction);
        _undoRedo.AddUndoReference(undoAction);
        _undoRedo.CommitAction();
    }

    private void CommitTangentHandleDrag()
    {
        var index = _draggedPointIndex;
        var track = GetTrackGenerator();
        var curve = track?.Curve;
        var dragType = _draggedHandleType;
        _draggedPointIndex = -1;
        _draggedHandleType = DraggedHandleType.None;

        if (curve == null || index < 0)
        {
            _handleDragMoved = false;
            _tangentStartValue = Vector3.Zero;
            return;
        }

        var currentValue = dragType == DraggedHandleType.TangentIn
            ? curve.GetPointIn(index)
            : curve.GetPointOut(index);
        var startValue = _tangentStartValue;
        _tangentStartValue = Vector3.Zero;

        var moved = _handleDragMoved && (startValue - currentValue).LengthSquared() > HandleTapThreshold * HandleTapThreshold;
        _handleDragMoved = false;

        if (!moved)
        {
            return;
        }

        if (_undoRedo == null)
        {
            return;
        }

        var isIn = dragType == DraggedHandleType.TangentIn;
        var doAction = new CurveTangentAction(curve, index, isIn, currentValue);
        var undoAction = new CurveTangentAction(curve, index, isIn, startValue);
        _undoRedo.CreateAction(isIn ? "Adjust In Tangent" : "Adjust Out Tangent");
        _undoRedo.AddDoMethod(new Callable(doAction, nameof(CurveTangentAction.Apply)));
        _undoRedo.AddUndoMethod(new Callable(undoAction, nameof(CurveTangentAction.Apply)));
        _undoRedo.AddDoReference(doAction);
        _undoRedo.AddUndoReference(undoAction);
        _undoRedo.CommitAction();
    }

    private Node3D? CreatePropNode(Vector3 planarPosition)
    {
        var track = GetTrackGenerator();
        if (_activePropPrefab != null)
        {
            if (_activePropPrefab.Instantiate() is not Node3D propInstance)
            {
                GD.PushWarning("[RuntimeInputMultiplexer] Active prop prefab must inherit Node3D.");
                return null;
            }

            if (track != null)
            {
                propInstance.Position = ProjectBrushPosition(planarPosition, track);
            }
            else
            {
                propInstance.Position = planarPosition;
            }

            return propInstance;
        }

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

    private sealed partial class CurvePointAction : RefCounted
    {
        private readonly Curve3D _curve;
        private readonly int _index;
        private readonly Vector3 _position;

        public CurvePointAction(Curve3D curve, int index, Vector3 position)
        {
            _curve = curve;
            _index = index;
            _position = position;
        }

        public void Apply()
        {
            if (!GodotObject.IsInstanceValid(_curve))
            {
                return;
            }

            _curve.SetPointPosition(_index, _position);
        }
    }

    private sealed partial class CurveTangentAction : RefCounted
    {
        private readonly Curve3D _curve;
        private readonly int _index;
        private readonly bool _isIn;
        private readonly Vector3 _value;

        public CurveTangentAction(Curve3D curve, int index, bool isIn, Vector3 value)
        {
            _curve = curve;
            _index = index;
            _isIn = isIn;
            _value = value;
        }

        public void Apply()
        {
            if (!GodotObject.IsInstanceValid(_curve))
            {
                return;
            }

            if (_isIn)
            {
                _curve.SetPointIn(_index, _value);
            }
            else
            {
                _curve.SetPointOut(_index, _value);
            }
        }
    }

    private sealed partial class DeletePointCommand : RefCounted
    {
        private readonly Curve3D _curve;
        private readonly int _index;
        private readonly Vector3 _position;
        private readonly Vector3 _in;
        private readonly Vector3 _out;

        public DeletePointCommand(Curve3D curve, int index, Vector3 position, Vector3 inHandle, Vector3 outHandle)
        {
            _curve = curve;
            _index = index;
            _position = position;
            _in = inHandle;
            _out = outHandle;
        }

        public void Do()
        {
            if (!GodotObject.IsInstanceValid(_curve))
            {
                return;
            }

            if (_index >= 0 && _index < _curve.GetPointCount())
            {
                _curve.RemovePoint(_index);
            }
        }

        public void Undo()
        {
            if (!GodotObject.IsInstanceValid(_curve))
            {
                return;
            }

            _curve.AddPoint(_position, _in, _out, _index);
        }
    }

    private sealed partial class InsertPointCommand : RefCounted
    {
        private readonly Curve3D _curve;
        private readonly int _index;
        private readonly Vector3 _position;

        public InsertPointCommand(Curve3D curve, int index, Vector3 position)
        {
            _curve = curve;
            _index = index;
            _position = position;
        }

        public void Do()
        {
            if (!GodotObject.IsInstanceValid(_curve))
            {
                return;
            }

            _curve.AddPoint(_position, Vector3.Zero, Vector3.Zero, _index);
        }

        public void Undo()
        {
            if (!GodotObject.IsInstanceValid(_curve))
            {
                return;
            }

            if (_index >= 0 && _index < _curve.GetPointCount())
            {
                _curve.RemovePoint(_index);
            }
        }
    }
}
