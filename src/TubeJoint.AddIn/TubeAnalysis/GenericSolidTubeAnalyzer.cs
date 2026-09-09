using Inventor;
using Point = Inventor.Point;

namespace TubeJoint.AddIn.TubeAnalysis;

/// <summary>
/// Analyzes one straight hollow solid from analytical B-Rep geometry. The body is
/// never modified. OrientedMinimumRangeBox is evaluated once and all faces are
/// classified in a single pass.
/// </summary>
internal sealed class GenericSolidTubeAnalyzer : ITubeAnalyzer
{
    private const double ParallelTolerance = 0.995;
    private const double GeometryToleranceCm = 1e-4;
    private const double CoaxialToleranceCm = 1e-3;
    private const double MinimumLengthRatio = 1.25;

    public TubeAnalysisResult Analyze(PartDocument document)
    {
        var definition = document.ComponentDefinition;
        var solidBodies = new List<SurfaceBody>();
        foreach (SurfaceBody candidateBody in definition.SurfaceBodies)
            if (candidateBody.IsSolid) solidBodies.Add(candidateBody);

        if (solidBodies.Count == 0)
            throw new TubeAnalysisException(TubeAnalysisFailure.NoSolidBody,
                $"В детали '{document.DisplayName}' нет замкнутого solid-тела.");
        if (solidBodies.Count > 1)
            throw new TubeAnalysisException(TubeAnalysisFailure.MultipleSolidBodies,
                $"В детали '{document.DisplayName}' найдено несколько solid-тел ({solidBodies.Count}).");

        var body = solidBodies[0];
        var box = body.OrientedMinimumRangeBox;
        var directions = new[] { box.DirectionOne, box.DirectionTwo, box.DirectionThree }
            .Select(vector => new BoxDirection(ToCoordinate(vector), vector.Length))
            .OrderByDescending(item => item.LengthCm)
            .ToArray();
        if (directions[0].LengthCm < directions[1].LengthCm * MinimumLengthRatio)
            throw new TubeAnalysisException(TubeAnalysisFailure.AmbiguousAxis,
                $"У детали '{document.DisplayName}' нет однозначного продольного размера.");

        var lengthAxis = CanonicalUnit(directions[0].Direction);
        var transverse = directions.Skip(1).OrderByDescending(item => item.LengthCm).ToArray();
        var widthAxis = CanonicalUnit(transverse[0].Direction);
        var heightAxis = Unit(Cross(lengthAxis, widthAxis));
        widthAxis = Unit(Cross(heightAxis, lengthAxis));
        var center = Add(ToCoordinate(box.CornerPoint),
            Scale(ToCoordinate(box.DirectionOne), 0.5),
            Scale(ToCoordinate(box.DirectionTwo), 0.5),
            Scale(ToCoordinate(box.DirectionThree), 0.5));

        var scan = ScanFaces(body, lengthAxis, widthAxis, heightAxis);
        var widthCm = transverse[0].LengthCm;
        var heightCm = transverse[1].LengthCm;
        TubeSectionKind kind;
        double wallCm;
        if (TryFindRoundTube(scan.Cylinders, lengthAxis, widthCm, heightCm,
                out var outerRadius, out var innerRadius))
        {
            kind = TubeSectionKind.Round;
            widthCm = heightCm = outerRadius * 2.0;
            wallCm = outerRadius - innerRadius;
        }
        else
        {
            kind = TubeSectionKind.Rectangular;
            wallCm = MeasureRectangularWall(scan.WidthPlanes, scan.HeightPlanes);
            if (wallCm <= GeometryToleranceCm)
                throw new TubeAnalysisException(TubeAnalysisFailure.UnsupportedSection,
                    $"Не удалось подтвердить внутренний контур трубы '{document.DisplayName}'.");
            if (wallCm >= Math.Min(widthCm, heightCm) * 0.45)
                throw new TubeAnalysisException(TubeAnalysisFailure.SolidBar,
                    $"Деталь '{document.DisplayName}' похожа на сплошной пруток.");
        }

        var exactWidthMm = widthCm * 10.0;
        var exactHeightMm = heightCm * 10.0;
        var exactWallMm = wallCm * 10.0;
        return new TubeAnalysisResult
        {
            SectionKind = kind,
            AxisMethod = TubeAxisMethod.OrientedMinimumRangeBox,
            CenterCm = center,
            LengthAxis = lengthAxis,
            WidthAxis = widthAxis,
            HeightAxis = heightAxis,
            ExactLengthMm = directions[0].LengthCm * 10.0,
            ExactWidthMm = exactWidthMm,
            ExactHeightMm = exactHeightMm,
            ExactWallThicknessMm = exactWallMm,
            NominalWidthMm = NominalMm(exactWidthMm),
            NominalHeightMm = NominalMm(exactHeightMm),
            NominalWallThicknessMm = NominalMm(exactWallMm),
            AxisConfidence = directions[0].LengthCm / Math.Max(directions[1].LengthCm, GeometryToleranceCm),
            Diagnostics = new TubeAnalysisDiagnostics
            {
                GeometryVersion = definition.ModelGeometryVersion,
                SolidBodyCount = solidBodies.Count,
                FaceCount = body.Faces.Count,
                AxialPlaneStationCount = scan.WidthPlanes.Count + scan.HeightPlanes.Count,
                AxialCylinderCount = scan.Cylinders.Count
            }
        };
    }

    private static FaceScan ScanFaces(
        SurfaceBody body,
        TubeCoordinate lengthAxis,
        TubeCoordinate widthAxis,
        TubeCoordinate heightAxis)
    {
        var result = new FaceScan();
        foreach (Face face in body.Faces)
        {
            if (face.SurfaceType == SurfaceTypeEnum.kCylinderSurface &&
                face.Geometry is Cylinder cylinder)
            {
                var axis = ToCoordinate(cylinder.AxisVector);
                if (Math.Abs(Dot(axis, lengthAxis)) >= ParallelTolerance)
                    result.Cylinders.Add(new AxialCylinder(ToCoordinate(cylinder.BasePoint), cylinder.Radius));
                continue;
            }

            if (face.SurfaceType != SurfaceTypeEnum.kPlaneSurface || face.Geometry is not Plane plane)
                continue;
            var normal = Unit(ToCoordinate(plane.Normal));
            if (Math.Abs(Dot(normal, lengthAxis)) > 0.05) continue;
            if (Math.Abs(Dot(normal, widthAxis)) >= ParallelTolerance)
                AddUnique(result.WidthPlanes, Dot(ToCoordinate(plane.RootPoint), widthAxis));
            else if (Math.Abs(Dot(normal, heightAxis)) >= ParallelTolerance)
                AddUnique(result.HeightPlanes, Dot(ToCoordinate(plane.RootPoint), heightAxis));
        }
        return result;
    }

    private static bool TryFindRoundTube(
        IReadOnlyList<AxialCylinder> cylinders,
        TubeCoordinate lengthAxis,
        double boxWidthCm,
        double boxHeightCm,
        out double outerRadius,
        out double innerRadius)
    {
        outerRadius = 0;
        innerRadius = 0;
        foreach (var outer in cylinders.OrderByDescending(item => item.RadiusCm))
        {
            var diameter = outer.RadiusCm * 2.0;
            if (!NearlySameSize(diameter, boxWidthCm) || !NearlySameSize(diameter, boxHeightCm))
                continue;
            var inner = cylinders
                .Where(item => item.RadiusCm < outer.RadiusCm - GeometryToleranceCm &&
                               AreCoaxial(outer.AxisPointCm, item.AxisPointCm, lengthAxis))
                .OrderByDescending(item => item.RadiusCm)
                .FirstOrDefault();
            if (inner is null) continue;
            var wall = outer.RadiusCm - inner.RadiusCm;
            if (inner.RadiusCm <= GeometryToleranceCm || wall >= outer.RadiusCm * 0.8) continue;
            outerRadius = outer.RadiusCm;
            innerRadius = inner.RadiusCm;
            return true;
        }
        return false;
    }

    private static double MeasureRectangularWall(List<double> widthPlanes, List<double> heightPlanes)
    {
        if (widthPlanes.Count < 4 || heightPlanes.Count < 4) return 0;
        var candidates = new List<double>(4);
        AddWallCandidates(widthPlanes, candidates);
        AddWallCandidates(heightPlanes, candidates);
        if (candidates.Count == 0) return 0;
        candidates.Sort();
        var middle = candidates.Count / 2;
        return candidates.Count % 2 == 0
            ? (candidates[middle - 1] + candidates[middle]) / 2.0
            : candidates[middle];
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

    private static bool NearlySameSize(double left, double right) =>
        Math.Abs(left - right) <= Math.Max(0.01, Math.Max(left, right) * 0.02);

    private static bool AreCoaxial(
        TubeCoordinate first, TubeCoordinate second, TubeCoordinate unitAxis)
    {
        var between = Subtract(second, first);
        var along = Dot(between, unitAxis);
        var perpendicularSquared = Math.Max(0, Dot(between, between) - along * along);
        return Math.Sqrt(perpendicularSquared) <= CoaxialToleranceCm;
    }

    internal static double NominalMm(double value) =>
        Math.Round(value, 1, MidpointRounding.AwayFromZero);

    private static TubeCoordinate CanonicalUnit(TubeCoordinate value)
    {
        var result = Unit(value);
        var dominant = new[] { result.X, result.Y, result.Z }.OrderByDescending(Math.Abs).First();
        return dominant < 0 ? Scale(result, -1) : result;
    }

    private static TubeCoordinate Unit(TubeCoordinate value)
    {
        var length = value.Length;
        if (length <= 1e-12)
            throw new TubeAnalysisException(TubeAnalysisFailure.AmbiguousAxis,
                "Получен нулевой вектор ориентации трубы.");
        return Scale(value, 1.0 / length);
    }

    private static TubeCoordinate Cross(TubeCoordinate a, TubeCoordinate b) => new(
        a.Y * b.Z - a.Z * b.Y,
        a.Z * b.X - a.X * b.Z,
        a.X * b.Y - a.Y * b.X);

    private static double Dot(TubeCoordinate a, TubeCoordinate b) =>
        a.X * b.X + a.Y * b.Y + a.Z * b.Z;

    private static TubeCoordinate Scale(TubeCoordinate value, double scale) =>
        new(value.X * scale, value.Y * scale, value.Z * scale);

    private static TubeCoordinate Subtract(TubeCoordinate left, TubeCoordinate right) =>
        new(left.X - right.X, left.Y - right.Y, left.Z - right.Z);

    private static TubeCoordinate Add(TubeCoordinate value, params TubeCoordinate[] additions)
    {
        foreach (var addition in additions)
            value = new TubeCoordinate(value.X + addition.X, value.Y + addition.Y, value.Z + addition.Z);
        return value;
    }

    private static TubeCoordinate ToCoordinate(Vector vector) => new(vector.X, vector.Y, vector.Z);
    private static TubeCoordinate ToCoordinate(UnitVector vector) => new(vector.X, vector.Y, vector.Z);
    private static TubeCoordinate ToCoordinate(Point point) => new(point.X, point.Y, point.Z);

    private sealed record BoxDirection(TubeCoordinate Direction, double LengthCm);
    private sealed record AxialCylinder(TubeCoordinate AxisPointCm, double RadiusCm);
    private sealed class FaceScan
    {
        public List<double> WidthPlanes { get; } = new();
        public List<double> HeightPlanes { get; } = new();
        public List<AxialCylinder> Cylinders { get; } = new();
    }
}
