using Godot;
using System;

[Tool]
public partial class TrackMeshGenerator : Path3D
{
    [Export] public float TrackWidth { get; set; } = 6.0f;
    [Export] public float TextureScale { get; set; } = 1.0f;

    private MeshInstance3D _meshInstance;

    public override void _Ready()
    {
        EnsureMeshInstance();
        if (Curve != null)
        {
            Curve.Changed += OnCurveChanged;
        }
        GenerateTrackMesh();
    }

    private void OnCurveChanged()
    {
        GenerateTrackMesh();
    }

    private void EnsureMeshInstance()
    {
        if (_meshInstance == null)
        {
            foreach (Node child in GetChildren())
            {
                if (child is MeshInstance3D mi && child.Name == "TrackMesh")
                {
                    _meshInstance = mi;
                    return;
                }
            }

            _meshInstance = new MeshInstance3D();
            _meshInstance.Name = "TrackMesh";
            
            AddChild(_meshInstance);

            if (Engine.IsEditorHint() && IsInsideTree())
            {
                _meshInstance.Owner = GetTree().EditedSceneRoot;
            }
        }
    }

    private void GenerateTrackMesh()
    {
        if (Curve == null || Curve.GetBakedPoints().Length < 2)
        {
            if (_meshInstance != null) _meshInstance.Mesh = null;
            return;
        }

        var surfaceTool = new SurfaceTool();
        surfaceTool.Begin(Mesh.PrimitiveType.Triangles);

        Vector3[] bakedPoints = Curve.GetBakedPoints();
        float distance = 0.0f;

        for (int i = 0; i < bakedPoints.Length; i++)
        {
            Vector3 currentPoint = bakedPoints[i];
            currentPoint.Z = 0;

            if (i > 0)
            {
                Vector3 prevPoint = bakedPoints[i - 1];
                prevPoint.Z = 0;
                distance += currentPoint.DistanceTo(prevPoint);
            }

            float vCoord = TextureScale > 0 ? distance / TextureScale : 0;

            surfaceTool.SetUV(new Vector2(0f, vCoord));
            surfaceTool.AddVertex(new Vector3(currentPoint.X, currentPoint.Y, -TrackWidth / 2.0f));

            surfaceTool.SetUV(new Vector2(1f, vCoord));
            surfaceTool.AddVertex(new Vector3(currentPoint.X, currentPoint.Y, TrackWidth / 2.0f));
        }

        for (int i = 0; i < bakedPoints.Length - 1; i++)
        {
            int prevLeft = i * 2;
            int prevRight = i * 2 + 1;
            int currLeft = (i + 1) * 2;
            int currRight = (i + 1) * 2 + 1;

            surfaceTool.AddIndex(prevLeft);
            surfaceTool.AddIndex(prevRight);
            surfaceTool.AddIndex(currLeft);

            surfaceTool.AddIndex(prevRight);
            surfaceTool.AddIndex(currRight);
            surfaceTool.AddIndex(currLeft);
        }

        surfaceTool.GenerateNormals();
        surfaceTool.GenerateTangents();

        _meshInstance.Mesh = surfaceTool.Commit();
    }
}
