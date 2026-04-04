using Godot;
using Godot.Collections;
using System;
using System.IO;

namespace CurveCanvas.Editor;

public partial class LevelSelectMenu : Control
{
    [Export(PropertyHint.File, "*.tscn")]
    public string DemoScenePath { get; set; } = "res://scenes/CurveCanvasDemo.tscn";

    [Export]
    public Array<string> SearchDirectories { get; set; } = new() { "user://Levels/" };

    private VBoxContainer? _listContainer;

    public override void _Ready()
    {
        EnsureBaseDirectories();
        _listContainer = GetNodeOrNull<VBoxContainer>("MarginContainer/Panel/VBox/ScrollContainer/LevelList");
        PopulateButtons();
    }

    private void PopulateButtons()
    {
        EnsureListContainer();
        foreach (Node child in _listContainer!.GetChildren())
        {
            child.QueueFree();
        }

        foreach (var rawDir in SearchDirectories)
        {
            var directory = rawDir?.Trim();
            if (string.IsNullOrWhiteSpace(directory))
            {
                continue;
            }

            EnumerateDirectory(directory);
        }

        if (_listContainer!.GetChildCount() == 0)
        {
            var label = new Label
            {
                Text = "No saved levels found.",
                HorizontalAlignment = HorizontalAlignment.Center
            };
            _listContainer.AddChild(label);
        }
    }

    private void AddLevelButton(string path)
    {
        var fileName = System.IO.Path.GetFileName(path);
        var button = new Button
        {
            Text = fileName,
            FocusMode = FocusModeEnum.None
        };
        button.Pressed += () => OnLevelSelected(path);
        _listContainer?.AddChild(button);
    }

    private void OnLevelSelected(string path)
    {
        RuntimeLevelSession.SetPendingLevel(path);
        var tree = GetTree();
        if (tree == null)
        {
            return;
        }

        var error = tree.ChangeSceneToFile(DemoScenePath);
        if (error != Error.Ok)
        {
            GD.PushError($"[LevelSelectMenu] Failed to load demo scene '{DemoScenePath}': {error}");
        }
    }

    private void EnumerateDirectory(string virtualPath)
    {
        var normalized = NormalizeVirtualPath(virtualPath);
        var globalPath = ProjectSettings.GlobalizePath(normalized);
        if (!Directory.Exists(globalPath))
        {
            return;
        }

        try
        {
            var files = Directory.GetFiles(globalPath, "*.*", SearchOption.TopDirectoryOnly);
            foreach (var file in files)
            {
                var extension = Path.GetExtension(file);
                if (!extension.Equals(".json", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                if (!file.EndsWith(".curvecanvas.json", StringComparison.OrdinalIgnoreCase) &&
                    !file.EndsWith(".curvesequence.json", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                var fileName = Path.GetFileName(file);
                var virtualFilePath = $"{normalized}{fileName}";
                AddLevelButton(virtualFilePath);
            }
        }
        catch (Exception ex)
        {
            GD.PushWarning($"[LevelSelectMenu] Failed to enumerate '{virtualPath}': {ex.Message}");
        }
    }

    private static string NormalizeVirtualPath(string path)
    {
        var trimmed = path.Trim();
        if (!trimmed.EndsWith("/"))
        {
            trimmed += "/";
        }

        return trimmed;
    }

    private void EnsureBaseDirectories()
    {
        foreach (var rawDir in SearchDirectories)
        {
            var normalized = NormalizeVirtualPath(rawDir ?? string.Empty);
            if (string.IsNullOrWhiteSpace(normalized))
            {
                continue;
            }

            var global = ProjectSettings.GlobalizePath(normalized);
            var error = DirAccess.MakeDirRecursiveAbsolute(global);
            if (error != Error.Ok && error != Error.AlreadyExists)
            {
                GD.PushWarning($"[LevelSelectMenu] Could not create '{normalized}': {error}");
            }
        }
    }

    private void EnsureListContainer()
    {
        if (_listContainer != null)
        {
            return;
        }

        var scroll = GetNodeOrNull<ScrollContainer>("MarginContainer/Panel/VBox/ScrollContainer");
        if (scroll == null)
        {
            scroll = new ScrollContainer { Name = "ScrollContainer" };
            var vbox = GetNodeOrNull<VBoxContainer>("MarginContainer/Panel/VBox");
            vbox ??= new VBoxContainer { Name = "VBox" };
            vbox.AddChild(scroll);
        }

        _listContainer = scroll.GetNodeOrNull<VBoxContainer>("LevelList");
        if (_listContainer == null)
        {
            _listContainer = new VBoxContainer { Name = "LevelList" };
            scroll.AddChild(_listContainer);
        }
    }
}
