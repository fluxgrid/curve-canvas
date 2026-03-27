using Godot;

namespace CurveCanvas.Editor;

[Tool]
public partial class CurveCanvasPlugin : EditorPlugin
{
    public override void _EnterTree()
    {
        GD.Print("CurveCanvas Initialized");
    }

    public override void _ExitTree()
    {
    }
}
