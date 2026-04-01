using Godot;

namespace CurveCanvas.Editor;

/// <summary>
/// Simple contextual menu for spline point actions.
/// </summary>
public partial class SplineContextMenu : PanelContainer
{
    [Signal]
    public delegate void DeletePointRequestedEventHandler(int pointIndex);

    [Signal]
    public delegate void InsertPointRequestedEventHandler(int pointIndex);

    [Signal]
    public delegate void SmoothPointRequestedEventHandler(int pointIndex);

    [Signal]
    public delegate void SharpenPointRequestedEventHandler(int pointIndex);

    private Button? _deleteButton;
    private Button? _insertButton;
    private Button? _smoothButton;
    private Button? _sharpenButton;
    private int _currentPointIndex = -1;

    public override void _Ready()
    {
        _deleteButton = GetNodeOrNull<Button>("VBoxContainer/DeleteButton");
        _insertButton = GetNodeOrNull<Button>("VBoxContainer/InsertButton");
        _smoothButton = GetNodeOrNull<Button>("VBoxContainer/SmoothButton");
        _sharpenButton = GetNodeOrNull<Button>("VBoxContainer/SharpenButton");
        if (_deleteButton != null)
        {
            _deleteButton.Pressed += OnDeletePressed;
        }

        if (_insertButton != null)
        {
            _insertButton.Pressed += OnInsertPressed;
        }

        if (_smoothButton != null)
        {
            _smoothButton.Pressed += OnSmoothPressed;
        }

        if (_sharpenButton != null)
        {
            _sharpenButton.Pressed += OnSharpenPressed;
        }

        HideMenu();
    }

    public void ShowAt(Vector2 screenPosition, int pointIndex)
    {
        _currentPointIndex = pointIndex;
        Position = screenPosition;
        Show();
    }

    public void HideMenu()
    {
        Hide();
        _currentPointIndex = -1;
    }

    private void OnDeletePressed()
    {
        if (_currentPointIndex < 0)
        {
            return;
        }

        EmitSignal(SignalName.DeletePointRequested, _currentPointIndex);
        HideMenu();
    }

    private void OnInsertPressed()
    {
        if (_currentPointIndex < 0)
        {
            return;
        }

        EmitSignal(SignalName.InsertPointRequested, _currentPointIndex);
        HideMenu();
    }

    private void OnSmoothPressed()
    {
        if (_currentPointIndex < 0)
        {
            return;
        }

        EmitSignal(SignalName.SmoothPointRequested, _currentPointIndex);
        HideMenu();
    }

    private void OnSharpenPressed()
    {
        if (_currentPointIndex < 0)
        {
            return;
        }

        EmitSignal(SignalName.SharpenPointRequested, _currentPointIndex);
        HideMenu();
    }
}
