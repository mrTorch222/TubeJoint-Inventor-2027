using Inventor;
using TubeJoint.AddIn.Models;

namespace TubeJoint.AddIn.Services;

internal sealed class TubeAnalyzerSelector
{
    private readonly IReadOnlyList<ITubeMemberAnalyzer> _analyzers;

    public TubeAnalyzerSelector(Inventor.Application application)
    {
        _analyzers =
        [
            new FrameGeneratorTubeAnalyzer(application),
            new GenericTubeAnalyzer(application)
        ];
    }

    public TubeMemberAnalysis Analyze(ComponentOccurrence occurrence)
    {
        var analyzer = _analyzers.FirstOrDefault(candidate => candidate.CanAnalyze(occurrence));
        if (analyzer is null)
            throw new InvalidOperationException($"Для детали '{occurrence.Name}' не найден анализатор трубы.");
        return analyzer.Analyze(occurrence);
    }
}
