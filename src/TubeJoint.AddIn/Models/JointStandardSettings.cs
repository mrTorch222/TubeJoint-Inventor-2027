namespace TubeJoint.AddIn.Models;

internal sealed class JointStandardSettings
{
    public double AutoWidthFactor { get; init; } = 0.50;
    public double AutoWidthMinimumMm { get; init; } = 8.0;
    public double AutoWidthMaximumMm { get; init; } = 40.0;
    public double AutoHeightFactor { get; init; } = 0.55;
    public double DimensionStepMm { get; init; } = 0.5;
    public IReadOnlyList<double> SlotThicknessesMm { get; init; } =
        new[] { 1.0, 1.5, 2.0, 2.5, 3.0 };
}
