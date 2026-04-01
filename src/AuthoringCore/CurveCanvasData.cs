using System;
using System.Collections.Generic;

namespace CurveCanvas.AuthoringCore;

/// <summary>
/// Serializable payload describing a CurveCanvas scene for interchange with runtime consumers.
/// </summary>
public sealed class CurveCanvasExportData
{
    public CurveCanvasMetadata Metadata { get; set; } = new();
    public List<CurveCanvasSplinePoint> Spline { get; set; } = new();
    public List<CurveCanvasActionObject> ActionObjects { get; set; } = new();
    public List<CurveCanvasSceneryObject> SceneryObjects { get; set; } = new();
    public List<CameraTriggerData> CameraTriggers { get; set; } = new();
}

public sealed class CurveCanvasSplinePoint
{
    public float X { get; set; }
    public float Y { get; set; }
    public float InX { get; set; }
    public float InY { get; set; }
    public float OutX { get; set; }
    public float OutY { get; set; }
}

public record struct CurveCanvasActionObject(string ObjectId, float CurveOffset);

public record struct CurveCanvasSceneryObject(string ObjectId, float X, float Y, int PlaneIndex, float DepthZ);

public sealed class CurveCanvasMetadata
{
    public string SceneName { get; set; } = string.Empty;
    public string ExportedAtUtc { get; set; } = DateTime.UtcNow.ToString("o");
    public string ToolVersion { get; set; } = "0.1.0";
    public string LevelName { get; set; } = "Untitled Track";
    public string Author { get; set; } = "Anonymous";
    public float ParTimeSeconds { get; set; } = 60f;
}

public sealed class CameraTriggerData
{
    public string ObjectId { get; set; } = string.Empty;
    public CurveCanvasTransform VolumeTransform { get; set; } = CurveCanvasTransform.Identity;
    public string TargetCameraPath { get; set; } = string.Empty;
}

public sealed class CurveCanvasTransform
{
    public CurveCanvasVector3 Position { get; set; } = CurveCanvasVector3.Zero;
    public CurveCanvasVector3 RotationDegrees { get; set; } = CurveCanvasVector3.Zero;
    public CurveCanvasVector3 Scale { get; set; } = CurveCanvasVector3.One;

    public static CurveCanvasTransform Identity => new();
}

public sealed class CurveCanvasVector3
{
    public CurveCanvasVector3()
    {
    }

    public CurveCanvasVector3(float x, float y, float z)
    {
        X = x;
        Y = y;
        Z = z;
    }

    public float X { get; set; }
    public float Y { get; set; }
    public float Z { get; set; }

    public static CurveCanvasVector3 Zero => new(0f, 0f, 0f);
    public static CurveCanvasVector3 One => new(1f, 1f, 1f);
}
