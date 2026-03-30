using System;
using System.Collections.Generic;
using System.Threading.Tasks;
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
    private StaticBody3D? _collisionBody;
    private CollisionShape3D? _collisionShape;

    private bool _isRebuilding;
    private bool _pendingRebuild;
    private MeshBuildResult? _pendingResult;
    private readonly object _resultLock = new();

    private enum SurfaceCategory
    {
        HighGrip,
        Ice
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

    private void ApplyEditorOwnership(Node node)
    {
        if (!Engine.IsEditorHint() || !IsInsideTree())
        {
            return;
        }

        var owner = GetTree()?.EditedSceneRoot;
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

        var settings = new MeshBuildSettings(
            TrackWidth,
            TextureScale,
            HighGripSlopeThreshold,
            IceSlopeThreshold
        );

        _isRebuilding = true;

        Task.Run(() =>
        {
            try
            {
                var result = BuildMeshData(bakedPoints, settings);
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

        if (!result.HasGeometry)
        {
            ClearOutputs();
            return;
        }

        var mesh = new ArrayMesh();
        var hasSurface = false;
        hasSurface |= AddSurface(mesh, result.HighGripSurface, HighGripMaterial ?? IceMaterial);
        hasSurface |= AddSurface(mesh, result.IceSurface, IceMaterial ?? HighGripMaterial);

        _meshInstance.Mesh = hasSurface ? mesh : null;

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

    private static MeshBuildResult BuildMeshData(Vector3[] bakedPoints, MeshBuildSettings settings)
    {
        var flattened = new Vector3[bakedPoints.Length];
        var vCoordinates = new float[bakedPoints.Length];
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
            vCoordinates[i] = settings.TextureScale > 0f ? cumulativeDistance / settings.TextureScale : 0f;
        }

        var slopes = ComputeSegmentSlopes(bakedPoints);
        var highGripAccumulator = new SurfaceAccumulator();
        var iceAccumulator = new SurfaceAccumulator();
        var collider = new List<Vector3>(Math.Max(0, (bakedPoints.Length - 1) * 6));
        var halfWidth = settings.TrackWidth * 0.5f;

        for (var i = 0; i < bakedPoints.Length - 1; i++)
        {
            var start = flattened[i];
            var end = flattened[i + 1];
            var startPoint = new Vector3(start.X, start.Y, 0f);
            var endPoint = new Vector3(end.X, end.Y, 0f);
            var startV = vCoordinates[i];
            var endV = vCoordinates[i + 1];

            var startLeft = new Vector3(startPoint.X, startPoint.Y, -halfWidth);
            var startRight = new Vector3(startPoint.X, startPoint.Y, halfWidth);
            var endLeft = new Vector3(endPoint.X, endPoint.Y, -halfWidth);
            var endRight = new Vector3(endPoint.X, endPoint.Y, halfWidth);

            var category = ClassifySurface(slopes[i], settings.HighGripThreshold, settings.IceThreshold);
            var accumulator = category == SurfaceCategory.Ice ? iceAccumulator : highGripAccumulator;
            accumulator.AppendQuad(startLeft, startRight, endLeft, endRight, startV, endV);

            AppendColliderTriangles(collider, startLeft, startRight, endLeft, endRight);
        }

        return new MeshBuildResult(
            highGripAccumulator.ToSurfaceData(),
            iceAccumulator.ToSurfaceData(),
            collider.ToArray()
        );
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

    private static float[] ComputeSegmentSlopes(IReadOnlyList<Vector3> bakedPoints)
    {
        var segmentCount = bakedPoints.Count - 1;
        var slopes = new float[Math.Max(segmentCount, 0)];
        for (var i = 0; i < segmentCount; i++)
        {
            slopes[i] = CalculateSlopeMagnitude(bakedPoints[i], bakedPoints[i + 1]);
        }

        return slopes;
    }

    private static float CalculateSlopeMagnitude(Vector3 start, Vector3 end)
    {
        var delta = end - start;
        var planarLength = Mathf.Abs(delta.X);
        if (Mathf.IsZeroApprox(planarLength))
        {
            return 0f;
        }

        return Mathf.Abs(delta.Y) / planarLength;
    }

    private static SurfaceCategory ClassifySurface(float slope, float highGripThreshold, float iceThreshold)
    {
        var steepThreshold = Mathf.Max(iceThreshold, highGripThreshold);
        var gripThreshold = Mathf.Min(highGripThreshold, steepThreshold);

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

    private sealed class SurfaceAccumulator
    {
        private readonly List<Vector3> _vertices = new();
        private readonly List<Vector3> _normals = new();
        private readonly List<Vector2> _uvs = new();
        private readonly List<int> _indices = new();

        public void AppendQuad(Vector3 startLeft, Vector3 startRight, Vector3 endLeft, Vector3 endRight, float startV, float endV)
        {
            var baseIndex = _vertices.Count;

            _vertices.Add(startLeft);
            _vertices.Add(startRight);
            _vertices.Add(endLeft);
            _vertices.Add(endRight);

            _uvs.Add(new Vector2(0f, startV));
            _uvs.Add(new Vector2(1f, startV));
            _uvs.Add(new Vector2(0f, endV));
            _uvs.Add(new Vector2(1f, endV));

            var normal = CalculateQuadNormal(startLeft, startRight, endLeft);
            _normals.Add(normal);
            _normals.Add(normal);
            _normals.Add(normal);
            _normals.Add(normal);

            _indices.Add(baseIndex);
            _indices.Add(baseIndex + 1);
            _indices.Add(baseIndex + 2);
            _indices.Add(baseIndex + 1);
            _indices.Add(baseIndex + 3);
            _indices.Add(baseIndex + 2);
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

    private static Vector3 CalculateQuadNormal(Vector3 startLeft, Vector3 startRight, Vector3 endLeft)
    {
        return new Vector3(0f, 0f, 1f);
    }

    private readonly record struct MeshBuildSettings(
        float TrackWidth,
        float TextureScale,
        float HighGripThreshold,
        float IceThreshold
    );

    private sealed class MeshBuildResult
    {
        public MeshBuildResult(SurfaceMeshData highGripSurface, SurfaceMeshData iceSurface, Vector3[] colliderTriangles)
        {
            HighGripSurface = highGripSurface;
            IceSurface = iceSurface;
            ColliderTriangles = colliderTriangles;
        }

        public SurfaceMeshData HighGripSurface { get; }
        public SurfaceMeshData IceSurface { get; }
        public Vector3[] ColliderTriangles { get; }

        public bool HasGeometry => HighGripSurface.HasData || IceSurface.HasData;
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
