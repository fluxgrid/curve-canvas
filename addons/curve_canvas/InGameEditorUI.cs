using System.Collections.Generic;
using CurveCanvas.AuthoringCore;
using Godot;

namespace CurveCanvas.Editor;

/// <summary>
/// Runtime-safe UI surface for importing/exporting CurveCanvas payloads and exposing Undo/Redo shortcuts.
/// </summary>
public partial class InGameEditorUI : CanvasLayer
{
    private const string TriggerPrefabPath = "res://addons/curve_canvas/Prefabs/CameraTrigger.tscn";

    public UndoRedo RuntimeUndoRedo { get; } = new();

    private HBoxContainer? _toolbar;
    private Button? _exportButton;
    private Button? _importButton;
    private FileDialog? _exportDialog;
    private FileDialog? _importDialog;
    private LevelMetadataPanel? _metadataPanel;
    private PackedScene? _triggerPrefab;
    private Node? _sceneRoot;

    public override void _Ready()
    {
        _triggerPrefab = GD.Load<PackedScene>(TriggerPrefabPath);
        BuildToolbar();
        CreateDialogs();
        CallDeferred(nameof(InitializeAfterSceneReady));
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
    }

    private void BuildToolbar()
    {
        if (_toolbar != null)
        {
            return;
        }

        _toolbar = new HBoxContainer
        {
            Name = "CurveCanvasToolbar",
            Position = new Vector2(32f, 32f)
        };
        _toolbar.AddThemeConstantOverride("separation", 12);

        _exportButton = new Button
        {
            Text = "Export Canvas",
            FocusMode = Control.FocusModeEnum.None
        };
        _exportButton.Pressed += OnExportButtonPressed;
        _toolbar.AddChild(_exportButton);

        _importButton = new Button
        {
            Text = "Import Canvas",
            FocusMode = Control.FocusModeEnum.None
        };
        _importButton.Pressed += OnImportButtonPressed;
        _toolbar.AddChild(_importButton);

        AddChild(_toolbar);
    }

    private void CreateDialogs()
    {
        if (_exportDialog == null)
        {
            _exportDialog = new FileDialog
            {
                Name = "CurveCanvasExportDialog",
                FileMode = FileDialog.FileModeEnum.SaveFile,
                Access = FileDialog.AccessEnum.Userdata
            };
            _exportDialog.AddFilter("*.curvecanvas.json", "CurveCanvas Level");
            _exportDialog.FileSelected += OnExportFileSelected;
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
            _importDialog.AddFilter("*.curvecanvas.json", "CurveCanvas Level");
            _importDialog.FileSelected += OnImportFileSelected;
            AddChild(_importDialog);
        }
    }

    private void OnExportButtonPressed()
    {
        _exportDialog?.PopupCenteredRatio();
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

        var overrides = BuildMetadataOverrides(_metadataPanel);
        CurveCanvasExporter.SaveCanvas(sceneRoot, path, overrides);
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
        if (root == null)
        {
            return;
        }

        var multiplexers = new List<RuntimeInputMultiplexer>();
        CollectMultiplexers(root, multiplexers);
        foreach (var multiplexer in multiplexers)
        {
            multiplexer.ConfigureUndoRedo(RuntimeUndoRedo);
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
}
