namespace TubeJoint.AddIn.TubeAnalysis;

internal enum TubeAnalysisFailure
{
    NoSolidBody,
    MultipleSolidBodies,
    AmbiguousAxis,
    UnsupportedSection,
    SolidBar
}

internal sealed class TubeAnalysisException : InvalidOperationException
{
    public TubeAnalysisException(TubeAnalysisFailure failure, string message)
        : base(message) => Failure = failure;

    public TubeAnalysisFailure Failure { get; }
}
