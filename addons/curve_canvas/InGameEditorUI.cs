using System;
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

    [Export]
    public NodePath SequenceEditorPanelPath { get; set; } = new();

    [Export]
    public NodePath SequenceVisualizerPath { get; set; } = new();

    [Export]
    public NodePath PrimaryTrackGeneratorPath { get; set; } = new();

    [Export]
    public NodePath PrimarySplineHandlesPath { get; set; } = new();

    [Export(PropertyHint.File, "*.tscn")]
    public string PlaytestCameraScenePath { get; set; } = "res://scenes/PlaytestCamera.tscn";

    [Export]
    public NodePath FiniteDirectorPath { get; set; } = new("../FiniteLevelDirector");

    [Export]
    public NodePath EndlessDirectorPath { get; set; } = new("../EndlessLevelDirector");

    [Export]
    public NodePath EditorCameraPath { get; set; } = new("../ArchitectCamera");

    [Export(PropertyHint.File, "*.tscn")]
    public string LevelSelectScenePath { get; set; } = "res://scenes/LevelSelectMenu.tscn";

    private Control? _uiRoot;
    private HBoxContainer? _toolbar;
    private HBoxContainer? _toolButtonsContainer;
    private ButtonGroup? _toolButtonGroup;
    private Button? _selectToolButton;
    private Button? _drawToolButton;
    private Button? _freehandToolButton;
    private Button? _propToolButton;
    private Button? _saveButton;
    private Button? _exportButton;
    private Button? _importButton;
    private Button? _attachPreviewButton;
    private Button? _clearPreviewButton;
    private Button? _playtestButton;
    private Button? _stopPlaytestButton;
    private Button? _exitButton;
    private OptionButton? _segmentTypeOption;
    private Label? _activeFileLabel;
    private HBoxContainer? _modeButtonsContainer;
    private ButtonGroup? _modeButtonGroup;
    private Button? _chunkModeButton;
    private Button? _sequenceModeButton;
    private FileDialog? _exportDialog;
    private FileDialog? _importDialog;
    private FileDialog? _attachPreviewDialog;
    private SplineContextMenu? _splineContextMenu;
    private ScrollContainer? _assetPaletteScroll;
    private HBoxContainer? _propPaletteContainer;
    private ButtonGroup? _propPaletteButtonGroup;
    private readonly List<Button> _propPaletteButtons = new();
    private Godot.Collections.Array<PackedScene>? _pendingPaletteProps;
    private LevelMetadataPanel? _metadataPanel;
    private PackedScene? _triggerPrefab;
    private Node? _sceneRoot;
    private readonly List<RuntimeInputMultiplexer> _multiplexers = new();
    private RuntimeInputMultiplexer.SandboxState _activeSandboxState = RuntimeInputMultiplexer.SandboxState.Select;
    private readonly HashSet<Button> _configuredToolButtons = new();
    private PackedScene? _activePropPrefab;
    private readonly List<Node> _previewAttachments = new();
    private string _activeFilePath = string.Empty;
    private SequenceEditorPanel? _sequencePanel;
    private SequenceVisualizer? _sequenceVisualizer;
    private Node3D? _primaryTrackNode;
    private TrackMeshGenerator? _primaryTrackGenerator;
    private Node3D? _primarySplineHandles;
    private bool _sequenceModeActive;
    private bool _freehandModeActive;
    private bool _playtestActive;
    private bool _useEndlessPlaytest;
    private PackedScene? _playtestCameraScene;
    private Camera3D? _playtestCameraInstance;
    private Camera3D? _editorCamera;
    private FiniteLevelDirector? _finiteDirector;
    private EndlessLevelDirector? _endlessDirector;
    private readonly List<string> _playtestTempFiles = new();
    private string _playtestSnapshot = string.Empty;

    public override void _Ready()
    {
        SetProcess(true);
        _triggerPrefab = GD.Load<PackedScene>(TriggerPrefabPath);
        BuildToolbar();
        CreateDialogs();
        InitializeContextMenu();
        InitializeAssetPalette();
        ResolveSequenceComponents();
        ResolvePlaytestDependencies();
        if (_pendingPaletteProps != null)
        {
            var pending = _pendingPaletteProps;
            _pendingPaletteProps = null;
            PopulateAssetPalette(pending);
        }
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

    public override void _Process(double delta)
    {
        base._Process(delta);
        if (!_playtestActive || !_useEndlessPlaytest)
        {
            return;
        }

        var cameraX = _playtestCameraInstance?.GlobalPosition.X ?? 0f;
        _endlessDirector?.ProcessEndlessGeneration(cameraX);
    }

    private void InitializeAfterSceneReady()
    {
        _sceneRoot = GetSceneRoot();
        _metadataPanel = FindMetadataPanel(_sceneRoot);
        ConfigureMultiplexers(_sceneRoot);
        ResolvePrimaryTrackNodes();
        ApplyActiveSandboxState();
        ApplyEditorMode();
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
        EnsureModeButtons();
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

        _attachPreviewDialog ??= _uiRoot?.GetNodeOrNull<FileDialog>("AttachPreviewDialog");
        if (_attachPreviewDialog == null)
        {
            _attachPreviewDialog = new FileDialog
            {
                Name = "CurveCanvasAttachPreviewDialog",
                FileMode = FileDialog.FileModeEnum.OpenFile,
                Access = FileDialog.AccessEnum.Userdata
            };
            AddChild(_attachPreviewDialog);
        }

        ConfigureDialog(_attachPreviewDialog, OnAttachPreviewFileSelected);
    }

    private void InitializeContextMenu()
    {
        _splineContextMenu ??= _uiRoot?.GetNodeOrNull<SplineContextMenu>("SplineContextMenu");
        _splineContextMenu?.HideMenu();
    }

    private void InitializeAssetPalette()
    {
        _uiRoot ??= GetNodeOrNull<Control>("UiRoot");
        _assetPaletteScroll ??= _uiRoot?.GetNodeOrNull<ScrollContainer>("AssetPalette");
        _propPaletteContainer ??= _assetPaletteScroll?.GetNodeOrNull<HBoxContainer>("PropPaletteContainer");
    }

    public void PopulateAssetPalette(Godot.Collections.Array<PackedScene> props)
    {
        InitializeAssetPalette();
        if (_propPaletteContainer == null)
        {
            _pendingPaletteProps = props;
            return;
        }

        foreach (var existing in _propPaletteButtons)
        {
            existing.QueueFree();
        }

        _propPaletteButtons.Clear();
        _propPaletteButtonGroup = new ButtonGroup();
        _activePropPrefab = null;

        if (props == null || props.Count == 0)
        {
            SetActivePropBrush(null);
            return;
        }

        var defaultAssigned = false;
        foreach (var prop in props)
        {
            if (prop == null)
            {
                continue;
            }

            var button = new Button
            {
                Text = GetPropDisplayName(prop),
                ToggleMode = true,
                ButtonGroup = _propPaletteButtonGroup,
                FocusMode = Control.FocusModeEnum.None
            };

            var capturedProp = prop;
            button.Toggled += pressed =>
            {
                if (pressed)
                {
                    OnPropPaletteButtonSelected(button, capturedProp);
                }
            };

            _propPaletteContainer.AddChild(button);
            _propPaletteButtons.Add(button);

            if (!defaultAssigned)
            {
                button.SetPressedNoSignal(true);
                OnPropPaletteButtonSelected(button, capturedProp);
                defaultAssigned = true;
            }
        }

        if (!defaultAssigned)
        {
            SetActivePropBrush(null);
        }
    }

    private void OnPropPaletteButtonSelected(Button button, PackedScene prop)
    {
        _ = button;
        SetActivePropBrush(prop);
        SetActiveSandboxState(RuntimeInputMultiplexer.SandboxState.PropBrush);
    }

    private static string GetPropDisplayName(PackedScene prop)
    {
        var resourcePath = prop.ResourcePath;
        if (string.IsNullOrEmpty(resourcePath))
        {
            return string.IsNullOrEmpty(prop.ResourceName) ? "Prop" : prop.ResourceName;
        }

        return IOPath.GetFileNameWithoutExtension(resourcePath);
    }

    private void OnExportButtonPressed()
    {
        _exportDialog?.PopupCenteredRatio();
    }

    private void OnAttachPreviewButtonPressed()
    {
        _attachPreviewDialog?.PopupCenteredRatio();
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

    private void OnClearPreviewButtonPressed()
    {
        if (_previewAttachments.Count == 0)
        {
            return;
        }

        foreach (var preview in _previewAttachments)
        {
            if (preview != null && preview.IsInsideTree())
            {
                preview.QueueFree();
            }
        }

        _previewAttachments.Clear();
        UpdatePreviewButtonsState();
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

    private void OnAttachPreviewFileSelected(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return;
        }

        var sceneRoot = GetSceneRoot();
        if (sceneRoot == null)
        {
            GD.PushError("[InGameEditorUI] Cannot attach preview; scene root not found.");
            return;
        }

        _triggerPrefab ??= GD.Load<PackedScene>(TriggerPrefabPath);
        if (_triggerPrefab == null)
        {
            GD.PushError("[InGameEditorUI] Trigger prefab missing; cannot attach preview.");
            return;
        }

        string json;
        try
        {
            json = FileAccess.GetFileAsString(path);
        }
        catch (Exception ex)
        {
            GD.PushError($"[InGameEditorUI] Failed to read preview file: {ex.Message}");
            return;
        }

        var previewRoot = CreatePreviewCanvas(sceneRoot, out var previewTrack);
        if (previewRoot == null || previewTrack == null)
        {
            GD.PushError("[InGameEditorUI] Failed to create preview canvas.");
            return;
        }

        CurveCanvasImporter.LoadCanvasFromString(json, previewRoot, _triggerPrefab, null, null, path);
        AlignPreviewToMainTrack(previewRoot, previewTrack);
        _previewAttachments.Add(previewRoot);
        UpdatePreviewButtonsState();
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
            multiplexer.ConfigureSplineContextMenu(_splineContextMenu);
            multiplexer.ActivePropPrefab = _activePropPrefab;
        }
        ApplyFreehandModeToMultiplexers();
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
            ParTimeSeconds = panel.ParTimeSeconds,
            LevelMode = panel.LevelMode
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

    private void SetActivePropBrush(PackedScene? prefab)
    {
        _activePropPrefab = prefab;
        foreach (var multiplexer in _multiplexers)
        {
            multiplexer.ActivePropPrefab = prefab;
        }
    }

    private void OnFreehandButtonToggled(bool pressed)
    {
        _freehandModeActive = pressed;
        ApplyFreehandModeToMultiplexers();
    }

    private void ApplyFreehandModeToMultiplexers()
    {
        foreach (var multiplexer in _multiplexers)
        {
            multiplexer.IsFreehandModeActive = _freehandModeActive;
        }
    }

    private void OnPlaytestButtonPressed()
    {
        EnterPlaytestMode();
    }

    private void OnStopPlaytestPressed()
    {
        ExitPlaytestMode();
    }

    private void EnsureExitButton()
    {
        if (_exitButton == null)
        {
            _exitButton = new Button
            {
                Name = "ExitToMenuButton",
                Text = "Exit to Menu",
                FocusMode = Control.FocusModeEnum.None,
                CustomMinimumSize = new Vector2(140f, 0f)
            };
            _toolbar?.AddChild(_exitButton);
        }

        // Keep the exit button pinned to the left edge so it never gets clipped
        if (_toolbar != null && _exitButton.GetParent() == _toolbar)
        {
            _toolbar.MoveChild(_exitButton, 0);
        }

        _exitButton.Pressed -= OnExitToMenuPressed;
        _exitButton.Pressed += OnExitToMenuPressed;
    }

    private void OnExitToMenuPressed()
    {
        ExitPlaytestMode();
        RuntimeLevelSession.SetPendingLevel(string.Empty);
        ChangeSceneTo(LevelSelectScenePath);
    }

    private void UpdatePreviewButtonsState()
    {
        if (_clearPreviewButton != null)
        {
            _clearPreviewButton.Disabled = _previewAttachments.Count == 0;
        }
    }

    private void EnterPlaytestMode()
    {
        if (_playtestActive)
        {
            return;
        }

        _sceneRoot ??= GetSceneRoot();
        if (_sceneRoot == null)
        {
            return;
        }

        _useEndlessPlaytest = IsEndlessLevel();

        if (!CapturePlaytestSnapshot())
        {
            GD.PushWarning("[InGameEditorUI] Unable to capture playtest snapshot.");
            return;
        }

        if (_useEndlessPlaytest)
        {
            StartEndlessPlaytest();
        }
        else
        {
            var runtimePath = BuildRuntimeLevelPath();
            if (!string.IsNullOrEmpty(runtimePath))
            {
                _finiteDirector?.LoadLevel(runtimePath);
            }
        }

        ToggleEditorVisibility(false);

        _playtestCameraScene ??= ResourceLoader.Load<PackedScene>(PlaytestCameraScenePath);
        _playtestCameraInstance = _playtestCameraScene?.Instantiate<Camera3D>();
        if (_playtestCameraInstance != null && _sceneRoot != null)
        {
            var spawn = DeterminePlaytestSpawnPosition();
            _playtestCameraInstance.GlobalPosition = spawn + new Vector3(0f, 8f, 20f);
            _sceneRoot.AddChild(_playtestCameraInstance);
            _playtestCameraInstance.Current = true;
        }

        _playtestActive = true;
        UpdatePlaytestButtons();
    }

    private void ExitPlaytestMode()
    {
        if (!_playtestActive)
        {
            return;
        }

        if (_playtestCameraInstance != null)
        {
            _playtestCameraInstance.QueueFree();
            _playtestCameraInstance = null;
        }

        if (_useEndlessPlaytest)
        {
            _endlessDirector?.ResetStream(Vector3.Zero);
            _endlessDirector?.ClearManualChunks();
        }

        RestorePlaytestSnapshot();
        CleanupPlaytestTempFiles();
        ToggleEditorVisibility(true);
        _playtestActive = false;
        UpdatePlaytestButtons();
    }

    private void ToggleEditorVisibility(bool show)
    {
        if (show)
        {
            if (_uiRoot != null)
            {
                _uiRoot.Visible = true;
            }

            if (_editorCamera != null)
            {
                _editorCamera.Visible = true;
                _editorCamera.Current = true;
            }

            ApplyEditorMode();
        }
        else
        {
            if (_uiRoot != null)
            {
                _uiRoot.Visible = false;
            }

            _sequencePanel?.Hide();
            _sequenceVisualizer?.SetVisualizationEnabled(false);
            if (_primarySplineHandles != null)
            {
                _primarySplineHandles.Visible = false;
            }

            foreach (var multiplexer in _multiplexers)
            {
                multiplexer.SetInteractionEnabled(false);
            }

            if (_editorCamera != null)
            {
                _editorCamera.Current = false;
                _editorCamera.Visible = false;
            }

            if (_primaryTrackNode != null)
            {
                _primaryTrackNode.Visible = true;
            }
        }
    }

    private bool CapturePlaytestSnapshot()
    {
        if (_sceneRoot == null)
        {
            return false;
        }

        var overrides = BuildMetadataOverrides(_metadataPanel);
        var json = CurveCanvasExporter.SerializeCanvasToString(_sceneRoot, overrides);
        if (string.IsNullOrEmpty(json))
        {
            return false;
        }

        _playtestSnapshot = json;
        return true;
    }

    private void RestorePlaytestSnapshot()
    {
        if (string.IsNullOrEmpty(_playtestSnapshot) || _sceneRoot == null)
        {
            return;
        }

        _triggerPrefab ??= GD.Load<PackedScene>(TriggerPrefabPath);
        if (_triggerPrefab == null)
        {
            return;
        }

        CurveCanvasImporter.LoadCanvasFromString(_playtestSnapshot, _sceneRoot, _triggerPrefab, null, _metadataPanel);
        _playtestSnapshot = string.Empty;
    }

    private string? BuildRuntimeLevelPath()
    {
        if (_sequenceModeActive && _sequencePanel != null && _sequencePanel.HasChunks &&
            _sequencePanel.TryCreateTemporarySequence(out var sequencePath))
        {
            _playtestTempFiles.Add(sequencePath);
            return sequencePath;
        }

        var chunkPath = CreateTemporaryChunkFromScene();
        if (!string.IsNullOrEmpty(chunkPath))
        {
            _playtestTempFiles.Add(chunkPath);
        }

        return chunkPath;
    }

    private void StartEndlessPlaytest()
    {
        if (_endlessDirector == null)
        {
            GD.PushWarning("[InGameEditorUI] EndlessLevelDirector was not found; cannot start endless playtest.");
            return;
        }

        var spawn = DeterminePlaytestSpawnPosition();
        _endlessDirector.ResetStream(spawn);
        _endlessDirector.ProcessEndlessGeneration(spawn.X);
    }

    private string? CreateTemporaryChunkFromScene()
    {
        if (_sceneRoot == null)
        {
            return null;
        }

        var overrides = BuildMetadataOverrides(_metadataPanel);
        var json = CurveCanvasExporter.SerializeCanvasToString(_sceneRoot, overrides);
        if (string.IsNullOrEmpty(json))
        {
            return null;
        }

        var tempPath = $"user://.playtest_{Guid.NewGuid():N}.curvecanvas.json";
        try
        {
            using var file = FileAccess.Open(tempPath, FileAccess.ModeFlags.Write);
            if (file == null)
            {
                return null;
            }

            file.StoreString(json);
        }
        catch (Exception ex)
        {
            GD.PushError($"[InGameEditorUI] Failed to write playtest chunk: {ex.Message}");
            return null;
        }

        return tempPath;
    }

    private void CleanupPlaytestTempFiles()
    {
        foreach (var temp in _playtestTempFiles)
        {
            if (string.IsNullOrEmpty(temp))
            {
                continue;
            }

            var global = ProjectSettings.GlobalizePath(temp);
            if (System.IO.File.Exists(global))
            {
                try
                {
                    System.IO.File.Delete(global);
                }
                catch
                {
                    // ignored
                }
            }
        }

        _playtestTempFiles.Clear();
    }

    private Vector3 DeterminePlaytestSpawnPosition()
    {
        var curve = _primaryTrackGenerator?.Curve;
        if (curve != null && curve.PointCount > 0)
        {
            return curve.GetPointPosition(0);
        }

        return Vector3.Zero;
    }

    private void UpdatePlaytestButtons()
    {
        if (_playtestButton != null)
        {
            _playtestButton.Disabled = _playtestActive;
        }

        if (_stopPlaytestButton != null)
        {
            _stopPlaytestButton.Visible = _playtestActive;
        }
    }

    private void ChangeSceneTo(string scenePath)
    {
        if (string.IsNullOrWhiteSpace(scenePath))
        {
            return;
        }

        var tree = GetTree();
        if (tree == null)
        {
            return;
        }

        var error = tree.ChangeSceneToFile(scenePath);
        if (error != Error.Ok)
        {
            GD.PushError($"[InGameEditorUI] Failed to change scene to '{scenePath}': {error}");
        }
    }

    private void EnsureToolButtons()
    {
        _toolButtonGroup ??= new ButtonGroup();

        _selectToolButton ??= _toolButtonsContainer?.GetNodeOrNull<Button>("SelectButton");
        _drawToolButton ??= _toolButtonsContainer?.GetNodeOrNull<Button>("DrawButton");
        _freehandToolButton ??= _toolButtonsContainer?.GetNodeOrNull<Button>("FreehandButton");
        _propToolButton ??= _toolButtonsContainer?.GetNodeOrNull<Button>("PropBrushButton");

        ConfigureToolButton(_selectToolButton, RuntimeInputMultiplexer.SandboxState.Select, isDefault: true);
        ConfigureToolButton(_drawToolButton, RuntimeInputMultiplexer.SandboxState.DrawSpline);
        ConfigureToolButton(_propToolButton, RuntimeInputMultiplexer.SandboxState.PropBrush);
        ConfigureFreehandButton();
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

    private void ConfigureFreehandButton()
    {
        if (_freehandToolButton == null)
        {
            return;
        }

        _freehandToolButton.ToggleMode = true;
        _freehandToolButton.Toggled -= OnFreehandButtonToggled;
        _freehandToolButton.Toggled += OnFreehandButtonToggled;
        _freehandToolButton.SetPressedNoSignal(_freehandModeActive);
    }

    private void EnsureActionButtons()
    {
        _saveButton ??= _toolbar?.GetNodeOrNull<Button>("SaveButton");
        _exportButton ??= _toolbar?.GetNodeOrNull<Button>("ExportButton");
        _importButton ??= _toolbar?.GetNodeOrNull<Button>("ImportButton");
        _attachPreviewButton ??= _toolbar?.GetNodeOrNull<Button>("AttachPreviewButton");
        _clearPreviewButton ??= _toolbar?.GetNodeOrNull<Button>("ClearPreviewButton");
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

        if (_attachPreviewButton == null)
        {
            _attachPreviewButton = new Button
            {
                Name = "AttachPreviewButton",
                Text = "Attach Preview",
                FocusMode = Control.FocusModeEnum.None
            };
            _toolbar?.AddChild(_attachPreviewButton);
        }

        if (_clearPreviewButton == null)
        {
            _clearPreviewButton = new Button
            {
                Name = "ClearPreviewButton",
                Text = "Clear Previews",
                FocusMode = Control.FocusModeEnum.None,
                Disabled = true
            };
            _toolbar?.AddChild(_clearPreviewButton);
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

        EnsureSegmentTypeOption();
        EnsurePlaytestButtons();
        EnsureExitButton();

        _saveButton.Pressed -= OnSaveButtonPressed;
        _saveButton.Pressed += OnSaveButtonPressed;
        _exportButton.Pressed -= OnExportButtonPressed;
        _exportButton.Pressed += OnExportButtonPressed;
        _importButton.Pressed -= OnImportButtonPressed;
        _importButton.Pressed += OnImportButtonPressed;
        _attachPreviewButton.Pressed -= OnAttachPreviewButtonPressed;
        _attachPreviewButton.Pressed += OnAttachPreviewButtonPressed;
        _clearPreviewButton.Pressed -= OnClearPreviewButtonPressed;
        _clearPreviewButton.Pressed += OnClearPreviewButtonPressed;
        UpdateActiveFileLabel();
        UpdatePreviewButtonsState();
    }

    private void EnsureSegmentTypeOption()
    {
        if (_segmentTypeOption == null)
        {
            _segmentTypeOption = new OptionButton
            {
                Name = "SegmentTypeDropdown",
                FocusMode = Control.FocusModeEnum.None,
                TooltipText = "Select the current track profile"
            };
            _segmentTypeOption.AddItem("Flow", 0);
            _segmentTypeOption.AddItem("Rail", 1);
            _toolbar?.AddChild(_segmentTypeOption);
        }

        _segmentTypeOption.ItemSelected -= OnSegmentTypeOptionSelected;
        _segmentTypeOption.ItemSelected += OnSegmentTypeOptionSelected;
        UpdateSegmentTypeOption();
    }


    private void UpdateSegmentTypeOption()
    {
        if (_segmentTypeOption == null || _primaryTrackGenerator == null)
        {
            return;
        }

        var current = _primaryTrackGenerator.CurrentSegmentType;
        var index = current.Equals("Rail", StringComparison.OrdinalIgnoreCase) ? 1 : 0;
        _segmentTypeOption.Select(index);
    }

    private void OnSegmentTypeOptionSelected(long index)
    {
        if (_primaryTrackGenerator == null)
        {
            return;
        }

        var nextType = index == 1 ? "Rail" : "Flow";
        _primaryTrackGenerator.SetSegmentType(nextType);
    }

    private bool IsEndlessLevel()
    {
        var mode = _metadataPanel?.LevelMode ?? "Finite";
        return mode.Equals("Endless", StringComparison.OrdinalIgnoreCase);
    }


    private void EnsurePlaytestButtons()
    {
        if (_playtestButton == null)
        {
            _playtestButton = new Button
            {
                Name = "PlaytestButton",
                Text = "Test Level",
                FocusMode = Control.FocusModeEnum.None
            };
            _toolbar?.AddChild(_playtestButton);
        }

        if (_stopPlaytestButton == null)
        {
            _stopPlaytestButton = new Button
            {
                Name = "StopPlaytestButton",
                Text = "Stop Testing",
                FocusMode = Control.FocusModeEnum.None,
                Visible = false
            };
            _toolbar?.AddChild(_stopPlaytestButton);
        }

        _playtestButton!.Pressed -= OnPlaytestButtonPressed;
        _playtestButton.Pressed += OnPlaytestButtonPressed;
        _stopPlaytestButton!.Pressed -= OnStopPlaytestPressed;
        _stopPlaytestButton.Pressed += OnStopPlaytestPressed;
        UpdatePlaytestButtons();
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

    private void EnsureModeButtons()
    {
        _modeButtonsContainer ??= _toolbar?.GetNodeOrNull<HBoxContainer>("ModeButtons");
        if (_modeButtonsContainer == null)
        {
            _modeButtonsContainer = new HBoxContainer
            {
                Name = "ModeButtons"
            };
            _modeButtonsContainer.AddThemeConstantOverride("separation", 6);
            _toolbar?.AddChild(_modeButtonsContainer);
        }

        _modeButtonGroup ??= new ButtonGroup();
        _chunkModeButton ??= _modeButtonsContainer.GetNodeOrNull<Button>("ChunkModeButton") ?? CreateModeButton("ChunkModeButton", "Chunk Mode");
        _sequenceModeButton ??= _modeButtonsContainer.GetNodeOrNull<Button>("SequenceModeButton") ?? CreateModeButton("SequenceModeButton", "Sequence Mode");

        ConfigureModeButton(_chunkModeButton, false);
        ConfigureModeButton(_sequenceModeButton, true);
        _chunkModeButton?.SetPressedNoSignal(!_sequenceModeActive);
        _sequenceModeButton?.SetPressedNoSignal(_sequenceModeActive);
    }

    private Button CreateModeButton(string name, string text)
    {
        var button = new Button
        {
            Name = name,
            Text = text,
            ToggleMode = true,
            FocusMode = Control.FocusModeEnum.None
        };
        _modeButtonsContainer?.AddChild(button);
        return button;
    }

    private void ConfigureModeButton(Button? button, bool activateSequenceMode)
    {
        if (button == null || _modeButtonGroup == null)
        {
            return;
        }

        button.ButtonGroup = _modeButtonGroup;
        button.Toggled += pressed =>
        {
            if (pressed)
            {
                SetEditorMode(activateSequenceMode);
            }
        };
    }

    private void SetEditorMode(bool sequenceMode)
    {
        if (_sequenceModeActive == sequenceMode)
        {
            return;
        }

        _sequenceModeActive = sequenceMode;
        ApplyEditorMode();
    }

    private void ApplyEditorMode()
    {
        if (_sequenceModeActive)
        {
            foreach (var multiplexer in _multiplexers)
            {
                multiplexer.SetInteractionEnabled(false);
            }

            if (_primaryTrackNode != null)
            {
                _primaryTrackNode.Visible = false;
            }

            if (_primarySplineHandles != null)
            {
                _primarySplineHandles.Visible = false;
            }

            _sequencePanel?.SetActive(true);
            _sequencePanel?.RebuildVisualization();
            _sequenceVisualizer?.SetVisualizationEnabled(true);
        }
        else
        {
            foreach (var multiplexer in _multiplexers)
            {
                multiplexer.SetInteractionEnabled(true);
            }

            if (_primaryTrackNode != null)
            {
                _primaryTrackNode.Visible = true;
            }

            if (_primarySplineHandles != null)
            {
                _primarySplineHandles.Visible = true;
            }

            _sequencePanel?.SetActive(false);
            _sequenceVisualizer?.SetVisualizationEnabled(false);
        }

        _chunkModeButton?.SetPressedNoSignal(!_sequenceModeActive);
        _sequenceModeButton?.SetPressedNoSignal(_sequenceModeActive);
    }

    private void ResolveSequenceComponents()
    {
        if (HasNodePath(SequenceEditorPanelPath))
        {
            _sequencePanel = GetNodeOrNull<SequenceEditorPanel>(SequenceEditorPanelPath);
        }

        if (HasNodePath(SequenceVisualizerPath))
        {
            _sequenceVisualizer = GetNodeOrNull<SequenceVisualizer>(SequenceVisualizerPath);
        }

        if (_sequencePanel != null && _sequenceVisualizer != null)
        {
            _sequencePanel.ConfigureVisualizer(_sequenceVisualizer);
            _sequencePanel.SetActive(false);
            _sequencePanel.CloseRequested -= OnSequencePanelCloseRequested;
            _sequencePanel.CloseRequested += OnSequencePanelCloseRequested;
        }
    }

    private void ResolvePlaytestDependencies()
    {
        _playtestCameraScene ??= ResourceLoader.Load<PackedScene>(PlaytestCameraScenePath);
        if (HasNodePath(FiniteDirectorPath))
        {
            _finiteDirector = GetNodeOrNull<FiniteLevelDirector>(FiniteDirectorPath);
        }

        if (HasNodePath(EndlessDirectorPath))
        {
            _endlessDirector = GetNodeOrNull<EndlessLevelDirector>(EndlessDirectorPath);
        }

        if (HasNodePath(EditorCameraPath))
        {
            _editorCamera = GetNodeOrNull<Camera3D>(EditorCameraPath);
        }
    }

    private void ResolvePrimaryTrackNodes()
    {
        if (HasNodePath(PrimaryTrackGeneratorPath))
        {
            _primaryTrackNode = GetNodeOrNull<Node3D>(PrimaryTrackGeneratorPath);
            if (_primaryTrackNode is TrackMeshGenerator generator)
            {
                _primaryTrackGenerator = generator;
            }
            else
            {
                _primaryTrackGenerator = _primaryTrackNode as TrackMeshGenerator;
            }
        }

        if (HasNodePath(PrimarySplineHandlesPath))
        {
            _primarySplineHandles = GetNodeOrNull<Node3D>(PrimarySplineHandlesPath);
        }

        UpdateSegmentTypeOption();
    }

    private static bool HasNodePath(NodePath path)
    {
        return !string.IsNullOrEmpty(path.ToString());
    }

    private void OnSequencePanelCloseRequested()
    {
        SetEditorMode(false);
    }

    private Node3D? CreatePreviewCanvas(Node sceneRoot, out TrackMeshGenerator? previewTrack)
    {
        previewTrack = null;
        var previewRoot = new Node3D
        {
            Name = $"AttachmentPreview_{_previewAttachments.Count:D2}"
        };

        sceneRoot.AddChild(previewRoot);

        var track = new TrackMeshGenerator
        {
            Name = "TrackGeneratorPreview"
        };
        track.Curve ??= new Curve3D();
        previewRoot.AddChild(track);

        var propContainer = new Node3D
        {
            Name = "PropContainer"
        };
        previewRoot.AddChild(propContainer);

        previewTrack = track;
        return previewRoot;
    }

    private void AlignPreviewToMainTrack(Node3D previewRoot, TrackMeshGenerator previewTrack)
    {
        var sceneRoot = GetSceneRoot();
        var mainTrack = sceneRoot != null ? CurveCanvasExportCommon.FindTrackGenerator(sceneRoot) : null;
        if (mainTrack?.Curve == null || previewTrack.Curve == null)
        {
            return;
        }

        var alignment = SegmentStitcher.CalculateSocketAlignment(mainTrack.Curve, previewTrack.Curve);
        var transform = previewRoot.GlobalTransform;
        transform.Origin += alignment.Origin;
        previewRoot.GlobalTransform = transform;
    }
}
