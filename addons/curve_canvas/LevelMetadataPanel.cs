using System;
using Godot;

namespace CurveCanvas.Editor;

/// <summary>
/// Simple UI surface that lets designers edit level metadata (name, author, par time)
/// alongside the CurveCanvas sandbox.
/// </summary>
[Tool]
public partial class LevelMetadataPanel : CanvasLayer
{
    public const string MetadataPanelGroup = "CurveCanvasLevelMetadata";

    [Export]
    public NodePath LevelNameInputPath { get; set; } = new();

    [Export]
    public NodePath AuthorInputPath { get; set; } = new();

    [Export]
    public NodePath ParTimeInputPath { get; set; } = new();

    [Export]
    public NodePath LevelModeOptionPath { get; set; } = new();

    private LineEdit? _levelNameInput;
    private LineEdit? _authorInput;
    private SpinBox? _parTimeInput;
    private OptionButton? _levelModeOption;

    public string LevelName => _levelNameInput?.Text ?? string.Empty;
    public string Author => _authorInput?.Text ?? string.Empty;
    public float ParTimeSeconds => _parTimeInput is null ? 60f : (float)_parTimeInput.Value;
    public string LevelMode
    {
        get
        {
            if (_levelModeOption == null)
            {
                return "Finite";
            }

            return _levelModeOption.GetSelectedId() == 1 ? "Endless" : "Finite";
        }
    }

    public override void _EnterTree()
    {
        base._EnterTree();
        CacheInputNodes();
        AddToGroup(MetadataPanelGroup, true);
    }

    public override void _Ready()
    {
        base._Ready();
        CacheInputNodes();
    }

    public override void _ExitTree()
    {
        RemoveFromGroup(MetadataPanelGroup);
        base._ExitTree();
    }

    public void ApplyMetadata(string? levelName, string? author, float parTimeSeconds, string? levelMode = "Finite")
    {
        CacheInputNodes();
        if (_levelNameInput != null)
        {
            _levelNameInput.Text = Sanitize(levelName, "Untitled Track");
        }

        if (_authorInput != null)
        {
            _authorInput.Text = Sanitize(author, "Anonymous");
        }

        if (_parTimeInput != null)
        {
            _parTimeInput.Value = Mathf.Max(1f, parTimeSeconds);
        }

        if (_levelModeOption != null)
        {
            var normalized = Sanitize(levelMode, "Finite");
            var index = normalized.Equals("Endless", StringComparison.OrdinalIgnoreCase) ? 1 : 0;
            _levelModeOption.Select(index);
        }
    }

    public void ResetToDefaults()
    {
        ApplyMetadata("Untitled Track", "Anonymous", 60f, "Finite");
    }

    private void CacheInputNodes()
    {
        _levelNameInput ??= GetNodeOrNull<LineEdit>(LevelNameInputPath);
        _authorInput ??= GetNodeOrNull<LineEdit>(AuthorInputPath);
        _parTimeInput ??= GetNodeOrNull<SpinBox>(ParTimeInputPath);
        _levelModeOption ??= GetNodeOrNull<OptionButton>(LevelModeOptionPath);
        if (_levelModeOption != null && _levelModeOption.ItemCount == 0)
        {
            _levelModeOption.AddItem("Finite", 0);
            _levelModeOption.AddItem("Endless", 1);
            _levelModeOption.Select(0);
        }
    }

    private static string Sanitize(string? value, string fallback)
    {
        return string.IsNullOrWhiteSpace(value) ? fallback : value;
    }
}
