using Inventor;

namespace TubeJoint.AddIn.TubeAnalysis;

/// <summary>
/// Read-only boundary shared by commands that need tube geometry. Implementations
/// must not edit the document, write iProperties, create features, or show UI.
/// </summary>
internal interface ITubeAnalyzer
{
    TubeAnalysisResult Analyze(PartDocument document);
}
