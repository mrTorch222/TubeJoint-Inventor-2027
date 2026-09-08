namespace TubeJoint.AddIn.Models;

internal sealed class JointPairRecord
{
    public string Id { get; init; } = Guid.NewGuid().ToString("N");
    public string MaleOccurrenceName { get; init; } = string.Empty;
    public string MaleDocumentPath { get; init; } = string.Empty;
    public string FemaleOccurrenceName { get; init; } = string.Empty;
    public string FemaleDocumentPath { get; init; } = string.Empty;
    public string TemplatePath { get; init; } = string.Empty;
    public int TemplateVersion { get; init; } = 6;
    public TubeJointParameters Parameters { get; init; } = new();
    public string GeometryStatus { get; init; } = "Created";
    public double JointPointX { get; init; }
    public double JointPointY { get; init; }
    public double JointPointZ { get; init; }
    public double MaleAxisX { get; init; }
    public double MaleAxisY { get; init; }
    public double MaleAxisZ { get; init; }
    public double TenonDirectionX { get; init; }
    public double TenonDirectionY { get; init; }
    public double TenonDirectionZ { get; init; }
    public double ProfileYAxisX { get; init; }
    public double ProfileYAxisY { get; init; }
    public double ProfileYAxisZ { get; init; }
    public double InsertionDeviationDegrees { get; init; }
    public double SideAGapMm { get; init; }
    public double SideBGapMm { get; init; }
}
