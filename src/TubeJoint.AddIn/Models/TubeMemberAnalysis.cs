using Inventor;
using Point = Inventor.Point;

namespace TubeJoint.AddIn.Models;

internal enum TubeMemberSource
{
    FrameGenerator,
    GenericSolid
}

internal sealed class TubeMemberAnalysis
{
    public required TubeMemberSource Source { get; init; }
    public required Vector AxisAssembly { get; init; }
    public required Point CenterAssembly { get; init; }
    public required string AxisMethod { get; init; }
    public int SolidBodyCount { get; init; }
    public int LinearEdgeCount { get; init; }
    public double AxisConfidence { get; init; }
}
