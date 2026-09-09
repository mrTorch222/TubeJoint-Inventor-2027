namespace TubeJoint.AddIn.TubeAnalysis;

internal enum TubeSectionKind
{
    Rectangular,
    Round
}

internal enum TubeAxisMethod
{
    OrientedMinimumRangeBox
}

/// <summary>A COM-free point/vector value in Inventor model units (centimetres).</summary>
internal readonly record struct TubeCoordinate(double X, double Y, double Z)
{
    public double Length => Math.Sqrt(X * X + Y * Y + Z * Z);
}

/// <summary>
/// Immutable, COM-free geometry snapshot. Exact dimensions describe the B-Rep;
/// nominal dimensions are rounded only for labels, iProperties, and file names.
/// </summary>
internal sealed record TubeAnalysisResult
{
    public const int SchemaVersion = 1;

    public required TubeSectionKind SectionKind { get; init; }
    public required TubeAxisMethod AxisMethod { get; init; }
    public required TubeCoordinate CenterCm { get; init; }
    public required TubeCoordinate LengthAxis { get; init; }
    public required TubeCoordinate WidthAxis { get; init; }
    public required TubeCoordinate HeightAxis { get; init; }
    public required double ExactLengthMm { get; init; }
    public required double ExactWidthMm { get; init; }
    public required double ExactHeightMm { get; init; }
    public required double ExactWallThicknessMm { get; init; }
    public required double NominalWidthMm { get; init; }
    public required double NominalHeightMm { get; init; }
    public required double NominalWallThicknessMm { get; init; }
    public required double AxisConfidence { get; init; }
    public required TubeAnalysisDiagnostics Diagnostics { get; init; }
}

internal sealed record TubeAnalysisDiagnostics
{
    public required string GeometryVersion { get; init; }
    public required int SolidBodyCount { get; init; }
    public required int FaceCount { get; init; }
    public required int AxialPlaneStationCount { get; init; }
    public required int AxialCylinderCount { get; init; }
}
