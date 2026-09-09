using Inventor;
using TubeJoint.AddIn.Models;
using Point = Inventor.Point;

namespace TubeJoint.AddIn.Services;

/// <summary>
/// Recognizes a straight hollow tube from its exact minimum oriented box and
/// analytical side faces. Inventor model units are centimetres.
/// </summary>
internal sealed class TubeRecognitionService
{
    private const double ParallelTolerance = 0.995;
    private const double GeometryToleranceCm = 1e-4;
    private readonly Inventor.Application _application;

    public TubeRecognitionService(Inventor.Application application) => _application = application;

    public TubeRecognitionResult Analyze(PartDocument document)
    {
        if (!document.IsModifiable)
            throw new InvalidOperationException($"Деталь недоступна для изменения: {document.DisplayName}");

        var definition = document.ComponentDefinition;
        var solidBodies = definition.SurfaceBodies.Cast<SurfaceBody>().Where(body => body.IsSolid).ToList();
        if (solidBodies.Count != 1)
            throw new InvalidOperationException(
                $"Распознавание требует ровно одно solid-тело. Найдено: {solidBodies.Count}.");

        var body = solidBodies[0];
        var box = body.OrientedMinimumRangeBox;
        var directions = new[] { box.DirectionOne, box.DirectionTwo, box.DirectionThree }
            .Select(vector => (Vector: vector.Copy(), Length: vector.Length))
            .OrderByDescending(item => item.Length)
            .ToArray();

        if (directions[0].Length < directions[1].Length * 1.25)
            throw new InvalidOperationException(
                "Деталь не похожа на прямую трубу: продольный размер должен быть явно больше сечения.");

        var lengthAxis = Unit(directions[0].Vector);
        var transverse = directions.Skip(1).OrderByDescending(item => item.Length).ToArray();
        var widthAxis = Unit(transverse[0].Vector);
        Canonicalize(lengthAxis);
        Canonicalize(widthAxis);
        var heightAxis = Cross(lengthAxis, widthAxis);
        heightAxis.Normalize();
        widthAxis = Cross(heightAxis, lengthAxis);
        widthAxis.Normalize();

        var center = box.CornerPoint.Copy();
        center.TranslateBy(Scaled(box.DirectionOne, 0.5));
        center.TranslateBy(Scaled(box.DirectionTwo, 0.5));
        center.TranslateBy(Scaled(box.DirectionThree, 0.5));

        var lengthCm = directions[0].Length;
        var widthCm = transverse[0].Length;
        var heightCm = transverse[1].Length;

        TubeProfileKind kind;
        double wallCm;
        if (TryFindRoundTube(body, lengthAxis, widthCm, heightCm,
                out var outerRadius, out var innerRadius))
        {
            kind = TubeProfileKind.Round;
            wallCm = outerRadius - innerRadius;
            widthCm = heightCm = outerRadius * 2.0;
        }
        else
        {
            kind = TubeProfileKind.Rectangular;
            wallCm = MeasureRectangularWall(body, lengthAxis, widthAxis, heightAxis);
            if (wallCm <= GeometryToleranceCm || wallCm >= Math.Min(widthCm, heightCm) * 0.45)
                throw new InvalidOperationException(
                    "Не удалось подтвердить внутренний контур профильной трубы и измерить стенку.");
        }

        return new TubeRecognitionResult
        {
            Document = document,
            Body = body,
            ProfileKind = kind,
            Center = center,
            LengthAxis = lengthAxis,
            WidthAxis = widthAxis,
            HeightAxis = heightAxis,
            LengthMm = lengthCm * 10.0,
            WidthMm = NominalMm(widthCm * 10.0),
            HeightMm = NominalMm(heightCm * 10.0),
            WallThicknessMm = NominalMm(wallCm * 10.0)
        };
    }

    public bool NormalizeAndWriteProperties(TubeRecognitionResult tube)
    {
        WriteProperties(tube);
        if (IsAlreadyNormalized(tube))
        {
            SetCustomProperty(tube.Document, "TubeJoint.Normalized", true);
            return false;
        }

        var definition = tube.Document.ComponentDefinition;
        var bodies = _application.TransientObjects.CreateObjectCollection();
        bodies.Add(tube.Body);
        var move = definition.Features.MoveFeatures.CreateMoveDefinition(bodies);

        // First place the OBB centre at the model origin. All following rotations
        // are about the fixed origin axes, so the centre stays at (0,0,0).
        move.AddFreeDrag(-tube.Center.X, -tube.Center.Y, -tube.Center.Z);

        var rotation = BuildWorldToTubeRotation(tube.WidthAxis, tube.HeightAxis, tube.LengthAxis);
        var (zAngle, yAngle, xAngle) = DecomposeZyx(rotation);
        // R = Rz * Ry * Rx, therefore body operations are applied X, Y, Z.
        if (Math.Abs(xAngle) > 1e-10)
            move.AddRotateAboutAxis(definition.WorkAxes[1], true, xAngle);
        if (Math.Abs(yAngle) > 1e-10)
            move.AddRotateAboutAxis(definition.WorkAxes[2], true, yAngle);
        if (Math.Abs(zAngle) > 1e-10)
            move.AddRotateAboutAxis(definition.WorkAxes[3], true, zAngle);

        var feature = definition.Features.MoveFeatures.Add(move);
        feature.Name = UniqueFeatureName(definition, "TJ_NORMALIZE_TUBE");
        tube.Document.Update2(true);
        SetCustomProperty(tube.Document, "TubeJoint.Normalized", true);
        return true;
    }

    public Matrix CreateNormalizationTransform(TubeRecognitionResult tube)
    {
        var geometry = _application.TransientGeometry;
        var transform = geometry.CreateMatrix();
        transform.SetToAlignCoordinateSystems(
            tube.Center,
            tube.WidthAxis,
            tube.HeightAxis,
            tube.LengthAxis,
            geometry.CreatePoint(0, 0, 0),
            geometry.CreateVector(1, 0, 0),
            geometry.CreateVector(0, 1, 0),
            geometry.CreateVector(0, 0, 1));
        return transform;
    }

    private static double[,] BuildWorldToTubeRotation(Vector x, Vector y, Vector z) => new[,]
    {
        { x.X, x.Y, x.Z },
        { y.X, y.Y, y.Z },
        { z.X, z.Y, z.Z }
    };

    private static (double Z, double Y, double X) DecomposeZyx(double[,] r)
    {
        var y = Math.Asin(Math.Clamp(-r[2, 0], -1.0, 1.0));
        var cosine = Math.Cos(y);
        if (Math.Abs(cosine) > 1e-9)
            return (Math.Atan2(r[1, 0], r[0, 0]), y, Math.Atan2(r[2, 1], r[2, 2]));

        // Gimbal lock: X can be zero; Z then contains the remaining rotation.
        return (Math.Atan2(-r[0, 1], r[1, 1]), y, 0.0);
    }

    private static bool IsAlreadyNormalized(TubeRecognitionResult tube)
    {
        var centerDistance = Math.Sqrt(
            tube.Center.X * tube.Center.X + tube.Center.Y * tube.Center.Y + tube.Center.Z * tube.Center.Z);
        return centerDistance < 1e-5 &&
               tube.WidthAxis.X > 0.999999 &&
               tube.HeightAxis.Y > 0.999999 &&
               tube.LengthAxis.Z > 0.999999;
    }

    private static bool TryFindRoundTube(
        SurfaceBody body,
        Vector lengthAxis,
        double boxWidthCm,
        double boxHeightCm,
        out double outerRadius,
        out double innerRadius)
    {
        outerRadius = 0;
        innerRadius = 0;
        var cylinders = new List<AxialCylinder>();
        foreach (Face face in body.Faces)
        {
            if (face.SurfaceType != SurfaceTypeEnum.kCylinderSurface || face.Geometry is not Cylinder cylinder)
                continue;
            if (Math.Abs(Dot(cylinder.AxisVector.AsVector(), lengthAxis)) < ParallelTolerance)
                continue;

            cylinders.Add(new AxialCylinder(cylinder.BasePoint.Copy(), cylinder.Radius));
        }

        // A real round tube has an outer and an inner cylindrical face on the
        // same axis, and its outer diameter equals both transverse OBB sizes.
        // Rounded RHS/SHS corners also create axial cylinders, but their axes
        // are offset and their small diameters do not span the whole section.
        foreach (var outer in cylinders.OrderByDescending(item => item.Radius))
        {
            var diameter = outer.Radius * 2.0;
            if (!NearlySameSize(diameter, boxWidthCm) || !NearlySameSize(diameter, boxHeightCm))
                continue;

            var inner = cylinders
                .Where(item => item.Radius < outer.Radius - GeometryToleranceCm &&
                               AreCoaxial(outer.AxisPoint, item.AxisPoint, lengthAxis))
                .OrderByDescending(item => item.Radius)
                .FirstOrDefault();
            if (inner is null) continue;
            var wall = outer.Radius - inner.Radius;
            if (inner.Radius <= GeometryToleranceCm || wall >= outer.Radius * 0.8) continue;

            outerRadius = outer.Radius;
            innerRadius = inner.Radius;
            return true;
        }
        return false;
    }

    private static bool NearlySameSize(double left, double right) =>
        Math.Abs(left - right) <= Math.Max(0.01, Math.Max(left, right) * 0.02);

    private static bool AreCoaxial(Point first, Point second, Vector axis)
    {
        var between = first.VectorTo(second);
        var along = Dot(between, axis);
        var perpendicularSquared = Math.Max(0, between.DotProduct(between) - along * along);
        return Math.Sqrt(perpendicularSquared) <= 0.001;
    }

    private static double MeasureRectangularWall(
        SurfaceBody body, Vector lengthAxis, Vector widthAxis, Vector heightAxis)
    {
        var widthPlanes = new List<double>();
        var heightPlanes = new List<double>();
        foreach (Face face in body.Faces)
        {
            if (face.SurfaceType != SurfaceTypeEnum.kPlaneSurface || face.Geometry is not Plane plane)
                continue;
            var normal = plane.Normal.AsVector();
            normal.Normalize();
            if (Math.Abs(Dot(normal, lengthAxis)) > 0.05) continue;

            if (Math.Abs(Dot(normal, widthAxis)) > ParallelTolerance)
                AddUnique(widthPlanes, Dot(plane.RootPoint, widthAxis));
            else if (Math.Abs(Dot(normal, heightAxis)) > ParallelTolerance)
                AddUnique(heightPlanes, Dot(plane.RootPoint, heightAxis));
        }

        var candidates = new List<double>();
        AddWallCandidates(widthPlanes, candidates);
        AddWallCandidates(heightPlanes, candidates);
        if (candidates.Count == 0) return 0;
        candidates.Sort();
        return candidates[candidates.Count / 2];
    }

    private static void AddWallCandidates(List<double> planes, List<double> target)
    {
        planes.Sort();
        if (planes.Count < 4) return;
        var first = planes[1] - planes[0];
        var last = planes[^1] - planes[^2];
        if (first > GeometryToleranceCm) target.Add(first);
        if (last > GeometryToleranceCm) target.Add(last);
    }

    private static void AddUnique(List<double> values, double value)
    {
        if (!values.Any(existing => Math.Abs(existing - value) < GeometryToleranceCm)) values.Add(value);
    }

    private static void WriteProperties(TubeRecognitionResult tube)
    {
        var design = tube.Document.PropertySets["{32853F0F-3444-11D1-9E93-0060B03C1CA6}"];
        design.ItemByPropId[55].Value = tube.StockNumber;
        design.ItemByPropId[29].Value = tube.Description;
        var partNumber = Convert.ToString(design.ItemByPropId[5].Value);
        if (string.IsNullOrWhiteSpace(partNumber)) design.ItemByPropId[5].Value = tube.StockNumber;

        SetCustomProperty(tube.Document, "TubeJoint.ProfileType",
            tube.ProfileKind == TubeProfileKind.Round ? "Round" : "Rectangular");
        SetCustomProperty(tube.Document, "TubeJoint.Profile", tube.ProfileName);
        SetCustomProperty(tube.Document, "TubeJoint.LengthMm", tube.LengthMm);
        SetCustomProperty(tube.Document, "TubeJoint.WidthMm", tube.WidthMm);
        SetCustomProperty(tube.Document, "TubeJoint.HeightMm", tube.HeightMm);
        SetCustomProperty(tube.Document, "TubeJoint.WallThicknessMm", tube.WallThicknessMm);
        SetCustomProperty(tube.Document, "TubeJoint.RecognitionVersion", 1);
        if (string.IsNullOrWhiteSpace(GetCustomProperty(tube.Document, "TubeJoint.OriginalFileName")))
        {
            var originalName = string.IsNullOrWhiteSpace(tube.Document.FullFileName)
                ? tube.Document.DisplayName
                : System.IO.Path.GetFileNameWithoutExtension(tube.Document.FullFileName);
            SetCustomProperty(tube.Document, "TubeJoint.OriginalFileName", originalName);
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

    private static double NominalMm(double value) =>
        Math.Round(value, 1, MidpointRounding.AwayFromZero);

    private static string UniqueFeatureName(PartComponentDefinition definition, string baseName)
    {
        var names = definition.Features.Cast<PartFeature>()
            .Select(feature => feature.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (!names.Contains(baseName)) return baseName;
        var index = 2;
        while (names.Contains($"{baseName}_{index}")) index++;
        return $"{baseName}_{index}";
    }

    private static Vector Unit(Vector source)
    {
        var result = source.Copy();
        result.Normalize();
        return result;
    }

    private static void Canonicalize(Vector vector)
    {
        var components = new[] { vector.X, vector.Y, vector.Z };
        var dominant = components.OrderByDescending(Math.Abs).First();
        if (dominant < 0) vector.ScaleBy(-1);
    }

    private static Vector Cross(Vector left, Vector right) => left.CrossProduct(right);
    private static double Dot(Vector left, Vector right) => left.DotProduct(right);
    private static double Dot(Point point, Vector vector) =>
        point.X * vector.X + point.Y * vector.Y + point.Z * vector.Z;
    private static Vector Scaled(Vector source, double scale)
    {
        var result = source.Copy();
        result.ScaleBy(scale);
        return result;
    }

    private sealed record AxialCylinder(Point AxisPoint, double Radius);
}
