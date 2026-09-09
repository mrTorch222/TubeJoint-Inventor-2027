using Inventor;
using TubeJoint.AddIn.TubeAnalysis;

namespace TubeJoint.AddIn.Services;

/// <summary>
/// Applies preparation side effects to an already analyzed tube. Geometry
/// recognition stays exclusively behind ITubeAnalyzer.
/// </summary>
internal sealed class TubePreparationService
{
    private readonly Inventor.Application _application;

    public TubePreparationService(Inventor.Application application) => _application = application;

    public bool NormalizeAndWriteProperties(PartDocument document, TubeAnalysisResult tube)
    {
        if (!document.IsModifiable)
            throw new InvalidOperationException($"Деталь недоступна для изменения: {document.DisplayName}");

        WriteProperties(document, tube);
        if (IsAlreadyNormalized(tube))
        {
            SetCustomProperty(document, "TubeJoint.Normalized", true);
            return false;
        }

        var definition = document.ComponentDefinition;
        var bodies = _application.TransientObjects.CreateObjectCollection();
        bodies.Add(GetSingleSolidBody(definition));
        var move = definition.Features.MoveFeatures.CreateMoveDefinition(bodies);
        move.AddFreeDrag(-tube.CenterCm.X, -tube.CenterCm.Y, -tube.CenterCm.Z);

        var rotation = BuildWorldToTubeRotation(
            tube.WidthAxis, tube.HeightAxis, tube.LengthAxis);
        var (zAngle, yAngle, xAngle) = DecomposeZyx(rotation);
        if (Math.Abs(xAngle) > 1e-10)
            move.AddRotateAboutAxis(definition.WorkAxes[1], true, xAngle);
        if (Math.Abs(yAngle) > 1e-10)
            move.AddRotateAboutAxis(definition.WorkAxes[2], true, yAngle);
        if (Math.Abs(zAngle) > 1e-10)
            move.AddRotateAboutAxis(definition.WorkAxes[3], true, zAngle);

        var feature = definition.Features.MoveFeatures.Add(move);
        feature.Name = UniqueFeatureName(definition, "TJ_NORMALIZE_TUBE");
        document.Update2(true);
        SetCustomProperty(document, "TubeJoint.Normalized", true);
        return true;
    }

    public Matrix CreateNormalizationTransform(TubeAnalysisResult tube)
    {
        var geometry = _application.TransientGeometry;
        var transform = geometry.CreateMatrix();
        transform.SetToAlignCoordinateSystems(
            ToPoint(tube.CenterCm),
            ToVector(tube.WidthAxis),
            ToVector(tube.HeightAxis),
            ToVector(tube.LengthAxis),
            geometry.CreatePoint(0, 0, 0),
            geometry.CreateVector(1, 0, 0),
            geometry.CreateVector(0, 1, 0),
            geometry.CreateVector(0, 0, 1));
        return transform;
    }

    public static string ProfileName(TubeAnalysisResult tube) =>
        tube.SectionKind == TubeSectionKind.Round
            ? $"Труба круглая Ø{Format(tube.NominalWidthMm)}×{Format(tube.NominalWallThicknessMm)}"
            : $"Труба профильная {Format(tube.NominalWidthMm)}×" +
              $"{Format(tube.NominalHeightMm)}×{Format(tube.NominalWallThicknessMm)}";

    public static string StockNumber(TubeAnalysisResult tube) =>
        tube.SectionKind == TubeSectionKind.Round
            ? $"CHS {Format(tube.NominalWidthMm)}x{Format(tube.NominalWallThicknessMm)}"
            : $"RHS {Format(tube.NominalWidthMm)}x{Format(tube.NominalHeightMm)}x" +
              $"{Format(tube.NominalWallThicknessMm)}";

    public static string Format(double value) =>
        Math.Abs(value - Math.Round(value)) < 0.01
            ? Math.Round(value).ToString("0")
            : value.ToString("0.##");

    private static SurfaceBody GetSingleSolidBody(PartComponentDefinition definition)
    {
        var result = definition.SurfaceBodies.Cast<SurfaceBody>().Where(body => body.IsSolid).ToList();
        if (result.Count != 1)
            throw new InvalidOperationException(
                $"Ожидалось одно solid-тело перед нормализацией, найдено: {result.Count}.");
        return result[0];
    }

    private static double[,] BuildWorldToTubeRotation(
        TubeCoordinate x, TubeCoordinate y, TubeCoordinate z) => new[,]
    {
        { x.X, x.Y, x.Z },
        { y.X, y.Y, y.Z },
        { z.X, z.Y, z.Z }
    };

    private static (double Z, double Y, double X) DecomposeZyx(double[,] rotation)
    {
        var y = Math.Asin(Math.Clamp(-rotation[2, 0], -1.0, 1.0));
        var cosine = Math.Cos(y);
        if (Math.Abs(cosine) > 1e-9)
            return (Math.Atan2(rotation[1, 0], rotation[0, 0]), y,
                Math.Atan2(rotation[2, 1], rotation[2, 2]));
        return (Math.Atan2(-rotation[0, 1], rotation[1, 1]), y, 0.0);
    }

    private static bool IsAlreadyNormalized(TubeAnalysisResult tube) =>
        tube.CenterCm.Length < 1e-5 &&
        tube.WidthAxis.X > 0.999999 &&
        tube.HeightAxis.Y > 0.999999 &&
        tube.LengthAxis.Z > 0.999999;

    private static void WriteProperties(PartDocument document, TubeAnalysisResult tube)
    {
        var profile = ProfileName(tube);
        var stockNumber = StockNumber(tube);
        var description = $"{profile}, L={Format(tube.ExactLengthMm)} мм";
        var design = document.PropertySets["{32853F0F-3444-11D1-9E93-0060B03C1CA6}"];
        design.ItemByPropId[55].Value = stockNumber;
        design.ItemByPropId[29].Value = description;
        if (string.IsNullOrWhiteSpace(Convert.ToString(design.ItemByPropId[5].Value)))
            design.ItemByPropId[5].Value = stockNumber;

        SetCustomProperty(document, "TubeJoint.ProfileType", tube.SectionKind.ToString());
        SetCustomProperty(document, "TubeJoint.Profile", profile);
        SetCustomProperty(document, "TubeJoint.LengthMm", tube.ExactLengthMm);
        SetCustomProperty(document, "TubeJoint.WidthMm", tube.NominalWidthMm);
        SetCustomProperty(document, "TubeJoint.HeightMm", tube.NominalHeightMm);
        SetCustomProperty(document, "TubeJoint.WallThicknessMm", tube.NominalWallThicknessMm);
        SetCustomProperty(document, "TubeJoint.RecognitionVersion", TubeAnalysisResult.SchemaVersion);
        if (string.IsNullOrWhiteSpace(GetCustomProperty(document, "TubeJoint.OriginalFileName")))
        {
            var originalName = string.IsNullOrWhiteSpace(document.FullFileName)
                ? document.DisplayName
                : System.IO.Path.GetFileNameWithoutExtension(document.FullFileName);
            SetCustomProperty(document, "TubeJoint.OriginalFileName", originalName);
        }
    }

    private static void SetCustomProperty(PartDocument document, string name, object value)
    {
        var custom = document.PropertySets["{D5CDD505-2E9C-101B-9397-08002B2CF9AE}"];
        try { custom[name].Value = value; }
        catch { custom.Add(value, name); }
    }

    private static string GetCustomProperty(PartDocument document, string name)
    {
        var custom = document.PropertySets["{D5CDD505-2E9C-101B-9397-08002B2CF9AE}"];
        try { return Convert.ToString(custom[name].Value)?.Trim() ?? string.Empty; }
        catch { return string.Empty; }
    }

    private static string UniqueFeatureName(PartComponentDefinition definition, string baseName)
    {
        var names = definition.Features.Cast<PartFeature>()
            .Select(feature => feature.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (!names.Contains(baseName)) return baseName;
        var index = 2;
        while (names.Contains($"{baseName}_{index}")) index++;
        return $"{baseName}_{index}";
    }

    private Inventor.Point ToPoint(TubeCoordinate value) =>
        _application.TransientGeometry.CreatePoint(value.X, value.Y, value.Z);

    private Vector ToVector(TubeCoordinate value) =>
        _application.TransientGeometry.CreateVector(value.X, value.Y, value.Z);
}
