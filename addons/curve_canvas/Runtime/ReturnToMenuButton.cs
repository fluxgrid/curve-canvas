using Godot;

namespace CurveCanvas.Editor;

public partial class ReturnToMenuButton : Button
{
    [Export(PropertyHint.File, "*.tscn")]
    public string MenuScenePath { get; set; } = "res://scenes/LevelSelectMenu.tscn";

    public override void _Ready()
    {
        FocusMode = FocusModeEnum.None;
        Pressed += OnPressed;
    }

    private void OnPressed()
    {
        RuntimeLevelSession.SetPendingLevel(string.Empty);
        var tree = GetTree();
        if (tree == null)
        {
            return;
        }

        var error = tree.ChangeSceneToFile(MenuScenePath);
        if (error != Error.Ok)
        {
            GD.PushError($"[ReturnToMenuButton] Failed to change scene to '{MenuScenePath}': {error}");
        }
    }
}
