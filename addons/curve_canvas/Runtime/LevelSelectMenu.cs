using System;
using Godot;
using Godot.Collections;

namespace CurveCanvas.Editor;

public partial class LevelSelectMenu : Control
{
    [Export(PropertyHint.File, "*.tscn")]
    public string DemoScenePath { get; set; } = "res://scenes/CurveCanvasDemo.tscn";

    [Export]
    public Array<string> SearchDirectories { get; set; } = new() { "user://", "res://Exports" };

    private VBoxContainer? _listContainer;

    public override void _Ready()
    {
        _listContainer = GetNodeOrNull<VBoxContainer>("MarginContainer/ScrollContainer/LevelList");
        PopulateButtons();
    }

    private void PopulateButtons()
    {
        if (_listContainer == null)
        {
            _listContainer = new VBoxContainer { Name = "LevelList" };
        }
        else
        {
            foreach (Node child in _listContainer.GetChildren())
            {
                child.QueueFree();
            }
        }

        foreach (var rawDir in SearchDirectories)
        {
            var directory = rawDir?.Trim();
            if (string.IsNullOrWhiteSpace(directory))
            {
                continue;
            }

            try
            {
                using var dir = DirAccess.Open(directory);
                if (dir == null)
                {
                    continue;
                }

                dir.ListDirBegin();
                while (true)
                {
                    var file = dir.GetNext();
                    if (string.IsNullOrEmpty(file))
                    {
                        break;
                    }

                    if (file == "." || file == "..")
                    {
                        continue;
                    }

                    if (dir.CurrentIsDir())
                    {
                        continue;
                    }

                    if (!file.EndsWith(".curvecanvas.json", StringComparison.OrdinalIgnoreCase) &&
                        !file.EndsWith(".curvesequence.json", StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    var fullPath = System.IO.Path.Combine(directory, file);
                    AddLevelButton(fullPath);
                }
            }
            catch (Exception ex)
            {
                GD.PushWarning($"[LevelSelectMenu] Failed to enumerate '{directory}': {ex.Message}");
            }
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
}
