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
}

public record struct CurveCanvasSplinePoint(float X, float Y);

public record struct CurveCanvasActionObject(string ObjectId, float CurveOffset);

public record struct CurveCanvasSceneryObject(string ObjectId, float X, float Y, int PlaneIndex, float DepthZ);

public sealed class CurveCanvasMetadata
{
    public string SceneName { get; set; } = string.Empty;
    public string ExportedAtUtc { get; set; } = DateTime.UtcNow.ToString("o");
    public string ToolVersion { get; set; } = "0.1.0";
}
