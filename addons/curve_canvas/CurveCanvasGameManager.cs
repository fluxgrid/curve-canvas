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

    private CurveCanvasStateManager? _stateManager;
    private Node? _sceneRoot;
    private LevelMetadataPanel? _metadataPanel;
    private PackedScene? _triggerPrefab;
    private string _editStateSnapshot = string.Empty;
    private GameState _currentState = GameState.Editing;

    public override void _Ready()
    {
        _stateManager = GetNodeOrNull<CurveCanvasStateManager>(StateManagerPath);
        _sceneRoot = ResolveSceneRoot();
        _metadataPanel = GetNodeOrNull<LevelMetadataPanel>(MetadataPanelPath);
        _triggerPrefab = ResourceLoader.Load<PackedScene>(TriggerPrefabPath);

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
