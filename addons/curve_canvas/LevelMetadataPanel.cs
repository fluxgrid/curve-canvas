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

    private LineEdit? _levelNameInput;
    private LineEdit? _authorInput;
    private SpinBox? _parTimeInput;

    public string LevelName => _levelNameInput?.Text ?? string.Empty;
    public string Author => _authorInput?.Text ?? string.Empty;
    public float ParTimeSeconds => _parTimeInput is null ? 60f : (float)_parTimeInput.Value;

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

    public void ApplyMetadata(string? levelName, string? author, float parTimeSeconds)
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
    }

    public void ResetToDefaults()
    {
        ApplyMetadata("Untitled Track", "Anonymous", 60f);
    }

    private void CacheInputNodes()
    {
        _levelNameInput ??= GetNodeOrNull<LineEdit>(LevelNameInputPath);
        _authorInput ??= GetNodeOrNull<LineEdit>(AuthorInputPath);
        _parTimeInput ??= GetNodeOrNull<SpinBox>(ParTimeInputPath);
    }

    private static string Sanitize(string? value, string fallback)
    {
        return string.IsNullOrWhiteSpace(value) ? fallback : value;
    }
}
