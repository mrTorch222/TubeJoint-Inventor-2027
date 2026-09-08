using Inventor;
using TubeJoint.AddIn.Models;

namespace TubeJoint.AddIn.Services;

internal interface ITubeMemberAnalyzer
{
    bool CanAnalyze(ComponentOccurrence occurrence);
    TubeMemberAnalysis Analyze(ComponentOccurrence occurrence);
}
