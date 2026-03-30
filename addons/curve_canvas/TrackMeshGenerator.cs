using System;
using System.Collections.Generic;
using CurveCanvas.Editor;
using Godot;

[Tool]
public partial class TrackMeshGenerator : Path3D
{
    [Export] public float TrackWidth { get; set; } = 6.0f;
    [Export] public float TextureScale { get; set; } = 1.0f;

    [ExportGroup("Track Surfacing")]
    [Export] public Material? HighGripMaterial { get; set; }

    [Export] public Material? IceMaterial { get; set; }

    [Export(PropertyHint.Range, "0,5,0.01")]
    public float HighGripSlopeThreshold { get; set; } = 0.25f;

    [Export(PropertyHint.Range, "0,5,0.01")]
    public float IceSlopeThreshold { get; set; } = 0.5f;

    [ExportGroup("Prop Brush Config")]
    [Export]
    public CurveCanvasRegistry? PropBrushRegistry { get; set; }

    [Export]
    public string PropBrushObjectId { get; set; } = string.Empty;

    [Export(PropertyHint.Range, "0.25,20,0.25")]
    public float PropBrushSampleInterval { get; set; } = 3.0f;

    [Export(PropertyHint.Range, "-200,200,0.1")]
    public float PropBrushDepthZ { get; set; } = -12.0f;

    [Export(PropertyHint.Range, "0,4,0.05")]
    public float PropBrushOffsetJitter { get; set; } = 0.75f;

    [Export]
    public int PropBrushPlaneIndex { get; set; } = 0;

    [Export]
    public ulong PropBrushNoiseSeed { get; set; } = 1337;

    private MeshInstance3D? _meshInstance;

    private enum SurfaceCategory
    {
        HighGrip,
        Ice
    }

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
        EnsureMeshInstance();
        if (_meshInstance == null)
        {
            return;
        }

        if (Curve == null || Curve.GetBakedPoints().Length < 2)
        {
            _meshInstance.Mesh = null;
            return;
        }

        Vector3[] bakedPoints = Curve.GetBakedPoints();
        var flattenedPoints = new Vector3[bakedPoints.Length];
        var vCoordinates = new float[bakedPoints.Length];
        var cumulativeDistance = 0.0f;

        for (int i = 0; i < bakedPoints.Length; i++)
        {
            var flattened = bakedPoints[i];
            flattened.Z = 0f;
            if (i > 0)
            {
                cumulativeDistance += flattened.DistanceTo(flattenedPoints[i - 1]);
            }

            flattenedPoints[i] = flattened;
            vCoordinates[i] = TextureScale > 0f ? cumulativeDistance / TextureScale : 0f;
        }

        var slopes = ComputeSegmentSlopes(bakedPoints);
        var mesh = new ArrayMesh();

        for (int i = 0; i < bakedPoints.Length - 1; i++)
        {
            var category = ClassifySurface(slopes[i]);
            AppendSegmentSurface(mesh, flattenedPoints[i], flattenedPoints[i + 1], vCoordinates[i], vCoordinates[i + 1], category);
        }

        _meshInstance.Mesh = mesh;
    }

    private float[] ComputeSegmentSlopes(IReadOnlyList<Vector3> bakedPoints)
    {
        var segmentCount = bakedPoints.Count - 1;
        var slopes = new float[Math.Max(segmentCount, 0)];
        for (int i = 0; i < segmentCount; i++)
        {
            slopes[i] = CalculateSlopeMagnitude(bakedPoints[i], bakedPoints[i + 1]);
        }

        return slopes;
    }

    private static float CalculateSlopeMagnitude(Vector3 start, Vector3 end)
    {
        var delta = end - start;
        var planarLength = Mathf.Sqrt(delta.X * delta.X + delta.Z * delta.Z);
        if (planarLength <= Mathf.Epsilon)
        {
            return 0f;
        }

        return Mathf.Abs(delta.Y) / planarLength;
    }

    private SurfaceCategory ClassifySurface(float slope)
    {
        var steepThreshold = Mathf.Max(IceSlopeThreshold, HighGripSlopeThreshold);
        var gripThreshold = Mathf.Min(HighGripSlopeThreshold, steepThreshold);

        if (slope >= steepThreshold)
        {
            return SurfaceCategory.Ice;
        }

        if (slope <= gripThreshold)
        {
            return SurfaceCategory.HighGrip;
        }

        var midpoint = (steepThreshold + gripThreshold) * 0.5f;
        return slope >= midpoint ? SurfaceCategory.Ice : SurfaceCategory.HighGrip;
    }

    private void AppendSegmentSurface(ArrayMesh mesh, Vector3 start, Vector3 end, float startV, float endV, SurfaceCategory category)
    {
        var surfaceTool = new SurfaceTool();
        surfaceTool.Begin(Mesh.PrimitiveType.Triangles);

        var halfWidth = TrackWidth * 0.5f;
        var startLeft = new Vector3(start.X, start.Y, -halfWidth);
        var startRight = new Vector3(start.X, start.Y, halfWidth);
        var endLeft = new Vector3(end.X, end.Y, -halfWidth);
        var endRight = new Vector3(end.X, end.Y, halfWidth);

        surfaceTool.SetUV(new Vector2(0f, startV));
        surfaceTool.AddVertex(startLeft);

        surfaceTool.SetUV(new Vector2(1f, startV));
        surfaceTool.AddVertex(startRight);

        surfaceTool.SetUV(new Vector2(0f, endV));
        surfaceTool.AddVertex(endLeft);

        surfaceTool.SetUV(new Vector2(1f, endV));
        surfaceTool.AddVertex(endRight);

        surfaceTool.AddIndex(0);
        surfaceTool.AddIndex(1);
        surfaceTool.AddIndex(2);

        surfaceTool.AddIndex(1);
        surfaceTool.AddIndex(3);
        surfaceTool.AddIndex(2);

        surfaceTool.GenerateNormals();
        surfaceTool.GenerateTangents();

        surfaceTool.Commit(mesh);
        var surfaceIndex = mesh.GetSurfaceCount() - 1;
        var material = category == SurfaceCategory.Ice ? IceMaterial ?? HighGripMaterial : HighGripMaterial ?? IceMaterial;
        if (material != null)
        {
            mesh.SurfaceSetMaterial(surfaceIndex, material);
        }
    }
}
