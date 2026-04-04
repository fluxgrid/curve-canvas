using System;
using Godot;

namespace CurveCanvas.Editor;

[Tool]
public partial class SegmentPropertiesPanel : CanvasLayer
{
    [Export]
    public NodePath SegmentTypeDropdownPath { get; set; } = new();

    [Export]
    public NodePath TrackGeneratorPath { get; set; } = new();

    private OptionButton? _segmentTypeDropdown;
    private TrackMeshGenerator? _track;
    private bool _suppressSelection;
    private static readonly string[] SegmentTypes = { "Flow", "Rail" };

    public override void _Ready()
    {
        CacheNodes();
        PopulateDropdown();
        SyncFromTrack();
    }

    private void CacheNodes()
    {
        _segmentTypeDropdown ??= GetNodeOrNull<OptionButton>(SegmentTypeDropdownPath);
        if (_segmentTypeDropdown != null)
        {
            _segmentTypeDropdown.ItemSelected -= OnSegmentTypeSelected;
            _segmentTypeDropdown.ItemSelected += OnSegmentTypeSelected;
        }

        if (!TrackGeneratorPath.IsEmpty)
        {
            _track = GetNodeOrNull<TrackMeshGenerator>(TrackGeneratorPath);
        }
    }

    private void PopulateDropdown()
    {
        if (_segmentTypeDropdown == null)
        {
            return;
        }

        _segmentTypeDropdown.Clear();
        foreach (var type in SegmentTypes)
        {
            _segmentTypeDropdown.AddItem(type);
        }
    }

    private void SyncFromTrack()
    {
        if (_segmentTypeDropdown == null || _track == null)
        {
            return;
        }

        var current = _track.CurrentSegmentType;
        var index = Array.FindIndex(SegmentTypes, t => t.Equals(current, StringComparison.OrdinalIgnoreCase));
        if (index < 0)
        {
            index = 0;
        }

        _suppressSelection = true;
        _segmentTypeDropdown.Select(index);
        _suppressSelection = false;
    }

    private void OnSegmentTypeSelected(long index)
    {
        if (_suppressSelection)
        {
            return;
        }

        var resolvedIndex = (int)Math.Clamp(index, 0, SegmentTypes.Length - 1);
        var nextType = SegmentTypes[resolvedIndex];
        if (_track == null)
        {
            return;
        }

        _track.SetSegmentType(nextType);
    }
}
