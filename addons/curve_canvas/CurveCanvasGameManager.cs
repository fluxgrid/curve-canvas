using CurveCanvas.AuthoringCore;
using Godot;

namespace CurveCanvas.Editor;

/// <summary>
/// Coordinates play/edit transitions to ensure the sandbox can revert to a pristine edit state after playtesting.
/// </summary>
public partial class CurveCanvasGameManager : Node
{
    private const string TriggerPrefabPath = "res://addons/curve_canvas/Prefabs/CameraTrigger.tscn";

    public enum GameState
    {
        Editing,
        Playing
    }

    [Export]
    public NodePath StateManagerPath { get; set; } = new("StateManager");

    [Export]
    public NodePath SceneRootPath { get; set; } = new("..");

    [Export]
    public NodePath MetadataPanelPath { get; set; } = new("../LevelMetadataPanel");

    [Export]
    public NodePath EditorUiPath { get; set; } = new("../InGameEditorUI");

    [Export]
    public NodePath TrackGeneratorPath { get; set; } = new("../TrackGenerator");

    [Export(PropertyHint.GlobalFile, "*.curvecanvas.json")]
    public string StartupLevelPath { get; set; } = "user://default_level.curvecanvas.json";

    [Export]
    public Godot.Collections.Array<PackedScene> AvailableProps { get; set; } = new();

    private CurveCanvasStateManager? _stateManager;
    private Node? _sceneRoot;
    private LevelMetadataPanel? _metadataPanel;
    private InGameEditorUI? _editorUI;
    private TrackMeshGenerator? _trackGenerator;
    private PackedScene? _triggerPrefab;
    private string _editStateSnapshot = string.Empty;
    private GameState _currentState = GameState.Editing;

    public override void _Ready()
    {
        _stateManager = GetNodeOrNull<CurveCanvasStateManager>(StateManagerPath);
        _sceneRoot = ResolveSceneRoot();
        _metadataPanel = GetNodeOrNull<LevelMetadataPanel>(MetadataPanelPath);
        _editorUI = GetNodeOrNull<InGameEditorUI>(EditorUiPath);
        _trackGenerator = ResolveTrackGenerator();
        _triggerPrefab = ResourceLoader.Load<PackedScene>(TriggerPrefabPath);
        InitializeStartupLevel();
        _editorUI?.PopulateAssetPalette(AvailableProps);

        if (_stateManager != null)
        {
            _stateManager.StateChanged += OnEditorStateChanged;
            var initialState = _stateManager.CurrentState == CurveCanvasStateManager.EditorState.Action
                ? GameState.Playing
                : GameState.Editing;
            SetState(initialState);
        }
    }

    public override void _ExitTree()
    {
        if (_stateManager != null)
        {
            _stateManager.StateChanged -= OnEditorStateChanged;
        }
    }

    private void OnEditorStateChanged(CurveCanvasStateManager.EditorState editorState)
    {
        switch (editorState)
        {
            case CurveCanvasStateManager.EditorState.Architect:
                SetState(GameState.Editing);
                break;
            case CurveCanvasStateManager.EditorState.Action:
                SetState(GameState.Playing);
                break;
        }
    }

    private void SetState(GameState newState)
    {
        if (_currentState == newState)
        {
            return;
        }

        _currentState = newState;
        if (newState == GameState.Playing)
        {
            CaptureEditSnapshot();
        }
        else
        {
            RestoreEditSnapshot();
        }
    }

    private void CaptureEditSnapshot()
    {
        if (!string.IsNullOrEmpty(_editStateSnapshot))
        {
            return;
        }

        var root = ResolveSceneRoot();
        if (root == null)
        {
            GD.PushWarning("[CurveCanvasGameManager] Cannot capture snapshot; scene root not found.");
            return;
        }

        var overrides = BuildMetadataOverrides(_metadataPanel);
        var json = CurveCanvasExporter.SerializeCanvasToString(root, overrides);
        if (string.IsNullOrEmpty(json))
        {
            GD.PushWarning("[CurveCanvasGameManager] Snapshot serialization failed.");
            return;
        }

        _editStateSnapshot = json;
    }

    private void RestoreEditSnapshot()
    {
        if (string.IsNullOrEmpty(_editStateSnapshot))
        {
            return;
        }

        var root = ResolveSceneRoot();
        if (root == null)
        {
            GD.PushWarning("[CurveCanvasGameManager] Cannot restore snapshot; scene root not found.");
            _editStateSnapshot = string.Empty;
            return;
        }

        _triggerPrefab ??= ResourceLoader.Load<PackedScene>(TriggerPrefabPath);
        if (_triggerPrefab == null)
        {
            GD.PushError("[CurveCanvasGameManager] Trigger prefab missing; cannot restore canvas snapshot.");
            _editStateSnapshot = string.Empty;
            return;
        }

        CurveCanvasImporter.LoadCanvasFromString(_editStateSnapshot, root, _triggerPrefab, null, _metadataPanel);
        _editStateSnapshot = string.Empty;
    }

    private Node? ResolveSceneRoot()
    {
        if (_sceneRoot != null && IsInstanceValid(_sceneRoot))
        {
            return _sceneRoot;
        }

        if (SceneRootPath.IsEmpty)
        {
            _sceneRoot = GetTree()?.CurrentScene;
        }
        else
        {
            _sceneRoot = GetNodeOrNull(SceneRootPath);
        }

        return _sceneRoot;
    }

    private TrackMeshGenerator? ResolveTrackGenerator()
    {
        if (_trackGenerator != null && IsInstanceValid(_trackGenerator))
        {
            return _trackGenerator;
        }

        if (TrackGeneratorPath.IsEmpty)
        {
            return null;
        }

        _trackGenerator = GetNodeOrNull<TrackMeshGenerator>(TrackGeneratorPath);
        return _trackGenerator;
    }

    private void InitializeStartupLevel()
    {
        var root = ResolveSceneRoot();
        if (root == null)
        {
            GD.PushWarning("[CurveCanvasGameManager] Cannot initialize startup level; scene root missing.");
            return;
        }

        if (string.IsNullOrWhiteSpace(StartupLevelPath))
        {
            EnsureDefaultCurve();
            return;
        }

        _triggerPrefab ??= ResourceLoader.Load<PackedScene>(TriggerPrefabPath);
        if (FileAccess.FileExists(StartupLevelPath))
        {
            if (_triggerPrefab == null)
            {
                GD.PushError("[CurveCanvasGameManager] Trigger prefab missing; cannot load startup level.");
                return;
            }

            CurveCanvasImporter.LoadCanvas(StartupLevelPath, root, _triggerPrefab, null, _metadataPanel);
            _editorUI?.SetActiveFilePath(StartupLevelPath);
            return;
        }

        EnsureDefaultCurve();
        _editorUI?.SetActiveFilePath(StartupLevelPath);
    }

    private void EnsureDefaultCurve()
    {
        var trackGenerator = ResolveTrackGenerator();
        if (trackGenerator?.Curve == null)
        {
            GD.PushWarning("[CurveCanvasGameManager] No TrackMeshGenerator with a valid Curve3D was found.");
            return;
        }

        var curve = trackGenerator.Curve;
        curve.ClearPoints();
        curve.AddPoint(new Vector3(-10f, 0f, 0f));
        curve.AddPoint(new Vector3(10f, 0f, 0f));
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
}
