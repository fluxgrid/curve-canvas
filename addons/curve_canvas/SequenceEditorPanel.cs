using System;
using System.Linq;
using CurveCanvas.AuthoringCore;
using Godot;

namespace CurveCanvas.Editor;

public partial class SequenceEditorPanel : PanelContainer
{
    public event Action? CloseRequested;

    private ItemList? _chunkList;
    private Button? _moveUpButton;
    private Button? _moveDownButton;
    private Button? _removeButton;
    private Button? _closeButton;
    private FileDialog? _addChunkDialog;
    private FileDialog? _loadSequenceDialog;
    private FileDialog? _saveSequenceDialog;
    private SequenceVisualizer? _visualizer;
    private CurveSequenceData _sequenceData = new();

    public override void _Ready()
    {
        _chunkList = GetNodeOrNull<ItemList>("VBoxContainer/ChunkList");
        _moveUpButton = GetNodeOrNull<Button>("VBoxContainer/MoveButtons/MoveUpButton");
        _moveDownButton = GetNodeOrNull<Button>("VBoxContainer/MoveButtons/MoveDownButton");
        _removeButton = GetNodeOrNull<Button>("VBoxContainer/ActionButtons/RemoveChunkButton");
        _addChunkDialog = GetNodeOrNull<FileDialog>("AddChunkDialog");
        _saveSequenceDialog = GetNodeOrNull<FileDialog>("SaveSequenceDialog");

        GetNode<Button>("VBoxContainer/ActionButtons/AddChunkButton").Pressed += OnAddChunkPressed;
        _removeButton!.Pressed += OnRemovePressed;
        _moveUpButton!.Pressed += () => MoveSelectedChunk(-1);
        _moveDownButton!.Pressed += () => MoveSelectedChunk(1);
        GetNode<Button>("VBoxContainer/ActionButtons/SaveButton").Pressed += OnSavePressed;
        GetNode<Button>("VBoxContainer/ActionButtons/LoadButton").Pressed += OnLoadPressed;
        _closeButton = GetNodeOrNull<Button>("VBoxContainer/Header/CloseButton");
        if (_closeButton != null)
        {
            _closeButton.Pressed += OnClosePressed;
        }

        _chunkList!.ItemSelected += _ => UpdateButtonStates();

        if (_addChunkDialog != null)
        {
            _addChunkDialog.Access = FileDialog.AccessEnum.Userdata;
            _addChunkDialog.FileSelected += path => AddChunk(path);
        }

        _loadSequenceDialog = GetNodeOrNull<FileDialog>("LoadSequenceDialog");
        if (_loadSequenceDialog != null)
        {
            _loadSequenceDialog.Access = FileDialog.AccessEnum.Userdata;
            _loadSequenceDialog.FileSelected += path => LoadSequence(path);
        }

        if (_saveSequenceDialog != null)
        {
            _saveSequenceDialog.Access = FileDialog.AccessEnum.Userdata;
            _saveSequenceDialog.FileSelected += path => CurveSequenceSerializer.Save(path, _sequenceData);
        }

        UpdateButtonStates();
    }

    public void ConfigureVisualizer(SequenceVisualizer? visualizer)
    {
        _visualizer = visualizer;
        RefreshVisualization();
    }

    public void SetActive(bool active)
    {
        Visible = active;
        _visualizer?.SetVisualizationEnabled(active);
    }

    public void RebuildVisualization()
    {
        RefreshVisualization();
    }

    private void OnAddChunkPressed()
    {
        _addChunkDialog?.PopupCenteredRatio();
    }

    private void OnLoadPressed()
    {
        _loadSequenceDialog?.PopupCenteredRatio();
    }

    private void OnClosePressed()
    {
        CloseRequested?.Invoke();
    }

    private void OnRemovePressed()
    {
        if (_chunkList == null)
        {
            return;
        }

        var selection = _chunkList.GetSelectedItems();
        if (selection.Length == 0)
        {
            return;
        }

        var index = selection[0];
        if (index >= 0 && index < _sequenceData.ChunkPaths.Count)
        {
            _sequenceData.ChunkPaths.RemoveAt(index);
            RefreshList();
        }
    }

    private void OnSavePressed()
    {
        if (_saveSequenceDialog == null)
        {
            return;
        }

        _saveSequenceDialog.CurrentFile = "sequence.curvesequence.json";
        _saveSequenceDialog.PopupCenteredRatio();
    }

    private void AddChunk(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return;
        }

        _sequenceData.ChunkPaths.Add(path);
        RefreshList();
    }

    private void LoadSequence(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return;
        }

        var data = CurveSequenceSerializer.Load(path);
        if (data == null)
        {
            return;
        }

        _sequenceData = data;
        RefreshList();
    }

    private void MoveSelectedChunk(int direction)
    {
        if (_chunkList == null)
        {
            return;
        }

        var selection = _chunkList.GetSelectedItems();
        if (selection.Length == 0)
        {
            return;
        }

        var index = selection[0];
        var targetIndex = index + direction;
        if (targetIndex < 0 || targetIndex >= _sequenceData.ChunkPaths.Count)
        {
            return;
        }

        var item = _sequenceData.ChunkPaths[index];
        _sequenceData.ChunkPaths.RemoveAt(index);
        _sequenceData.ChunkPaths.Insert(targetIndex, item);
        RefreshList(targetIndex);
    }

    private void RefreshList(int selectedIndex = -1)
    {
        _chunkList?.Clear();
        foreach (var chunk in _sequenceData.ChunkPaths)
        {
            _chunkList?.AddItem(System.IO.Path.GetFileName(chunk));
        }

        if (selectedIndex >= 0 && selectedIndex < _chunkList?.GetItemCount())
        {
            _chunkList?.Select(selectedIndex);
        }
        UpdateButtonStates();
        RefreshVisualization();
    }

    private void UpdateButtonStates()
    {
        var hasItems = _sequenceData.ChunkPaths.Count > 0;
        var selection = _chunkList?.GetSelectedItems() ?? Array.Empty<int>();
        var index = selection.Length > 0 ? selection[0] : -1;
        var hasSelection = index >= 0;

        _removeButton!.Disabled = !hasSelection;
        _moveUpButton!.Disabled = !hasSelection || index <= 0;
        _moveDownButton!.Disabled = !hasSelection || index >= _sequenceData.ChunkPaths.Count - 1;
    }

    private void RefreshVisualization()
    {
        _visualizer?.RebuildVisuals(_sequenceData.ChunkPaths);
    }
}
