using System.Collections.Generic;
using Godot;

namespace CurveCanvas.Editor;

[Tool]
public partial class CurveCanvasPlugin : EditorPlugin
{
    private const string TriggerPrefabPath = "res://addons/curve_canvas/Prefabs/CameraTrigger.tscn";
    private const float SelectionRadius = 2.0f;
    private TrackMeshGenerator? _currentTrack;
    private int _selectedPointIndex = -1;
    private Vector3 _selectedPointOriginalPosition = Vector3.Zero;
    private bool _isDraggingPoint;
    private bool _pointMovedDuringDrag;

    private bool _propBrushMode;
    private readonly PropBrushTool _propBrushTool = new();
    private HBoxContainer? _toolbar;
    private Button? _exportButton;
    private Button? _importButton;
    private EditorFileDialog? _exportDialog;
    private EditorFileDialog? _importDialog;

    public override void _EnterTree()
    {
        GD.Print("CurveCanvas Initialized");
        CreateToolbar();
        CreateDialogs();
        ConfigureRuntimeMultiplexers();
    }

    public override void _ExitTree()
    {
        _currentTrack = null;
        _selectedPointIndex = -1;
        _propBrushTool.CancelStroke();
        _propBrushMode = false;
        DestroyToolbar();
        DestroyDialogs();
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
        ConfigureRuntimeMultiplexers();
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

    private void ConfigureRuntimeMultiplexers()
    {
        var sceneRoot = EditorInterface.Singleton?.GetEditedSceneRoot();
        if (sceneRoot == null)
        {
            return;
        }

        var tree = sceneRoot.GetTree();
        if (tree == null)
        {
            return;
        }

        var undoRedo = GetUndoRedo();
        var multiplexers = new List<RuntimeInputMultiplexer>();
        CollectMultiplexers(sceneRoot, multiplexers);
        foreach (var multiplexer in multiplexers)
        {
            multiplexer.ConfigureUndoRedo(undoRedo);
        }
    }

    private static void CollectMultiplexers(Node node, List<RuntimeInputMultiplexer> results)
    {
        if (node is RuntimeInputMultiplexer multiplexer)
        {
            results.Add(multiplexer);
        }

        foreach (Node child in node.GetChildren())
        {
            CollectMultiplexers(child, results);
        }
    }

    private void CreateToolbar()
    {
        if (_toolbar != null)
        {
            return;
        }

        _toolbar = new HBoxContainer
        {
            Name = "CurveCanvasToolbar",
            SizeFlagsHorizontal = Control.SizeFlags.ShrinkCenter
        };

        _exportButton = new Button
        {
            Text = "Export Canvas",
            FocusMode = Control.FocusModeEnum.None
        };
        _exportButton.Pressed += OnExportCanvasPressed;
        _toolbar.AddChild(_exportButton);

        _importButton = new Button
        {
            Text = "Import Canvas",
            FocusMode = Control.FocusModeEnum.None
        };
        _importButton.Pressed += OnImportCanvasPressed;
        _toolbar.AddChild(_importButton);

        AddControlToContainer(CustomControlContainer.SpatialEditorMenu, _toolbar);
    }

    private void DestroyToolbar()
    {
        if (_toolbar == null)
        {
            return;
        }

        RemoveControlFromContainer(CustomControlContainer.SpatialEditorMenu, _toolbar);
        if (_exportButton != null)
        {
            _exportButton.Pressed -= OnExportCanvasPressed;
            _exportButton.QueueFree();
        }

        if (_importButton != null)
        {
            _importButton.Pressed -= OnImportCanvasPressed;
            _importButton.QueueFree();
        }

        _toolbar.QueueFree();
        _toolbar = null;
        _exportButton = null;
        _importButton = null;
    }

    private void CreateDialogs()
    {
        var baseControl = EditorInterface.Singleton?.GetBaseControl();
        if (baseControl == null)
        {
            GD.PushError("[CurveCanvasPlugin] Failed to create file dialogs: base control unavailable.");
            return;
        }

        if (_exportDialog == null)
        {
            _exportDialog = new EditorFileDialog
            {
                Name = "CurveCanvasExportDialog",
                FileMode = EditorFileDialog.FileModeEnum.SaveFile,
                Access = FileDialog.AccessEnum.Resources
            };
            _exportDialog.AddFilter("*.curvecanvas.json", "CurveCanvas Level");
            _exportDialog.FileSelected += OnExportFileSelected;
            baseControl.AddChild(_exportDialog);
        }

        if (_importDialog == null)
        {
            _importDialog = new EditorFileDialog
            {
                Name = "CurveCanvasImportDialog",
                FileMode = EditorFileDialog.FileModeEnum.OpenFile,
                Access = FileDialog.AccessEnum.Resources
            };
            _importDialog.AddFilter("*.curvecanvas.json", "CurveCanvas Level");
            _importDialog.FileSelected += OnImportFileSelected;
            baseControl.AddChild(_importDialog);
        }
    }

    private void DestroyDialogs()
    {
        if (_exportDialog != null)
        {
            _exportDialog.FileSelected -= OnExportFileSelected;
            _exportDialog.QueueFree();
            _exportDialog = null;
        }

        if (_importDialog != null)
        {
            _importDialog.FileSelected -= OnImportFileSelected;
            _importDialog.QueueFree();
            _importDialog = null;
        }
    }

    private void OnExportCanvasPressed()
    {
        if (_exportDialog == null)
        {
            GD.PushError("[CurveCanvasPlugin] Export dialog unavailable.");
            return;
        }

        _exportDialog.PopupCenteredRatio();
    }

    private void OnImportCanvasPressed()
    {
        if (_importDialog == null)
        {
            GD.PushError("[CurveCanvasPlugin] Import dialog unavailable.");
            return;
        }

        _importDialog.PopupCenteredRatio();
    }

    private void OnExportFileSelected(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return;
        }

        var sceneRoot = EditorInterface.Singleton?.GetEditedSceneRoot();
        if (sceneRoot == null)
        {
            GD.PushError("[CurveCanvasPlugin] No active scene to export.");
            return;
        }

        CurveCanvasExporter.SaveCanvas(sceneRoot, path);
    }

    private void OnImportFileSelected(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return;
        }

        var sceneRoot = EditorInterface.Singleton?.GetEditedSceneRoot();
        if (sceneRoot == null)
        {
            GD.PushError("[CurveCanvasPlugin] No active scene to import into.");
            return;
        }

        var triggerPrefab = GD.Load<PackedScene>(TriggerPrefabPath);
        if (triggerPrefab == null)
        {
            GD.PushError($"[CurveCanvasPlugin] Failed to load trigger prefab at {TriggerPrefabPath}");
            return;
        }

        CurveCanvasImporter.LoadCanvas(path, sceneRoot, triggerPrefab, GetUndoRedo());
    }
}
