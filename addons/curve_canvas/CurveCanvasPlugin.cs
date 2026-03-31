#if TOOLS
using Godot;

namespace CurveCanvas.Editor;

[Tool]
public partial class CurveCanvasPlugin : EditorPlugin
{
    public override void _EnterTree()
    {
        // Runtime UI now lives in InGameEditorUI; no editor-specific setup required here.
    }

    public override void _ExitTree()
    {
    }
}
#endif
