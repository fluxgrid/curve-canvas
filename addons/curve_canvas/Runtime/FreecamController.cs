using Godot;

namespace CurveCanvas.Editor;

public partial class FreecamController : Camera3D
{
    [Export] public float MoveSpeed { get; set; } = 15f;
    [Export] public float BoostMultiplier { get; set; } = 3f;
    [Export] public float ScrollSpeed { get; set; } = 20f;

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
        if (@event is not InputEventMouseButton mouse || !mouse.Pressed)
        {
            return;
        }

        if (mouse.ButtonIndex == MouseButton.WheelUp)
        {
            Translate(new Vector3(0f, 0f, -ScrollSpeed));
            GetViewport()?.SetInputAsHandled();
        }
        else if (mouse.ButtonIndex == MouseButton.WheelDown)
        {
            Translate(new Vector3(0f, 0f, ScrollSpeed));
            GetViewport()?.SetInputAsHandled();
        }
    }
}
