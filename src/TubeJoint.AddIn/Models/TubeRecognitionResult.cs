using Inventor;
using Point = Inventor.Point;

namespace TubeJoint.AddIn.Models;

internal enum TubeProfileKind
{
    Rectangular,
    Round
}

internal sealed class TubeRecognitionResult
{
    public required PartDocument Document { get; init; }
    public required SurfaceBody Body { get; init; }
    public required TubeProfileKind ProfileKind { get; init; }
    public required Point Center { get; init; }
    public required Vector LengthAxis { get; init; }
    public required Vector WidthAxis { get; init; }
    public required Vector HeightAxis { get; init; }
    public required double LengthMm { get; init; }
    public required double WidthMm { get; init; }
    public required double HeightMm { get; init; }
    public required double WallThicknessMm { get; init; }

    public string ProfileName => ProfileKind == TubeProfileKind.Round
        ? $"Труба круглая Ø{Format(WidthMm)}×{Format(WallThicknessMm)}"
        : $"Труба профильная {Format(WidthMm)}×{Format(HeightMm)}×{Format(WallThicknessMm)}";

    public string StockNumber => ProfileKind == TubeProfileKind.Round
        ? $"CHS {Format(WidthMm)}x{Format(WallThicknessMm)}"
        : $"RHS {Format(WidthMm)}x{Format(HeightMm)}x{Format(WallThicknessMm)}";

    public string Description => $"{ProfileName}, L={Format(LengthMm)} мм";

    public static string Format(double value) =>
        Math.Abs(value - Math.Round(value)) < 0.01
            ? Math.Round(value).ToString("0")
            : value.ToString("0.##");
}
