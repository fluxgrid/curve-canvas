using Godot;

namespace CurveCanvas.Editor;

public partial class FreecamController : Camera3D
{
    [Export] public float MoveSpeed { get; set; } = 15f;
    [Export] public float BoostMultiplier { get; set; } = 3f;
    [Export] public float ScrollSpeed { get; set; } = 20f;
    [Export] public float MousePanSpeed { get; set; } = 0.002f;

    private bool _isMousePanning;

    public override void _Ready()
    {
        Current = true;
    }

    public override void _Process(double delta)
    {
        var inputVector = Input.GetVector("ui_left", "ui_right", "ui_up", "ui_down");
        var multiplier = Input.IsKeyPressed(Key.Shift) ? BoostMultiplier : 1f;
        var movement = new Vector3(inputVector.X, inputVector.Y, 0f) * MoveSpeed * multiplier * (float)delta;
        Translate(movement);
    }

    public override void _UnhandledInput(InputEvent @event)
    {
        if (@event is InputEventMouseButton mouse)
        {
            if (mouse.ButtonIndex == MouseButton.WheelUp && mouse.Pressed)
            {
                Translate(new Vector3(0f, 0f, -ScrollSpeed));
                GetViewport()?.SetInputAsHandled();
                return;
            }

            if (mouse.ButtonIndex == MouseButton.WheelDown && mouse.Pressed)
            {
                Translate(new Vector3(0f, 0f, ScrollSpeed));
                GetViewport()?.SetInputAsHandled();
                return;
            }

            if (mouse.ButtonIndex == MouseButton.Middle)
            {
                _isMousePanning = mouse.Pressed;
                GetViewport()?.SetInputAsHandled();
                return;
            }
        }

        if (@event is InputEventMouseMotion motion && _isMousePanning)
        {
            var depthScale = Mathf.Max(1f, Mathf.Abs(Position.Z));
            var delta = motion.Relative;
            var movement = new Vector3(-delta.X, delta.Y, 0f) * MousePanSpeed * depthScale;
            Translate(movement);
            GetViewport()?.SetInputAsHandled();
        }
    }
}
