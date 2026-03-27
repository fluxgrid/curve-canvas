using Godot;

namespace CurveCanvas.Editor;

/// <summary>
/// Manages Architect/Action state transitions for the CurveCanvas sandbox.
/// </summary>
[Tool]
public partial class CurveCanvasStateManager : Node
{
    public enum EditorState
    {
        Architect,
        Action
    }

    [Export]
    public NodePath ArchitectCameraPath { get; set; } = NodePath.Empty;

    [Export]
    public NodePath ActionCameraPath { get; set; } = NodePath.Empty;

    [Export]
    public NodePath TrackPathNodePath { get; set; } = NodePath.Empty;

    [Export]
    public NodePath HostCharacterPath { get; set; } = NodePath.Empty;

    private Camera3D? _architectCamera;
    private Camera3D? _actionCamera;
    private Path3D? _trackPath;
    private Node3D? _hostCharacter;
    private EditorState _currentState = EditorState.Architect;

    public override void _Ready()
    {
        RefreshNodeReferences();
        ApplyState(force: true);
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

    private void ApplyState(bool force = false)
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
}
