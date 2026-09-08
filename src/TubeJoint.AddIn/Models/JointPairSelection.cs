using Inventor;
using Point = Inventor.Point;

namespace TubeJoint.AddIn.Models;

internal sealed class JointPairSelection
{
    public TubeMemberSource MaleTubeSource { get; init; } = TubeMemberSource.GenericSolid;
    public string MaleAxisMethod { get; init; } = string.Empty;
    public double MaleAxisConfidence { get; init; }
    public JointWallPair WallPair { get; init; } = JointWallPair.AB;
    public JointPairSelection? RotatedPair { get; set; }
    public required JointFaceSelection Male { get; init; }
    public required JointFaceSelection MaleOpposite { get; init; }
    public required JointFaceSelection Female { get; init; }
    public required Point JointPointAssembly { get; init; }
    public required Point SideAAnchorAssembly { get; init; }
    public required Point SideBAnchorAssembly { get; init; }
    public required Point SideASlotCenterAssembly { get; init; }
    public required Point SideBSlotCenterAssembly { get; init; }
    public required Vector MaleAxisTowardFemale { get; init; }
    public required Vector TenonDirectionAssembly { get; init; }
    public required Vector MaleProfileYAxis { get; init; }
    public required Vector MaleSideNormal { get; init; }
    public double MaleWallThicknessMm { get; init; }
    public double FemaleWallThicknessMm { get; init; }
    public double MaleProfileSpanMm { get; init; }
    public double MaleCrossSpanMm { get; init; }
    public double InsertionDeviationDegrees { get; init; }
    public double SideAGapMm { get; init; }
    public double SideBGapMm { get; init; }
}
