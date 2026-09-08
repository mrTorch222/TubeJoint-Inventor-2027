using Inventor;
using TubeJoint.AddIn.Models;
using Point = Inventor.Point;

namespace TubeJoint.AddIn.Services;

internal sealed class OccurrenceSelectionService
{
    private const double Tolerance = 1e-7;
    private readonly Inventor.Application _application;
    private readonly TubeAnalyzerSelector _tubeAnalyzers;

    public OccurrenceSelectionService(Inventor.Application application)
    {
        _application = application;
        _tubeAnalyzers = new TubeAnalyzerSelector(application);
    }

    public JointPairSelection PickPair()
    {
        var maleOccurrence = PickPartOccurrence("Выберите целиком трубу, на которой будет ШИП");
        var female = PickPlanarFace("Выберите плоскую грань второй трубы, в которой будет ПАЗ");

        if (string.Equals(maleOccurrence.Name, female.Occurrence.Name, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Нужно выбрать два разных вхождения деталей.");

        var maleDocument = GetPartDocument(maleOccurrence);
        var femaleDocument = GetPartDocument(female.Occurrence);
        if (string.Equals(maleDocument.FullFileName, femaleDocument.FullFileName, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                "Оба вхождения ссылаются на один IPT-файл. Перед созданием соединения " +
                "сделайте одно вхождение независимым.");
        }

        EnsureWritable(maleDocument, "деталь с шипом");
        EnsureWritable(femaleDocument, "деталь с пазом");

        var maleTube = _tubeAnalyzers.Analyze(maleOccurrence);
        var axis = maleTube.AxisAssembly.Copy();
        var femalePlane = RequirePlane(female.ProxyFace);
        var femaleNormal = femalePlane.Normal.AsVector();
        var center = maleTube.CenterAssembly;
        var denominator = Dot(axis, femaleNormal);
        if (Math.Abs(denominator) < 0.05)
            throw new InvalidOperationException("Ось трубы почти параллельна выбранной грани паза.");

        var distance = Dot(center.VectorTo(femalePlane.RootPoint), femaleNormal) / denominator;
        if (distance < 0)
        {
            axis = Negated(axis);
            distance = -distance;
        }

        // Keep the analytical axis/plane intersection. GetClosestPointTo clamps
        // to a trimmed face boundary and was the source of slot/tenon offsets
        // when the selected face came from Frame Generator Notch or Trim.
        var jointPoint = PointAlong(center, axis, distance);
        JointPairSelection primary;
        FaceProxy maleSide;
        try
        {
            maleSide = FindMaleSideFace(maleOccurrence, axis, jointPoint);
            primary = BuildPair(
                maleOccurrence, female, axis, femalePlane, femaleNormal,
                jointPoint, maleSide, JointWallPair.AB, maleTube);
        }
        catch (Exception exception) when (
            maleTube.Source == TubeMemberSource.GenericSolid &&
            exception is not OperationCanceledException)
        {
            var confidence = double.IsPositiveInfinity(maleTube.AxisConfidence)
                ? "однозначно"
                : maleTube.AxisConfidence.ToString("0.##");
            throw new InvalidOperationException(
                $"Обычная solid-деталь распознана, но её не удалось разобрать как прямую полую трубу. " +
                $"Ось: {maleTube.AxisMethod}, уверенность: {confidence}. {exception.Message}",
                exception);
        }

        // Discover the second pair now, while topology references are live. A real
        // 90-degree switch must replace the selected wall faces and all derived axes;
        // rotating only client graphics would create geometry on the wrong walls.
        try
        {
            var rotatedSide = FindOrthogonalMaleSideFace(maleOccurrence, axis, maleSide, center);
            var rotated = BuildPair(
                maleOccurrence, female, axis, femalePlane, femaleNormal,
                jointPoint, rotatedSide, JointWallPair.CD, maleTube);
            primary.RotatedPair = rotated;
            rotated.RotatedPair = primary;
        }
        catch
        {
            // Non-rectangular members can still use their valid first opposite pair.
            primary.RotatedPair = null;
        }

        return primary;
    }

    private JointPairSelection BuildPair(
        ComponentOccurrence maleOccurrence,
        JointFaceSelection female,
        Vector axis,
        Plane femalePlane,
        Vector femaleNormal,
        Point analyticalJointPoint,
        FaceProxy maleSide,
        JointWallPair wallPair,
        TubeMemberAnalysis maleTube)
    {
        var oppositeMaleSide = FindOppositeMaleSideFace(maleOccurrence, axis, maleSide);
        var sideNormal = Unit(RequirePlane(maleSide).Normal.AsVector());
        var tenonDirection = BuildTenonDirection(femaleNormal, sideNormal, axis);
        var profileY = Cross(sideNormal, tenonDirection);
        profileY.Normalize();
        var acrossOnFemale = ProjectToPlane(sideNormal, femaleNormal);
        if (acrossOnFemale.Length < 0.05)
            throw new InvalidOperationException(
                "Выбранная грань не позволяет совместить две стенки трубы с пазами.");
        acrossOnFemale.Normalize();
        // Tenon roots and slot centers are not the same points on an angled joint.
        // Roots are obtained by moving transversely from one analytical tube-axis
        // station to the two outer walls. Since sideNormal is perpendicular to the
        // tube axis, A and B are guaranteed to have the exact same root station.
        // Slot anchors are calculated separately on the selected female plane.
        var sideAAnchor = MovePointToPlane(
            analyticalJointPoint, sideNormal, RequirePlane(maleSide));
        var sideBAnchor = MovePointToPlane(
            analyticalJointPoint, sideNormal, RequirePlane(oppositeMaleSide));
        var sideASlotSurface = MovePointToPlane(
            analyticalJointPoint, acrossOnFemale, RequirePlane(maleSide));
        var sideBSlotSurface = MovePointToPlane(
            analyticalJointPoint, acrossOnFemale, RequirePlane(oppositeMaleSide));
        var acrossAtoB = sideAAnchor.VectorTo(sideBAnchor);
        if (acrossAtoB.Length < Tolerance)
            throw new InvalidOperationException("Не удалось определить расстояние между стенками A и B.");
        acrossAtoB.Normalize();
        var slotAcrossAtoB = sideASlotSurface.VectorTo(sideBSlotSurface);
        if (slotAcrossAtoB.Length < Tolerance)
            throw new InvalidOperationException("Не удалось определить расстояние между пазами A и B.");
        slotAcrossAtoB.Normalize();
        var maleWall = MeasureWallThicknessMm(maleOccurrence, maleSide);
        var femaleWall = MeasureWallThicknessMm(female.Occurrence, female.ProxyFace);
        var sectionY = Cross(sideNormal, axis);
        sectionY.Normalize();
        var profileSpan = MeasureSpanMm(maleOccurrence, sectionY);
        var crossSpan = MeasureSpanMm(maleOccurrence, sideNormal);
        var sideASlotCenter = PointAlong(sideASlotSurface, slotAcrossAtoB, maleWall / 20.0);
        var sideBSlotCenter = PointAlong(sideBSlotSurface, slotAcrossAtoB, -maleWall / 20.0);
        var jointPoint = Midpoint(sideASlotCenter, sideBSlotCenter);
        var sideAGap = MeasureRootBackfillMm(
            maleSide, axis, tenonDirection, sideAAnchor, profileSpan / 10.0);
        var sideBGap = MeasureRootBackfillMm(
            oppositeMaleSide, axis, tenonDirection, sideBAnchor, profileSpan / 10.0);
        var alignment = Math.Clamp(Dot(axis, tenonDirection), -1.0, 1.0);

        return new JointPairSelection
        {
            MaleTubeSource = maleTube.Source,
            MaleAxisMethod = maleTube.AxisMethod,
            MaleAxisConfidence = maleTube.AxisConfidence,
            WallPair = wallPair,
            Male = new JointFaceSelection(maleOccurrence, maleSide),
            MaleOpposite = new JointFaceSelection(maleOccurrence, oppositeMaleSide),
            Female = female,
            JointPointAssembly = jointPoint,
            SideAAnchorAssembly = sideAAnchor,
            SideBAnchorAssembly = sideBAnchor,
            SideASlotCenterAssembly = sideASlotCenter,
            SideBSlotCenterAssembly = sideBSlotCenter,
            MaleAxisTowardFemale = axis,
            TenonDirectionAssembly = tenonDirection,
            MaleProfileYAxis = profileY,
            MaleSideNormal = sideNormal,
            MaleWallThicknessMm = maleWall,
            FemaleWallThicknessMm = femaleWall,
            MaleProfileSpanMm = profileSpan,
            MaleCrossSpanMm = crossSpan,
            InsertionDeviationDegrees = Math.Acos(alignment) * 180.0 / Math.PI,
            SideAGapMm = sideAGap,
            SideBGapMm = sideBGap
        };
    }

    public static PartDocument GetPartDocument(ComponentOccurrence occurrence)
    {
        if (occurrence.DefinitionDocumentType != DocumentTypeEnum.kPartDocumentObject)
            throw new InvalidOperationException($"'{occurrence.Name}' не является деталью.");
        return (PartDocument)((PartComponentDefinition)occurrence.Definition).Document;
    }

    private ComponentOccurrence PickPartOccurrence(string prompt)
    {
        var selected = _application.CommandManager.Pick(SelectionFilterEnum.kAssemblyLeafOccurrenceFilter, prompt);
        if (selected is not ComponentOccurrence occurrence)
            throw new OperationCanceledException("Выбор отменён.");
        if (occurrence.DefinitionDocumentType != DocumentTypeEnum.kPartDocumentObject)
            throw new InvalidOperationException("Выберите отдельную деталь трубы.");
        return occurrence;
    }

    private JointFaceSelection PickPlanarFace(string prompt)
    {
        var selected = _application.CommandManager.Pick(SelectionFilterEnum.kPartFacePlanarFilter, prompt);
        if (selected is not FaceProxy face)
            throw new OperationCanceledException("Выбор отменён.");
        return new JointFaceSelection(face.ContainingOccurrence, face);
    }

    private static FaceProxy FindMaleSideFace(ComponentOccurrence occurrence, Vector axis, Point jointPoint)
    {
        var candidates = new List<(FaceProxy Face, Plane Plane, double Area)>();
        foreach (SurfaceBody body in occurrence.SurfaceBodies)
        foreach (Face face in body.Faces)
        {
            if (face is not FaceProxy proxy || proxy.SurfaceType != SurfaceTypeEnum.kPlaneSurface)
                continue;
            var plane = RequirePlane(proxy);
            if (Math.Abs(Dot(plane.Normal.AsVector(), axis)) > 0.15)
                continue;
            candidates.Add((proxy, plane, Convert.ToDouble(proxy.Evaluator.Area)));
        }

        if (candidates.Count == 0)
            throw new InvalidOperationException("Не удалось найти плоскую боковую стенку трубы.");

        // Area chooses the initial wall family (the broad face for a rectangular
        // tube). Trim/Notch can make its inner face larger than the clipped outer
        // face, so a second pass must explicitly choose the outermost plane in
        // that family. Mixing one inner and one outer wall shifts both the paired
        // placement centre and the optional vent by half a wall thickness.
        var orientationSeed = candidates.MaxBy(candidate => candidate.Area);
        var seedNormal = orientationSeed.Plane.Normal.AsVector();
        seedNormal.Normalize();
        return candidates
            .Where(candidate =>
            {
                var normal = candidate.Plane.Normal.AsVector();
                normal.Normalize();
                return Math.Abs(Dot(normal, seedNormal)) >= 0.995;
            })
            .OrderByDescending(candidate =>
            {
                var normal = candidate.Plane.Normal.AsVector();
                normal.Normalize();
                return Math.Abs(Dot(jointPoint.VectorTo(candidate.Plane.RootPoint), normal));
            })
            .ThenByDescending(candidate => candidate.Area)
            .First().Face;
    }

    private static FaceProxy FindOppositeMaleSideFace(
        ComponentOccurrence occurrence,
        Vector axis,
        FaceProxy primary)
    {
        var primaryPlane = RequirePlane(primary);
        var primaryNormal = primaryPlane.Normal.AsVector();
        primaryNormal.Normalize();
        FaceProxy? best = null;
        var bestDistance = -1.0;

        foreach (SurfaceBody body in occurrence.SurfaceBodies)
        foreach (Face face in body.Faces)
        {
            if (face is not FaceProxy candidate || ReferenceEquals(candidate.NativeObject, primary.NativeObject))
                continue;
            if (candidate.SurfaceType != SurfaceTypeEnum.kPlaneSurface)
                continue;

            var plane = RequirePlane(candidate);
            var normal = plane.Normal.AsVector();
            normal.Normalize();
            if (Math.Abs(Dot(normal, axis)) > 0.15)
                continue;
            if (Math.Abs(Dot(normal, primaryNormal)) < 0.995)
                continue;

            var distance = Math.Abs(Dot(primaryPlane.RootPoint.VectorTo(plane.RootPoint), primaryNormal));
            if (distance > bestDistance)
            {
                best = candidate;
                bestDistance = distance;
            }
        }

        return best ?? throw new InvalidOperationException(
            "Не удалось найти противоположную наружную стенку трубы. Для режима A/B нужна прямая полая труба.");
    }

    private static FaceProxy FindOrthogonalMaleSideFace(
        ComponentOccurrence occurrence,
        Vector axis,
        FaceProxy primary,
        Point sectionCenter)
    {
        var primaryNormal = RequirePlane(primary).Normal.AsVector();
        primaryNormal.Normalize();
        FaceProxy? best = null;
        var bestCenterDistance = -1.0;
        var bestArea = -1.0;

        foreach (SurfaceBody body in occurrence.SurfaceBodies)
        foreach (Face face in body.Faces)
        {
            if (face is not FaceProxy candidate || candidate.SurfaceType != SurfaceTypeEnum.kPlaneSurface)
                continue;
            var plane = RequirePlane(candidate);
            var normal = plane.Normal.AsVector();
            normal.Normalize();
            if (Math.Abs(Dot(normal, axis)) > 0.15)
                continue;
            if (Math.Abs(Dot(normal, primaryNormal)) > 0.15)
                continue;

            // The hollow profile also contains parallel inner walls. The outer wall
            // is farther from the section centre; area breaks ties after Trim/Notch.
            var centerDistance = Math.Abs(Dot(sectionCenter.VectorTo(plane.RootPoint), normal));
            var area = Convert.ToDouble(candidate.Evaluator.Area);
            if (centerDistance > bestCenterDistance + 1e-5 ||
                (Math.Abs(centerDistance - bestCenterDistance) <= 1e-5 && area > bestArea))
            {
                best = candidate;
                bestCenterDistance = centerDistance;
                bestArea = area;
            }
        }

        return best ?? throw new InvalidOperationException(
            "Не удалось найти вторую пару наружных стенок C/D.");
    }

    private static Plane RequirePlane(FaceProxy face) =>
        face.Geometry as Plane ?? throw new InvalidOperationException("Грань должна быть плоской.");

    private static double MeasureWallThicknessMm(ComponentOccurrence occurrence, FaceProxy referenceFace)
    {
        var referencePlane = RequirePlane(referenceFace);
        var referenceNormal = referencePlane.Normal.AsVector();
        referenceNormal.Normalize();
        var minimum = double.MaxValue;

        foreach (SurfaceBody body in occurrence.SurfaceBodies)
        foreach (Face face in body.Faces)
        {
            if (face is not FaceProxy candidate || candidate.SurfaceType != SurfaceTypeEnum.kPlaneSurface)
                continue;
            var plane = RequirePlane(candidate);
            var normal = plane.Normal.AsVector();
            normal.Normalize();
            if (Math.Abs(Dot(referenceNormal, normal)) < 0.999)
                continue;

            var distance = Math.Abs(Dot(referencePlane.RootPoint.VectorTo(plane.RootPoint), referenceNormal));
            if (distance > 1e-4 && distance < minimum)
                minimum = distance;
        }

        if (minimum == double.MaxValue || minimum > 2.0)
        {
            throw new InvalidOperationException(
                $"Не удалось автоматически определить толщину стенки трубы '{occurrence.Name}'. " +
                "Нужна полая труба с параллельными внутренней и наружной гранями.");
        }

        return minimum * 10.0; // Inventor database length unit is centimeter.
    }

    private static double MeasureSpanMm(ComponentOccurrence occurrence, Vector assemblyDirection)
    {
        var direction = assemblyDirection.Copy();
        direction.Normalize();
        var minimum = double.MaxValue;
        var maximum = double.MinValue;
        foreach (SurfaceBody body in occurrence.SurfaceBodies)
        foreach (Vertex vertex in body.Vertices)
        {
            var point = vertex.Point;
            var value = point.X * direction.X + point.Y * direction.Y + point.Z * direction.Z;
            minimum = Math.Min(minimum, value);
            maximum = Math.Max(maximum, value);
        }

        if (minimum == double.MaxValue || maximum - minimum < 1e-6)
            throw new InvalidOperationException("Не удалось определить размеры поперечного сечения трубы.");
        return (maximum - minimum) * 10.0;
    }

    private static double MeasureRootBackfillMm(
        FaceProxy sideFace,
        Vector tubeAxis,
        Vector tenonDirection,
        Point anchor,
        double sectionHeight)
    {
        var axis = tubeAxis.Copy();
        axis.Normalize();
        var insertion = tenonDirection.Copy();
        insertion.Normalize();
        var vertices = new List<(Point Point, double AxisStation)>();
        var forwardAxisStation = double.MinValue;
        foreach (Vertex vertex in sideFace.Vertices)
        {
            var point = vertex.Point;
            var station = Station(point, axis);
            vertices.Add((point, station));
            forwardAxisStation = Math.Max(forwardAxisStation, station);
        }

        if (vertices.Count == 0) return 0.0;
        // Only inspect the profiled end of the long side face. This covers angled
        // Trim/Notch contours without accidentally measuring to the remote tube end.
        var endWindow = Math.Max(sectionHeight * 2.25, 0.5);
        var anchorStation = Station(anchor, insertion);
        var rootDepth = 0.0;
        foreach (var candidate in vertices)
        {
            if (candidate.AxisStation < forwardAxisStation - endWindow) continue;
            rootDepth = Math.Max(rootDepth, anchorStation - Station(candidate.Point, insertion));
        }
        return Math.Max(0.0, rootDepth * 10.0);
    }

    private Vector BuildTenonDirection(Vector femaleNormal, Vector sideNormal, Vector tubeAxis)
    {
        var normal = Unit(femaleNormal);
        if (Dot(normal, tubeAxis) < 0) normal = Negated(normal);
        var direction = ProjectToPlane(normal, sideNormal);
        if (direction.Length < 0.05)
            throw new InvalidOperationException(
                "Нормаль выбранной грани почти перпендикулярна плоскости стенки шипа.");
        direction.Normalize();
        if (Dot(direction, tubeAxis) < 0) direction = Negated(direction);
        return direction;
    }

    private Vector ProjectToPlane(Vector source, Vector planeNormal)
    {
        var result = Unit(source);
        var normal = Unit(planeNormal);
        var normalPart = normal.Copy();
        normalPart.ScaleBy(Dot(result, normal));
        result.SubtractVector(normalPart);
        return result;
    }

    private Point MovePointToPlane(Point seed, Vector travelDirection, Plane targetPlane)
    {
        var normal = targetPlane.Normal.AsVector();
        var denominator = Dot(travelDirection, normal);
        if (Math.Abs(denominator) < Tolerance)
            throw new InvalidOperationException("Не удалось совместить центр соединения с плоскостью стенки.");
        var distance = Dot(seed.VectorTo(targetPlane.RootPoint), normal) / denominator;
        return PointAlong(seed, travelDirection, distance);
    }

    private Point Midpoint(Point first, Point second) => _application.TransientGeometry.CreatePoint(
        (first.X + second.X) / 2.0,
        (first.Y + second.Y) / 2.0,
        (first.Z + second.Z) / 2.0);

    private static double Station(Point point, Vector direction) =>
        point.X * direction.X + point.Y * direction.Y + point.Z * direction.Z;
    private Point CenterOf(Box box) => _application.TransientGeometry.CreatePoint(
        (box.MinPoint.X + box.MaxPoint.X) / 2.0,
        (box.MinPoint.Y + box.MaxPoint.Y) / 2.0,
        (box.MinPoint.Z + box.MaxPoint.Z) / 2.0);
    private Point PointAlong(Point origin, Vector direction, double distance) =>
        _application.TransientGeometry.CreatePoint(
            origin.X + direction.X * distance,
            origin.Y + direction.Y * distance,
            origin.Z + direction.Z * distance);
    private Vector Negated(Vector v) => _application.TransientGeometry.CreateVector(-v.X, -v.Y, -v.Z);
    private Vector Unit(Vector v)
    {
        var result = _application.TransientGeometry.CreateVector(v.X, v.Y, v.Z);
        result.Normalize();
        return result;
    }
    private Vector Cross(Vector a, Vector b)
    {
        var result = _application.TransientGeometry.CreateVector(
            a.Y * b.Z - a.Z * b.Y,
            a.Z * b.X - a.X * b.Z,
            a.X * b.Y - a.Y * b.X);
        if (result.Length < Tolerance)
            throw new InvalidOperationException("Не удалось определить ориентацию профиля.");
        return result;
    }
    private static double Dot(Vector a, Vector b) => a.X * b.X + a.Y * b.Y + a.Z * b.Z;

    private static void EnsureWritable(PartDocument document, string role)
    {
        if (string.IsNullOrWhiteSpace(document.FullFileName))
            throw new InvalidOperationException($"Перед созданием соединения сохраните {role}.");
        if (!document.IsModifiable)
            throw new InvalidOperationException($"Нельзя редактировать {role}: {document.DisplayName}");
    }
}
