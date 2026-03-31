using System.Collections.Generic;
using CurveCanvas.AuthoringCore;
using Godot;
using IOPath = System.IO.Path;

namespace CurveCanvas.Editor;

/// <summary>
/// Runtime-safe UI surface for importing/exporting CurveCanvas payloads and exposing Undo/Redo shortcuts.
/// </summary>
public partial class InGameEditorUI : CanvasLayer
{
    private const string TriggerPrefabPath = "res://addons/curve_canvas/Prefabs/CameraTrigger.tscn";

    public UndoRedo RuntimeUndoRedo { get; } = new();

    private Control? _uiRoot;
    private HBoxContainer? _toolbar;
    private HBoxContainer? _toolButtonsContainer;
    private ButtonGroup? _toolButtonGroup;
    private Button? _selectToolButton;
    private Button? _drawToolButton;
    private Button? _propToolButton;
    private Button? _saveButton;
    private Button? _exportButton;
    private Button? _importButton;
    private Label? _activeFileLabel;
    private FileDialog? _exportDialog;
    private FileDialog? _importDialog;
    private LevelMetadataPanel? _metadataPanel;
    private PackedScene? _triggerPrefab;
    private Node? _sceneRoot;
    private readonly List<RuntimeInputMultiplexer> _multiplexers = new();
    private RuntimeInputMultiplexer.SandboxState _activeSandboxState = RuntimeInputMultiplexer.SandboxState.Select;
    private readonly HashSet<Button> _configuredToolButtons = new();
    private string _activeFilePath = string.Empty;

    public override void _Ready()
    {
        _triggerPrefab = GD.Load<PackedScene>(TriggerPrefabPath);
        BuildToolbar();
        CreateDialogs();
        CallDeferred(nameof(InitializeAfterSceneReady));
        UpdateActiveFileLabel();
    }

    public override void _UnhandledInput(InputEvent @event)
    {
        if (@event is not InputEventKey key || !key.Pressed || key.Echo)
        {
            return;
        }

        var commandPressed = key.CtrlPressed || key.MetaPressed;
        if (!commandPressed || key.Keycode != Key.Z)
        {
            return;
        }

        if (key.ShiftPressed)
        {
            RuntimeUndoRedo.Redo();
        }
        else
        {
            RuntimeUndoRedo.Undo();
        }

        GetViewport()?.SetInputAsHandled();
    }

    private void InitializeAfterSceneReady()
    {
        _sceneRoot = GetSceneRoot();
        _metadataPanel = FindMetadataPanel(_sceneRoot);
        ConfigureMultiplexers(_sceneRoot);
        ApplyActiveSandboxState();
    }

    private void BuildToolbar()
    {
        _uiRoot ??= GetNodeOrNull<Control>("UiRoot");
        _toolbar = _uiRoot?.GetNodeOrNull<HBoxContainer>("Toolbar") ?? _toolbar;
        if (_toolbar == null)
        {
            _toolbar = new HBoxContainer
            {
                Name = "CurveCanvasToolbar",
                Position = new Vector2(32f, 32f)
            };
            _toolbar.AddThemeConstantOverride("separation", 12);
            AddChild(_toolbar);
        }

        _toolButtonsContainer = _toolbar.GetNodeOrNull<HBoxContainer>("ToolButtons") ?? _toolButtonsContainer;
        if (_toolButtonsContainer == null)
        {
            _toolButtonsContainer = new HBoxContainer
            {
                Name = "ToolButtons"
            };
            _toolButtonsContainer.AddThemeConstantOverride("separation", 8);
            _toolbar.AddChild(_toolButtonsContainer, true);
        }

        EnsureToolButtons();
        EnsureActionButtons();
    }

    private void CreateDialogs()
    {
        _exportDialog ??= _uiRoot?.GetNodeOrNull<FileDialog>("ExportDialog");
        _importDialog ??= _uiRoot?.GetNodeOrNull<FileDialog>("ImportDialog");

        if (_exportDialog == null)
        {
            _exportDialog = new FileDialog
            {
                Name = "CurveCanvasExportDialog",
                FileMode = FileDialog.FileModeEnum.SaveFile,
                Access = FileDialog.AccessEnum.Userdata
            };
            AddChild(_exportDialog);
        }

        if (_importDialog == null)
        {
            _importDialog = new FileDialog
            {
                Name = "CurveCanvasImportDialog",
                FileMode = FileDialog.FileModeEnum.OpenFile,
                Access = FileDialog.AccessEnum.Userdata
            };
            AddChild(_importDialog);
        }

        ConfigureDialog(_exportDialog, OnExportFileSelected);
        ConfigureDialog(_importDialog, OnImportFileSelected);
    }

    private void OnExportButtonPressed()
    {
        _exportDialog?.PopupCenteredRatio();
    }

    private void OnSaveButtonPressed()
    {
        if (string.IsNullOrWhiteSpace(_activeFilePath))
        {
            _exportDialog?.PopupCenteredRatio();
            return;
        }

        TrySaveCanvas(_activeFilePath);
    }

    private void OnImportButtonPressed()
    {
        _importDialog?.PopupCenteredRatio();
    }

    private void OnExportFileSelected(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return;
        }

        var sceneRoot = GetSceneRoot();
        if (sceneRoot == null)
        {
            GD.PushError("[InGameEditorUI] No active scene to export.");
            return;
        }

        TrySaveCanvas(path);
    }

    private void OnImportFileSelected(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return;
        }

        var sceneRoot = GetSceneRoot();
        if (sceneRoot == null)
        {
            GD.PushError("[InGameEditorUI] No active scene to import into.");
            return;
        }

        _triggerPrefab ??= GD.Load<PackedScene>(TriggerPrefabPath);
        if (_triggerPrefab == null)
        {
            GD.PushError($"[InGameEditorUI] Failed to load trigger prefab at {TriggerPrefabPath}");
            return;
        }

        CurveCanvasImporter.LoadCanvas(path, sceneRoot, _triggerPrefab, RuntimeUndoRedo, _metadataPanel);
        SetActiveFilePath(path);
    }

    private Node? GetSceneRoot()
    {
        var tree = GetTree();
        if (tree?.CurrentScene != null)
        {
            return tree.CurrentScene;
        }

        if (tree?.Root.GetChildCount() > 0)
        {
            return tree.Root.GetChild(0);
        }

        return null;
    }

    private void ConfigureMultiplexers(Node? root)
    {
        _multiplexers.Clear();
        if (root == null)
        {
            return;
        }

        CollectMultiplexers(root, _multiplexers);
        foreach (var multiplexer in _multiplexers)
        {
            multiplexer.ConfigureUndoRedo(RuntimeUndoRedo);
            multiplexer.SetSandboxState(_activeSandboxState);
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

    private static LevelMetadataPanel? FindMetadataPanel(Node? root)
    {
        if (root == null)
        {
            return null;
        }

        if (root is LevelMetadataPanel panel)
        {
            return panel;
        }

        foreach (Node child in root.GetChildren())
        {
            var match = FindMetadataPanel(child);
            if (match != null)
            {
                return match;
            }
        }

        return null;
    }

    private static CurveCanvasMetadata? BuildMetadataOverrides(LevelMetadataPanel? panel)
    {
        if (panel == null)
        {
            return null;
        }

        return new CurveCanvasMetadata
        {
            LevelName = panel.LevelName,
            Author = panel.Author,
            ParTimeSeconds = panel.ParTimeSeconds
        };
    }

    private bool TrySaveCanvas(string filePath, bool updateActivePath = true)
    {
        var sceneRoot = GetSceneRoot();
        if (sceneRoot == null)
        {
            GD.PushError("[InGameEditorUI] No active scene to export.");
            return false;
        }

        var overrides = BuildMetadataOverrides(_metadataPanel);
        if (!CurveCanvasExporter.SaveCanvas(sceneRoot, filePath, overrides))
        {
            return false;
        }

        if (updateActivePath)
        {
            SetActiveFilePath(filePath);
        }

        return true;
    }

    public void SetActiveFilePath(string? path)
    {
        _activeFilePath = path ?? string.Empty;
        UpdateActiveFileLabel();
    }

    private void UpdateActiveFileLabel()
    {
        _activeFileLabel ??= _toolbar?.GetNodeOrNull<Label>("ActiveFileLabel");
        if (_activeFileLabel == null)
        {
            return;
        }

        var displayName = string.IsNullOrEmpty(_activeFilePath)
            ? "Unsaved Scene"
            : IOPath.GetFileName(_activeFilePath);
        _activeFileLabel.Text = $"Editing: {displayName}";
    }

    private void EnsureToolButtons()
    {
        _toolButtonGroup ??= new ButtonGroup();

        _selectToolButton ??= _toolButtonsContainer?.GetNodeOrNull<Button>("SelectButton");
        _drawToolButton ??= _toolButtonsContainer?.GetNodeOrNull<Button>("DrawButton");
        _propToolButton ??= _toolButtonsContainer?.GetNodeOrNull<Button>("PropBrushButton");

        ConfigureToolButton(_selectToolButton, RuntimeInputMultiplexer.SandboxState.Select, isDefault: true);
        ConfigureToolButton(_drawToolButton, RuntimeInputMultiplexer.SandboxState.DrawSpline);
        ConfigureToolButton(_propToolButton, RuntimeInputMultiplexer.SandboxState.PropBrush);
        ApplyActiveSandboxState();
    }

    private void ConfigureToolButton(Button? button, RuntimeInputMultiplexer.SandboxState state, bool isDefault = false)
    {
        if (button == null || _toolButtonGroup == null)
        {
            return;
        }

        button.ToggleMode = true;
        button.ButtonGroup = _toolButtonGroup;
        if (isDefault && _activeSandboxState == RuntimeInputMultiplexer.SandboxState.Select)
        {
            button.SetPressedNoSignal(true);
        }

        if (_configuredToolButtons.Add(button))
        {
            button.Toggled += pressed =>
            {
                if (pressed)
                {
                    SetActiveSandboxState(state);
                }
            };
        }
    }

    private void EnsureActionButtons()
    {
        _saveButton ??= _toolbar?.GetNodeOrNull<Button>("SaveButton");
        _exportButton ??= _toolbar?.GetNodeOrNull<Button>("ExportButton");
        _importButton ??= _toolbar?.GetNodeOrNull<Button>("ImportButton");
        _activeFileLabel ??= _toolbar?.GetNodeOrNull<Label>("ActiveFileLabel");

        if (_saveButton == null)
        {
            _saveButton = new Button
            {
                Name = "SaveButton",
                Text = "Save",
                FocusMode = Control.FocusModeEnum.None
            };
            _toolbar?.AddChild(_saveButton);
        }

        if (_exportButton == null)
        {
            _exportButton = new Button
            {
                Name = "ExportButton",
                Text = "Export Canvas",
                FocusMode = Control.FocusModeEnum.None
            };
            _toolbar?.AddChild(_exportButton);
        }

        if (_importButton == null)
        {
            _importButton = new Button
            {
                Name = "ImportButton",
                Text = "Import Canvas",
                FocusMode = Control.FocusModeEnum.None
            };
            _toolbar?.AddChild(_importButton);
        }

        if (_activeFileLabel == null)
        {
            _activeFileLabel = new Label
            {
                Name = "ActiveFileLabel",
                Text = "Editing: Unsaved Scene"
            };
            _toolbar?.AddChild(_activeFileLabel);
        }

        _saveButton.Pressed -= OnSaveButtonPressed;
        _saveButton.Pressed += OnSaveButtonPressed;
        _exportButton.Pressed -= OnExportButtonPressed;
        _exportButton.Pressed += OnExportButtonPressed;
        _importButton.Pressed -= OnImportButtonPressed;
        _importButton.Pressed += OnImportButtonPressed;
        UpdateActiveFileLabel();
    }

    private static void ConfigureDialog(FileDialog? dialog, FileDialog.FileSelectedEventHandler handler)
    {
        if (dialog == null)
        {
            return;
        }

        dialog.FileSelected -= handler;
        dialog.FileSelected += handler;
        dialog.ClearFilters();
        dialog.AddFilter("*.curvecanvas.json", "CurveCanvas Level");
        dialog.Access = FileDialog.AccessEnum.Userdata;
    }

    private void SetActiveSandboxState(RuntimeInputMultiplexer.SandboxState state)
    {
        if (_activeSandboxState == state)
        {
            return;
        }

        _activeSandboxState = state;
        ApplyActiveSandboxState();
    }

    private void ApplyActiveSandboxState()
    {
        foreach (var multiplexer in _multiplexers)
        {
            multiplexer.SetSandboxState(_activeSandboxState);
        }

        _selectToolButton?.SetPressedNoSignal(_activeSandboxState == RuntimeInputMultiplexer.SandboxState.Select);
        _drawToolButton?.SetPressedNoSignal(_activeSandboxState == RuntimeInputMultiplexer.SandboxState.DrawSpline);
        _propToolButton?.SetPressedNoSignal(_activeSandboxState == RuntimeInputMultiplexer.SandboxState.PropBrush);
    }
}
