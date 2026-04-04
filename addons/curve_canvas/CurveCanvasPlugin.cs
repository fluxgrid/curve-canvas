#if TOOLS
using Godot;
using IOPath = System.IO.Path;

namespace CurveCanvas.Editor;

[Tool]
public partial class CurveCanvasPlugin : EditorPlugin
{
    private EditorDock? _dock;
    private VBoxContainer? _dockContent;
    private LineEdit? _pathField;
    private Label? _statusLabel;
    private Button? _refreshButton;
    private Button? _browseButton;
    private EditorFileDialog? _fileDialog;
    private CurveCanvasGameManager? _cachedManager;

    public override void _EnterTree()
    {
        BuildDock();
        BuildFileDialog();
        if (_dock != null)
        {
            AddDock(_dock);
        }
        RefreshGameManagerReference();
    }

    public override void _ExitTree()
    {
        if (_dock != null)
        {
            RemoveDock(_dock);
            _dock.QueueFree();
            _dock = null;
        }

        if (_fileDialog != null)
        {
            _fileDialog.QueueFree();
            _fileDialog = null;
        }
    }

    private void BuildDock()
    {
        _dock = new EditorDock
        {
            Name = "CurveCanvasStartupLevelDock",
            DefaultSlot = EditorDock.DockSlot.LeftUl
        };

        _dockContent = new VBoxContainer
        {
            CustomMinimumSize = new Vector2(260, 0),
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill
        };
        _dock.AddChild(_dockContent);

        var title = new Label
        {
            Text = "CurveCanvas Startup Level",
            ThemeTypeVariation = "HeaderSmall"
        };
        _dockContent.AddChild(title);

        _statusLabel = new Label
        {
            Text = "Looking for CurveCanvasGameManager..."
        };
        _dockContent.AddChild(_statusLabel);

        _pathField = new LineEdit
        {
            Editable = false,
            PlaceholderText = "No startup level configured"
        };
        _dockContent.AddChild(_pathField);

        var buttonRow = new HBoxContainer
        {
            Alignment = BoxContainer.AlignmentMode.Center
        };
        _dockContent.AddChild(buttonRow);

        _refreshButton = new Button
        {
            Text = "Refresh",
            FocusMode = Control.FocusModeEnum.None
        };
        _refreshButton.Pressed += RefreshGameManagerReference;
        buttonRow.AddChild(_refreshButton);

        _browseButton = new Button
        {
            Text = "Browse…",
            FocusMode = Control.FocusModeEnum.None
        };
        _browseButton.Pressed += OnBrowsePressed;
        buttonRow.AddChild(_browseButton);
    }

    private void BuildFileDialog()
    {
        _fileDialog = new EditorFileDialog
        {
            Access = EditorFileDialog.AccessEnum.Filesystem,
            FileMode = EditorFileDialog.FileModeEnum.OpenFile
        };
        _fileDialog.AddFilter("*.curvecanvas.json", "CurveCanvas Level");
        _fileDialog.FileSelected += OnFileSelected;
        EditorInterface.Singleton.GetBaseControl().AddChild(_fileDialog);
    }

    private void RefreshGameManagerReference()
    {
        var editedScene = EditorInterface.Singleton.GetEditedSceneRoot();
        _cachedManager = FindGameManager(editedScene);
        UpdateDockUi();
    }

    private void UpdateDockUi()
    {
        var hasManager = _cachedManager != null && IsInstanceValid(_cachedManager);
        if (_browseButton != null)
        {
            _browseButton.Disabled = !hasManager;
        }

        if (!hasManager)
        {
            if (_statusLabel != null)
            {
                _statusLabel.Text = "CurveCanvasGameManager not found in the current scene.";
            }

            if (_pathField != null)
            {
                _pathField.Text = string.Empty;
            }

            return;
        }

        if (_statusLabel != null)
        {
            _statusLabel.Text = "Startup level file:";
        }

        if (_pathField != null)
        {
            _pathField.Text = _cachedManager!.StartupLevelPath;
        }
    }

    private void OnBrowsePressed()
    {
        if (_fileDialog == null)
        {
            return;
        }

        var startupPath = _cachedManager?.StartupLevelPath;
        if (string.IsNullOrWhiteSpace(startupPath))
        {
            startupPath = "user://Levels/default_level.curvecanvas.json";
        }

        var absolutePath = ProjectSettings.GlobalizePath(startupPath);
        var directory = IOPath.GetDirectoryName(absolutePath);
        if (!string.IsNullOrEmpty(directory))
        {
            _fileDialog.CurrentDir = directory;
        }

        var fileName = IOPath.GetFileName(absolutePath);
        if (!string.IsNullOrEmpty(fileName))
        {
            _fileDialog.CurrentFile = fileName;
        }

        _fileDialog.PopupCenteredRatio();
    }

    private void OnFileSelected(string path)
    {
        if (string.IsNullOrEmpty(path))
        {
            return;
        }

        var manager = _cachedManager;
        if (manager == null || !IsInstanceValid(manager))
        {
            GD.PushWarning("[CurveCanvasPlugin] Cannot set startup level; no CurveCanvasGameManager detected.");
            return;
        }

        var undoRedo = GetUndoRedo();
        var previous = manager.StartupLevelPath;
        undoRedo.CreateAction("Set Startup Level Path");
        undoRedo.AddDoProperty(manager, nameof(CurveCanvasGameManager.StartupLevelPath), path);
        undoRedo.AddUndoProperty(manager, nameof(CurveCanvasGameManager.StartupLevelPath), previous);
        undoRedo.CommitAction();
        UpdateDockUi();
    }

    private static CurveCanvasGameManager? FindGameManager(Node? root)
    {
        if (root == null)
        {
            return null;
        }

        if (root is CurveCanvasGameManager manager)
        {
            return manager;
        }

        foreach (Node child in root.GetChildren())
        {
            var match = FindGameManager(child);
            if (match != null)
            {
                return match;
            }
        }

        return null;
    }
}
#endif
