using Godot;

namespace CurveCanvas.Editor;

/// <summary>
/// Procedurally generates a flat 2.5D track mesh along the assigned Curve3D.
/// </summary>
[Tool]
public partial class TrackMeshGenerator : Path3D
{
    private const string MeshNodeName = "GeneratedTrackMesh";

    private MeshInstance3D? _meshInstance;
    private Curve3D? _subscribedCurve;
    private bool _meshDirty = true;
    private float _trackWidth = 6.0f;
    private float _textureScale = 1.0f;

    [Export(PropertyHint.Range, "0.1,100,0.1")]
    public float TrackWidth
    {
        get => _trackWidth;
        set
        {
            var clamped = Mathf.Max(0.1f, value);
            if (Mathf.IsEqualApprox(_trackWidth, clamped))
            {
                return;
            }

            _trackWidth = clamped;
            MarkMeshDirty();
        }
    }

    [Export(PropertyHint.Range, "0.1,100,0.1")]
    public float TextureScale
    {
        get => _textureScale;
        set
        {
            var clamped = Mathf.Max(0.1f, value);
            if (Mathf.IsEqualApprox(_textureScale, clamped))
            {
                return;
            }

            _textureScale = clamped;
            MarkMeshDirty();
        }
    }

    public override void _Ready()
    {
        EnsureMeshInstance();
        SubscribeToCurve();
        MarkMeshDirty();
        SetProcess(Engine.IsEditorHint());
    }

    public override void _ExitTree()
    {
        base._ExitTree();
        UnsubscribeFromCurve();
    }

    public override void _Process(double delta)
    {
        if (!Engine.IsEditorHint())
        {
            SetProcess(false);
            return;
        }

        if (_subscribedCurve != Curve)
        {
            SubscribeToCurve();
        }

        if (_meshDirty)
        {
            GenerateTrackMesh();
            _meshDirty = false;
        }
    }

    private void EnsureMeshInstance()
    {
        _meshInstance = GetNodeOrNull<MeshInstance3D>(MeshNodeName);
        if (_meshInstance != null)
        {
            return;
        }

        _meshInstance = new MeshInstance3D
        {
            Name = MeshNodeName
        };

        AddChild(_meshInstance);
        if (Engine.IsEditorHint())
        {
            var owner = GetTree()?.EditedSceneRoot ?? GetOwner();
            if (owner != null)
            {
                _meshInstance.Owner = owner;
            }
        }
    }

    private void SubscribeToCurve()
    {
        UnsubscribeFromCurve();

        if (Curve == null)
        {
            return;
        }

        _subscribedCurve = Curve;
        _subscribedCurve.Changed += OnCurveChanged;
    }

    private void UnsubscribeFromCurve()
    {
        if (_subscribedCurve != null)
        {
            _subscribedCurve.Changed -= OnCurveChanged;
        }

        _subscribedCurve = null;
    }

    private void OnCurveChanged()
    {
        MarkMeshDirty();
    }

    private void MarkMeshDirty()
    {
        _meshDirty = true;
        if (Engine.IsEditorHint())
        {
            SetProcess(true);
        }
    }

    private void GenerateTrackMesh()
    {
        EnsureMeshInstance();

        if (Curve == null || _meshInstance == null)
        {
            return;
        }

        var bakedPoints = Curve.GetBakedPoints();
        if (bakedPoints.Length < 2)
        {
            _meshInstance.Mesh = null;
            return;
        }

        var surfaceTool = new SurfaceTool();
        surfaceTool.Begin(Mesh.PrimitiveType.Triangles);

        float cumulativeDistance = 0f;
        Vector3 previousPoint = default;
        bool hasPreviousPoint = false;
        int vertexIndex = 0;

        foreach (var rawPoint in bakedPoints)
        {
            var planarPoint = new Vector3(rawPoint.X, rawPoint.Y, 0f);

            if (hasPreviousPoint)
            {
                cumulativeDistance += planarPoint.DistanceTo(previousPoint);
            }
            else
            {
                hasPreviousPoint = true;
            }

            previousPoint = planarPoint;

            var halfWidth = _trackWidth * 0.5f;
            var leftVertex = new Vector3(planarPoint.X, planarPoint.Y, -halfWidth);
            var rightVertex = new Vector3(planarPoint.X, planarPoint.Y, halfWidth);

            var distanceV = cumulativeDistance / _textureScale;
            var leftUv = new Vector2(0f, distanceV);
            var rightUv = new Vector2(1f, distanceV);

            surfaceTool.SetUV(leftUv);
            surfaceTool.AddVertex(leftVertex);

            surfaceTool.SetUV(rightUv);
            surfaceTool.AddVertex(rightVertex);

            if (vertexIndex >= 2)
            {
                int prevLeft = vertexIndex - 2;
                int prevRight = vertexIndex - 1;
                int currLeft = vertexIndex;
                int currRight = vertexIndex + 1;

                surfaceTool.AddIndex(prevLeft);
                surfaceTool.AddIndex(currLeft);
                surfaceTool.AddIndex(prevRight);

                surfaceTool.AddIndex(prevRight);
                surfaceTool.AddIndex(currLeft);
                surfaceTool.AddIndex(currRight);
            }

            vertexIndex += 2;
        }

        surfaceTool.GenerateNormals();
        surfaceTool.GenerateTangents();

        var mesh = surfaceTool.Commit();
        _meshInstance.Mesh = mesh;
    }
}
