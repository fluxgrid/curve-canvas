using System;
using System.Collections.Generic;
using Godot;

namespace CurveCanvas.Editor;

/// <summary>
/// Manages Architect/Action state transitions for the CurveCanvas sandbox.
/// </summary>
[Tool]
public partial class CurveCanvasStateManager : Node
{
    public event Action<EditorState>? StateChanged;

    public enum EditorState
    {
        Architect,
        Action
    }

    public EditorState CurrentState => _currentState;

    [Export]
    public NodePath ArchitectCameraPath { get; set; } = new();

    [Export]
    public NodePath ActionCameraPath { get; set; } = new();

    [Export]
    public NodePath TrackPathNodePath { get; set; } = new();

    [Export]
    public NodePath HostCharacterPath { get; set; } = new();

    [Export(PropertyHint.Range, "-500,500,0.1")]
    public float KillZ { get; set; } = -75f;

    private Camera3D? _architectCamera;
    private Camera3D? _actionCamera;
    private Path3D? _trackPath;
    private Node3D? _hostCharacter;
    private EditorState _currentState = EditorState.Architect;
    private ActionObjectSnapper? _spawnPoint;
    private ActionObjectSnapper? _goalLine;
    private readonly List<CameraTriggerAuthor> _cameraTriggers = new();
    private readonly Dictionary<CameraTriggerAuthor, Area3D.BodyEnteredEventHandler> _triggerEnterHandlers = new();
    private readonly Dictionary<CameraTriggerAuthor, Area3D.BodyExitedEventHandler> _triggerExitHandlers = new();
    private CameraTriggerAuthor? _activeCameraTrigger;
    private Camera3D? _overrideCamera;

    public override void _Ready()
    {
        RefreshNodeReferences();
        ApplyState(force: true, emitSignal: true);
        SetPhysicsProcess(true);
    }

    public override void _UnhandledInput(InputEvent @event)
    {
        if (@event is not InputEventKey keyEvent || !keyEvent.Pressed || keyEvent.Echo)
        {
            return;
        }

        if (keyEvent.Keycode == Key.Tab)
        {
            var nextState = _currentState == EditorState.Architect
                ? EditorState.Action
                : EditorState.Architect;
            SetState(nextState);
            GetViewport()?.SetInputAsHandled();
        }
    }

    /// <summary>
    /// Switches between Architect and Action sandbox modes.
    /// </summary>
    public void SetState(EditorState newState)
    {
        if (_currentState == newState)
        {
            return;
        }

        _currentState = newState;
        ApplyState();
    }

    private void ApplyState(bool force = false, bool emitSignal = true)
    {
        RefreshNodeReferences();

        switch (_currentState)
        {
            case EditorState.Architect:
                EnterArchitectState(force);
                break;
            case EditorState.Action:
                EnterActionState(force);
                break;
        }

        if (emitSignal)
        {
            StateChanged?.Invoke(_currentState);
        }
    }

    private void EnterArchitectState(bool force)
    {
        if (_architectCamera != null)
        {
            _architectCamera.MakeCurrent();
        }

        if (_hostCharacter != null)
        {
            _hostCharacter.ProcessMode = Node.ProcessModeEnum.Disabled;
            _hostCharacter.Visible = false;
        }
    }

    private void EnterActionState(bool force)
    {
        if (_actionCamera != null)
        {
            _actionCamera.MakeCurrent();
        }

        if (_hostCharacter == null || _trackPath?.Curve == null || _architectCamera == null)
        {
            return;
        }

        if (TryApplySpawnPoint())
        {
            _hostCharacter.Visible = true;
            _hostCharacter.ProcessMode = Node.ProcessModeEnum.Inherit;
            return;
        }

        var viewport = GetViewport();
        if (viewport == null)
        {
            return;
        }

        var mousePosition = viewport.GetMousePosition();
        var rayOrigin = _architectCamera.ProjectRayOrigin(mousePosition);
        var rayDirection = _architectCamera.ProjectRayNormal(mousePosition);
        var targetPoint = ProjectRayToZPlane(rayOrigin, rayDirection);

        var closestPoint = _trackPath.Curve.GetClosestPoint(targetPoint);
        _hostCharacter.GlobalPosition = closestPoint;
        _hostCharacter.Visible = true;
        _hostCharacter.ProcessMode = Node.ProcessModeEnum.Inherit;
    }

    public override void _PhysicsProcess(double delta)
    {
        base._PhysicsProcess(delta);

        if (_currentState != EditorState.Action || _hostCharacter == null)
        {
            return;
        }

        if (_hostCharacter.GlobalPosition.Y <= KillZ)
        {
            ResetHostCharacterToSpawn();
        }
    }

    private static Vector3 ProjectRayToZPlane(Vector3 origin, Vector3 direction)
    {
        const float planeZ = 0f;
        if (Mathf.IsZeroApprox(direction.Z))
        {
            return new Vector3(origin.X, origin.Y, planeZ);
        }

        var t = (planeZ - origin.Z) / direction.Z;
        return origin + direction * t;
    }

    private void RefreshNodeReferences()
    {
        _architectCamera = ResolveNode(ArchitectCameraPath, _architectCamera);
        _actionCamera = ResolveNode(ActionCameraPath, _actionCamera);
        _trackPath = ResolveNode(TrackPathNodePath, _trackPath);
        _hostCharacter = ResolveNode(HostCharacterPath, _hostCharacter);
        RefreshSpecialActionObjects();
        RefreshCameraTriggers();
    }

    private T? ResolveNode<T>(NodePath path, T? current) where T : Node
    {
        if (current != null && IsInstanceValid(current))
        {
            return current;
        }

        if (path.IsEmpty)
        {
            return null;
        }

        return GetNodeOrNull<T>(path);
    }

    private void RefreshSpecialActionObjects()
    {
        _spawnPoint = null;
        _goalLine = null;

        var tree = GetTree();
        if (tree == null)
        {
            return;
        }

        var groupMembers = tree.GetNodesInGroup(ActionObjectSnapper.ActionObjectGroup);
        foreach (var member in groupMembers)
        {
            if (member is not ActionObjectSnapper snapper)
            {
                continue;
            }

            switch (snapper.SpecialRole)
            {
                case ActionObjectSnapper.SpecialObjectRole.SpawnPoint when _spawnPoint == null:
                    _spawnPoint = snapper;
                    break;
                case ActionObjectSnapper.SpecialObjectRole.GoalLine when _goalLine == null:
                    _goalLine = snapper;
                    break;
            }
        }
    }

    private void RefreshCameraTriggers()
    {
        foreach (var trigger in _cameraTriggers)
        {
            if (_triggerEnterHandlers.TryGetValue(trigger, out var enterHandler))
            {
                trigger.BodyEntered -= enterHandler;
            }

            if (_triggerExitHandlers.TryGetValue(trigger, out var exitHandler))
            {
                trigger.BodyExited -= exitHandler;
            }
        }

        _cameraTriggers.Clear();
        _triggerEnterHandlers.Clear();
        _triggerExitHandlers.Clear();

        var tree = GetTree();
        if (tree == null)
        {
            return;
        }

        var members = tree.GetNodesInGroup(CameraTriggerAuthor.TriggerGroup);
        foreach (var member in members)
        {
            if (member is not CameraTriggerAuthor trigger)
            {
                continue;
            }

            Area3D.BodyEnteredEventHandler enterHandler = body => OnCameraTriggerBodyEntered(trigger, body);
            Area3D.BodyExitedEventHandler exitHandler = body => OnCameraTriggerBodyExited(trigger, body);

            trigger.BodyEntered += enterHandler;
            trigger.BodyExited += exitHandler;

            _cameraTriggers.Add(trigger);
            _triggerEnterHandlers[trigger] = enterHandler;
            _triggerExitHandlers[trigger] = exitHandler;
        }
    }

    private void OnCameraTriggerBodyEntered(CameraTriggerAuthor trigger, Node3D body)
    {
        if (_hostCharacter == null || body != _hostCharacter)
        {
            return;
        }

        var targetCamera = trigger.GetNodeOrNull<Camera3D>(trigger.TargetCameraPath);
        if (targetCamera == null)
        {
            GD.PushWarning($"[CurveCanvasStateManager] Camera trigger '{trigger.Name}' has no valid TargetCameraPath.");
            return;
        }

        _activeCameraTrigger = trigger;
        _overrideCamera = targetCamera;
        targetCamera.MakeCurrent();
    }

    private void OnCameraTriggerBodyExited(CameraTriggerAuthor trigger, Node3D body)
    {
        if (_hostCharacter == null || body != _hostCharacter)
        {
            return;
        }

        if (_activeCameraTrigger != trigger)
        {
            return;
        }

        _activeCameraTrigger = null;
        _overrideCamera = null;
        _actionCamera?.MakeCurrent();
    }

    private bool TryApplySpawnPoint()
    {
        RefreshSpecialActionObjects();
        if (_spawnPoint == null || _hostCharacter == null)
        {
            return false;
        }

        _hostCharacter.GlobalTransform = _spawnPoint.GlobalTransform;
        if (_hostCharacter is CharacterBody3D characterBody)
        {
            characterBody.Velocity = Vector3.Zero;
        }

        return true;
    }

    private void ResetHostCharacterToSpawn()
    {
        if (TryApplySpawnPoint())
        {
            return;
        }

        if (_hostCharacter == null || _trackPath?.Curve == null)
        {
            return;
        }

        var fallback = _trackPath.Curve.GetClosestPoint(_hostCharacter.GlobalPosition);
        _hostCharacter.GlobalPosition = fallback;
        if (_hostCharacter is CharacterBody3D body)
        {
            body.Velocity = Vector3.Zero;
        }
    }
}
