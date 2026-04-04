using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using CurveCanvas.Editor;
using Godot;
using Godot.Collections;

[Tool]
public partial class TrackMeshGenerator : Path3D
{
    [Export] public float TrackWidth { get; set; } = 6.0f;
    [Export] public float TextureScale { get; set; } = 1.0f;

    [Export(PropertyHint.Range, "0.1,1000,0.1")]
    public float TextureRepeatDistance { get; set; } = 5.0f;

    [ExportGroup("Segment Metadata")]
    [Export] public Dictionary SegmentMetadata { get; set; } = new();

    [ExportGroup("Segment Profiles")]
    [Export] public Material? FlowMaterial { get; set; }

    [Export] public Material? RailMaterial { get; set; }

    [Export(PropertyHint.Range, "-200,0,0.5")]
    public float FlowBottomY { get; set; } = -50.0f;

    [Export(PropertyHint.Range, "0.05,10,0.05")]
    public float RailThickness { get; set; } = 0.6f;

    [Export(PropertyHint.Range, "0.05,10,0.05")]
    public float RailHeight { get; set; } = 0.25f;

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

    public string CurrentSegmentType => ResolveSegmentType();

    private MeshInstance3D? _meshInstance;
    private StaticBody3D? _collisionBody;
    private CollisionShape3D? _collisionShape;

    private bool _isRebuilding;
    private bool _pendingRebuild;
    private MeshBuildResult? _pendingResult;
    private readonly object _resultLock = new();

    public void SetSegmentType(string? segmentType)
    {
        var type = string.IsNullOrWhiteSpace(segmentType) ? "Flow" : segmentType!;
        EnsureSegmentMetadataDefaults();
        SegmentMetadata["SegmentType"] = type;
        GenerateTrackMesh();
    }

    private const string TrackMeshNodeName = "TrackMesh";
    private const string TrackColliderNodeName = "TrackCollider";
    private const string TrackCollisionShapeNodeName = "TrackCollisionShape";
    private const string ApplyResultMethodName = nameof(ApplyPendingMeshResult);
    private const string HandleFailureMethodName = nameof(HandleMeshBuildFailure);

    public override void _Ready()
    {
        EnsureMeshInstance();
        EnsureCollisionNodes();
        EnsureSegmentMetadataDefaults();
        if (Curve != null)
        {
            Curve.Changed += OnCurveChanged;
        }

        GenerateTrackMesh();
    }

    public override void _ExitTree()
    {
        if (Curve != null)
        {
            Curve.Changed -= OnCurveChanged;
        }
    }

    private void OnCurveChanged()
    {
        GenerateTrackMesh();
    }

    private void EnsureMeshInstance()
    {
        if (_meshInstance != null && IsInstanceValid(_meshInstance))
        {
            return;
        }

        _meshInstance = GetNodeOrNull<MeshInstance3D>(TrackMeshNodeName);
        if (_meshInstance != null)
        {
            return;
        }

        _meshInstance = new MeshInstance3D
        {
            Name = TrackMeshNodeName
        };

        AddChild(_meshInstance);
        ApplyEditorOwnership(_meshInstance);
    }

    private void EnsureCollisionNodes()
    {
        if (_collisionBody == null || !IsInstanceValid(_collisionBody))
        {
            _collisionBody = GetNodeOrNull<StaticBody3D>(TrackColliderNodeName);
            if (_collisionBody == null)
            {
                _collisionBody = new StaticBody3D
                {
                    Name = TrackColliderNodeName
                };
                AddChild(_collisionBody);
                ApplyEditorOwnership(_collisionBody);
            }
        }

        if (_collisionShape != null && IsInstanceValid(_collisionShape))
        {
            return;
        }

        if (_collisionBody == null)
        {
            return;
        }

        _collisionShape = _collisionBody.GetNodeOrNull<CollisionShape3D>(TrackCollisionShapeNodeName);
        if (_collisionShape == null)
        {
            _collisionShape = new CollisionShape3D
            {
                Name = TrackCollisionShapeNodeName
            };
            _collisionBody.AddChild(_collisionShape);
            ApplyEditorOwnership(_collisionShape);
        }
    }

    private void EnsureSegmentMetadataDefaults()
    {
        SegmentMetadata ??= new Dictionary();
        if (!SegmentMetadata.ContainsKey("SegmentType"))
        {
            SegmentMetadata["SegmentType"] = "Flow";
        }
    }

    private string ResolveSegmentType()
    {
        EnsureSegmentMetadataDefaults();
        var value = SegmentMetadata["SegmentType"];
        return value.VariantType switch
        {
            Variant.Type.StringName => value.AsStringName().ToString(),
            Variant.Type.String => value.AsString(),
            _ => value.ToString() ?? "Flow"
        };
    }

    private void ApplyEditorOwnership(Node node)
    {
        if (!Engine.IsEditorHint() || !IsInsideTree())
        {
            return;
        }

        var owner = GetTree()?.CurrentScene;
        if (owner != null)
        {
            node.Owner = owner;
        }
    }

    private void GenerateTrackMesh()
    {
        EnsureMeshInstance();
        EnsureCollisionNodes();

        if (_meshInstance == null)
        {
            return;
        }

        if (Curve == null)
        {
            ClearOutputs();
            return;
        }

        // Force high-resolution baking to eliminate jagged edges in play mode
        if (!Mathf.IsEqualApprox(Curve.BakeInterval, 0.5f))
        {
            Curve.BakeInterval = 0.5f;
        }

        var bakedPoints = Curve.GetBakedPoints();
        if (bakedPoints.Length < 2)
        {
            ClearOutputs();
            return;
        }

        if (_isRebuilding)
        {
            _pendingRebuild = true;
            return;
        }

        _isRebuilding = true;

        Task.Run(() =>
        {
            try
            {
                var result = BuildMeshData(bakedPoints);
                lock (_resultLock)
                {
                    _pendingResult = result;
                }

                CallDeferred(ApplyResultMethodName);
            }
            catch (Exception ex)
            {
                lock (_resultLock)
                {
                    _pendingResult = null;
                }

                var message = string.IsNullOrWhiteSpace(ex.Message) ? ex.GetType().Name : ex.Message;
                CallDeferred(HandleFailureMethodName, message);
            }
        });
    }

    private void ClearOutputs()
    {
        if (_meshInstance != null)
        {
            _meshInstance.Mesh = null;
        }

        if (_collisionShape != null)
        {
            _collisionShape.Shape = null;
        }
    }

    private Material? GetActiveMaterial()
    {
        var isRail = CurrentSegmentType.Equals("Rail", StringComparison.OrdinalIgnoreCase);
        return isRail ? (RailMaterial ?? FlowMaterial) : (FlowMaterial ?? RailMaterial);
    }

    private void ApplyPendingMeshResult()
    {
        MeshBuildResult? result;
        lock (_resultLock)
        {
            result = _pendingResult;
            _pendingResult = null;
        }

        if (result == null || _meshInstance == null)
        {
            FinalizeRebuild();
            return;
        }

        ApplyMeshBuildResult(result);
        FinalizeRebuild();
    }

    private void HandleMeshBuildFailure(string message)
    {
        GD.PushError($"[TrackMeshGenerator] Mesh rebuild failed: {message}");
        FinalizeRebuild();
    }

    private void FinalizeRebuild()
    {
        _isRebuilding = false;

        if (_pendingRebuild)
        {
            _pendingRebuild = false;
            GenerateTrackMesh();
        }
    }

    private void ApplyMeshBuildResult(MeshBuildResult result)
    {
        if (_meshInstance == null)
        {
            return;
        }

        if (!result.VisualSurface.HasData)
        {
            _meshInstance.Mesh = null;
        }
        else
        {
            var mesh = new ArrayMesh();
            AddSurface(mesh, result.VisualSurface, GetActiveMaterial());
            _meshInstance.Mesh = mesh;
        }

        if (_collisionShape != null)
        {
            if (result.ColliderTriangles.Length == 0)
            {
                _collisionShape.Shape = null;
            }
            else
            {
                var concaveShape = new ConcavePolygonShape3D
                {
                    Data = result.ColliderTriangles
                };
                _collisionShape.Shape = concaveShape;
            }
        }
    }

    private static bool AddSurface(ArrayMesh mesh, SurfaceMeshData data, Material? material)
    {
        if (!data.HasData)
        {
            return false;
        }

        var arrays = new Godot.Collections.Array();
        arrays.Resize((int)Mesh.ArrayType.Max);
        arrays[(int)Mesh.ArrayType.Vertex] = data.Vertices;
        arrays[(int)Mesh.ArrayType.Normal] = data.Normals;
        arrays[(int)Mesh.ArrayType.TexUV] = data.UVs;
        arrays[(int)Mesh.ArrayType.Index] = data.Indices;

        mesh.AddSurfaceFromArrays(Mesh.PrimitiveType.Triangles, arrays);

        if (material != null)
        {
            mesh.SurfaceSetMaterial(mesh.GetSurfaceCount() - 1, material);
        }

        return true;
    }

    private MeshBuildResult BuildMeshData(Vector3[] bakedPoints)
    {
        var flattened = FlattenBakedPoints(bakedPoints, out var vCoordinates);
        var collider = BuildColliderTriangleStrip(flattened);

        var surface = CurrentSegmentType.Equals("Rail", StringComparison.OrdinalIgnoreCase)
            ? BuildRailSurface(flattened, vCoordinates)
            : BuildFlowSurface(flattened, vCoordinates);

        return new MeshBuildResult(surface, collider);
    }

    private Vector3[] FlattenBakedPoints(Vector3[] bakedPoints, out float[] cumulativeDistances)
    {
        var flattened = new Vector3[bakedPoints.Length];
        cumulativeDistances = new float[bakedPoints.Length];
        var cumulativeDistance = 0f;

        for (var i = 0; i < bakedPoints.Length; i++)
        {
            var flattenedPoint = bakedPoints[i];
            flattenedPoint.Z = 0f;

            if (i > 0)
            {
                cumulativeDistance += flattenedPoint.DistanceTo(flattened[i - 1]);
            }

            flattened[i] = flattenedPoint;
            cumulativeDistances[i] = cumulativeDistance;
        }

        return flattened;
    }

    private float DistanceToRepeatU(float distance)
    {
        return TextureRepeatDistance > Mathf.Epsilon
            ? distance / TextureRepeatDistance
            : 0f;
    }

    private SurfaceMeshData BuildFlowSurface(Vector3[] flattenedPoints, float[] vCoordinates)
    {
        var accumulator = new SurfaceAccumulator();
        var halfWidth = Mathf.Max(0.1f, TrackWidth * 0.5f);

        for (var i = 0; i < flattenedPoints.Length - 1; i++)
        {
            var start = flattenedPoints[i];
            var end = flattenedPoints[i + 1];
            var startDistance = vCoordinates[i];
            var endDistance = vCoordinates[i + 1];
            var startU = DistanceToRepeatU(startDistance);
            var endU = DistanceToRepeatU(endDistance);

            var startFrontTop = new Vector3(start.X, start.Y, halfWidth);
            var startBackTop = new Vector3(start.X, start.Y, -halfWidth);
            var endFrontTop = new Vector3(end.X, end.Y, halfWidth);
            var endBackTop = new Vector3(end.X, end.Y, -halfWidth);

            var startFrontBottom = new Vector3(start.X, FlowBottomY, halfWidth);
            var startBackBottom = new Vector3(start.X, FlowBottomY, -halfWidth);
            var endFrontBottom = new Vector3(end.X, FlowBottomY, halfWidth);
            var endBackBottom = new Vector3(end.X, FlowBottomY, -halfWidth);

            // top deck that follows the spline profile
            accumulator.AppendQuad(
                startBackTop,
                startFrontTop,
                endBackTop,
                endFrontTop,
                startU,
                endU,
                0f,
                1f);

            // front wall (positive Z)
            accumulator.AppendQuad(
                startFrontTop,
                endFrontTop,
                startFrontBottom,
                endFrontBottom,
                startU,
                endU,
                0f,
                1f);

            // back wall (negative Z)
            accumulator.AppendQuad(
                endBackTop,
                startBackTop,
                endBackBottom,
                startBackBottom,
                startU,
                endU,
                0f,
                1f);

            // bottom face to close off the volume
            accumulator.AppendQuad(
                startFrontBottom,
                startBackBottom,
                endFrontBottom,
                endBackBottom,
                startU,
                endU,
                0f,
                1f);
        }

        return accumulator.ToSurfaceData();
    }

    private SurfaceMeshData BuildRailSurface(Vector3[] flattenedPoints, float[] vCoordinates)
    {
        var accumulator = new SurfaceAccumulator();
        var halfWidth = Mathf.Max(0.05f, RailThickness * 0.5f);
        var halfHeight = Mathf.Max(0.05f, RailHeight * 0.5f);

        for (var i = 0; i < flattenedPoints.Length - 1; i++)
        {
            var start = flattenedPoints[i];
            var end = flattenedPoints[i + 1];
            var startTopLeft = new Vector3(start.X, start.Y + halfHeight, -halfWidth);
            var startTopRight = new Vector3(start.X, start.Y + halfHeight, halfWidth);
            var endTopLeft = new Vector3(end.X, end.Y + halfHeight, -halfWidth);
            var endTopRight = new Vector3(end.X, end.Y + halfHeight, halfWidth);

            var startBottomLeft = new Vector3(start.X, start.Y - halfHeight, -halfWidth);
            var startBottomRight = new Vector3(start.X, start.Y - halfHeight, halfWidth);
            var endBottomLeft = new Vector3(end.X, end.Y - halfHeight, -halfWidth);
            var endBottomRight = new Vector3(end.X, end.Y - halfHeight, halfWidth);

            var startDistance = vCoordinates[i];
            var endDistance = vCoordinates[i + 1];
            var startU = DistanceToRepeatU(startDistance);
            var endU = DistanceToRepeatU(endDistance);

            // top (width across Z axis)
            accumulator.AppendQuad(startTopLeft, startTopRight, endTopLeft, endTopRight, startU, endU, 0f, 1f);
            // bottom
            accumulator.AppendQuad(startBottomRight, startBottomLeft, endBottomRight, endBottomLeft, startU, endU, 0f, 1f);
            // left side (vertical)
            accumulator.AppendQuad(startTopLeft, startBottomLeft, endTopLeft, endBottomLeft, startU, endU, 0f, 1f);
            // right side (vertical)
            accumulator.AppendQuad(startBottomRight, startTopRight, endBottomRight, endTopRight, startU, endU, 0f, 1f);
        }

        return accumulator.ToSurfaceData();
    }

    private Vector3[] BuildColliderTriangleStrip(IReadOnlyList<Vector3> flattenedPoints)
    {
        var collider = new List<Vector3>(Math.Max(0, (flattenedPoints.Count - 1) * 6));
        var halfWidth = TrackWidth * 0.5f;
        for (var i = 0; i < flattenedPoints.Count - 1; i++)
        {
            var start = flattenedPoints[i];
            var end = flattenedPoints[i + 1];
            var startLeft = new Vector3(start.X, start.Y, -halfWidth);
            var startRight = new Vector3(start.X, start.Y, halfWidth);
            var endLeft = new Vector3(end.X, end.Y, -halfWidth);
            var endRight = new Vector3(end.X, end.Y, halfWidth);
            AppendColliderTriangles(collider, startLeft, startRight, endLeft, endRight);
        }

        return collider.ToArray();
    }

    private static void AppendColliderTriangles(List<Vector3> collider, Vector3 startLeft, Vector3 startRight, Vector3 endLeft, Vector3 endRight)
    {
        collider.Add(startLeft);
        collider.Add(startRight);
        collider.Add(endLeft);

        collider.Add(startRight);
        collider.Add(endRight);
        collider.Add(endLeft);
    }

    private sealed class SurfaceAccumulator
    {
        private readonly List<Vector3> _vertices = new();
        private readonly List<Vector3> _normals = new();
        private readonly List<Vector2> _uvs = new();
        private readonly List<int> _indices = new();

        public void AppendQuad(Vector3 v0, Vector3 v1, Vector3 v2, Vector3 v3, float startU, float endU, float startV, float endV)
        {
            var baseIndex = _vertices.Count;

            _vertices.Add(v0);
            _vertices.Add(v1);
            _vertices.Add(v2);
            _vertices.Add(v3);

            _uvs.Add(new Vector2(startU, startV));
            _uvs.Add(new Vector2(endU, startV));
            _uvs.Add(new Vector2(startU, endV));
            _uvs.Add(new Vector2(endU, endV));

            var normal = CalculateQuadNormal(v0, v1, v2);
            _normals.Add(normal);
            _normals.Add(normal);
            _normals.Add(normal);
            _normals.Add(normal);

            _indices.Add(baseIndex);
            _indices.Add(baseIndex + 2);
            _indices.Add(baseIndex + 1);
            _indices.Add(baseIndex + 1);
            _indices.Add(baseIndex + 2);
            _indices.Add(baseIndex + 3);
        }

        public SurfaceMeshData ToSurfaceData()
        {
            return new SurfaceMeshData(
                _vertices.ToArray(),
                _normals.ToArray(),
                _uvs.ToArray(),
                _indices.ToArray()
            );
        }
    }

    private static Vector3 CalculateQuadNormal(Vector3 v0, Vector3 v1, Vector3 v2)
    {
        var edge1 = v1 - v0;
        var edge2 = v2 - v0;
        var normal = edge1.Cross(edge2);
        if (normal.LengthSquared() <= Mathf.Epsilon)
        {
            return Vector3.Up;
        }

        return normal.Normalized();
    }

    private sealed class MeshBuildResult
    {
        public MeshBuildResult(SurfaceMeshData visualSurface, Vector3[] colliderTriangles)
        {
            VisualSurface = visualSurface;
            ColliderTriangles = colliderTriangles;
        }

        public SurfaceMeshData VisualSurface { get; }
        public Vector3[] ColliderTriangles { get; }
    }

    private readonly struct SurfaceMeshData
    {
        public SurfaceMeshData(Vector3[] vertices, Vector3[] normals, Vector2[] uvs, int[] indices)
        {
            Vertices = vertices;
            Normals = normals;
            UVs = uvs;
            Indices = indices;
        }

        public Vector3[] Vertices { get; }
        public Vector3[] Normals { get; }
        public Vector2[] UVs { get; }
        public int[] Indices { get; }

        public bool HasData => Vertices.Length > 0 && Indices.Length > 0;
    }
}
