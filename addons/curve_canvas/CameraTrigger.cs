using Godot;

namespace CurveCanvas.Editor;

[Tool]
public partial class CameraTrigger : Area3D
{
    [Export]
    public string TargetCameraPath { get; set; } = string.Empty;
}
