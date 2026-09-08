using Inventor;

namespace TubeJoint.AddIn.Services;

internal sealed record TemplatePoint(double X, double Y, int ReliefId = -1);

internal sealed record TemplateRelief(int Id, double CenterX, double CenterY, double Radius);

internal sealed record TemplateProfileShape(
    IReadOnlyList<TemplatePoint> Points,
    double MinX,
    double MaxX,
    double MinY,
    double MaxY,
    IReadOnlyList<TemplateRelief> Reliefs)
{
    public double Width => MaxX - MinX;
    public double Height => MaxY - MinY;
}

internal sealed record TemplateProfileSet(
    TemplateProfileShape? Male,
    TemplateProfileShape? Female,
    TemplateProfileShape? Vent,
    IReadOnlyList<string> Errors);

/// <summary>Reads the same ordered closed contours that Inventor uses for extrusion.</summary>
internal static class TemplateProfileSampler
{
    public static TemplateProfileSet Load(
        Inventor.Application application,
        string templatePath,
        JointTemplateService? templates = null,
        TubeJoint.AddIn.Models.TubeJointParameters? parameters = null)
    {
        PartDocument? document = null;
        var openedHere = false;
        foreach (Document openDocument in application.Documents)
        {
            if (!string.Equals(openDocument.FullFileName, templatePath, StringComparison.OrdinalIgnoreCase))
                continue;
            document = (PartDocument)openDocument;
            break;
        }

        if (document is null)
        {
            document = (PartDocument)application.Documents.Open(templatePath, false);
            openedHere = true;
        }

        try
        {
            using var applied = templates is not null && parameters is not null
                ? templates.TemporarilyApplyParameters(document, parameters)
                : null;
            var sketches = document.ComponentDefinition.Sketches;
            var errors = new List<string>();
            return new TemplateProfileSet(
                TrySample(sketches[JointTemplateService.MaleSketchName], errors),
                TrySample(sketches[JointTemplateService.FemaleSketchName], errors),
                TrySample(sketches[JointTemplateService.CenterVentSketchName], errors),
                errors);
        }
        finally
        {
            if (openedHere) document.Close(false);
        }
    }

    private static TemplateProfileShape? TrySample(PlanarSketch sketch, ICollection<string> errors)
    {
        try { return SampleSketch(sketch); }
        catch (Exception exception)
        {
            errors.Add($"{sketch.Name}: {exception.Message}");
            return null;
        }
    }

    internal static TemplateProfileShape SampleSketch(PlanarSketch sketch)
    {
        Profile profile;
        try { profile = sketch.Profiles.AddForSolid(); }
        catch (Exception profileException)
        {
            return SampleConnectedSketchGeometry(sketch, profileException);
        }
        ProfilePath? selected = null;
        foreach (ProfilePath path in profile)
        {
            if (path.TextBoxPath || !path.Closed || !path.AddsMaterial) continue;
            if (selected is not null)
                throw new InvalidOperationException(
                    $"Эскиз '{sketch.Name}' должен содержать ровно один внешний закрытый контур.");
            selected = path;
        }

        if (selected is null)
            throw new InvalidOperationException($"В эскизе '{sketch.Name}' нет закрытого рабочего контура.");

        var segments = new List<RawSegment>();
        foreach (ProfileEntity entity in selected)
        {
            switch (entity.Curve)
            {
                case LineSegment2d line:
                    segments.Add(new RawSegment(new List<TemplatePoint>
                        {
                            new(line.StartPoint.X, line.StartPoint.Y),
                            new(line.EndPoint.X, line.EndPoint.Y)
                        }, null, 0.0));
                    break;
                case Arc2d arc:
                    segments.Add(new RawSegment(SampleArc(
                        arc.Center, arc.Radius, arc.StartAngle, arc.SweepAngle, 36),
                        new TemplatePoint(arc.Center.X, arc.Center.Y), arc.Radius));
                    break;
                case Circle2d circle:
                    segments.Add(new RawSegment(SampleArc(
                        circle.Center, circle.Radius, 0, Math.PI * 2.0, 48),
                        new TemplatePoint(circle.Center.X, circle.Center.Y), circle.Radius));
                    break;
                case BSplineCurve2d spline:
                    segments.Add(new RawSegment(
                        SampleEvaluatedCurve(spline.Evaluator, sketch.Name, "Control Vertex Spline"),
                        null, 0.0));
                    break;
                case EllipticalArc2d ellipticalArc:
                    segments.Add(new RawSegment(
                        SampleEvaluatedCurve(ellipticalArc.Evaluator, sketch.Name, "эллиптическая дуга"),
                        null, 0.0));
                    break;
                case EllipseFull2d ellipse:
                    segments.Add(new RawSegment(
                        SampleEvaluatedCurve(ellipse.Evaluator, sketch.Name, "эллипс"),
                        null, 0.0));
                    break;
                case Polyline2d polyline:
                    segments.Add(new RawSegment(
                        SampleEvaluatedCurve(polyline.Evaluator, sketch.Name, "полилиния"),
                        null, 0.0));
                    break;
                default:
                    throw new InvalidOperationException(
                        $"Эскиз '{sketch.Name}' содержит неподдерживаемую кривую " +
                        $"Inventor CurveType={entity.CurveType}.");
            }
        }

        return BuildShape(sketch.Name, segments);
    }

    private static TemplateProfileShape SampleConnectedSketchGeometry(
        PlanarSketch sketch,
        Exception profileException)
    {
        try
        {
            var segments = new List<RawSegment>();
            foreach (SketchEntity entity in sketch.SketchEntities)
            {
                if (entity.Construction || entity.Reference)
                    continue;
                switch (entity)
                {
                    case SketchPoint:
                        // Origin/center markers can be non-construction entities,
                        // but points never participate in a solid profile boundary.
                        break;
                    case SketchLine line:
                        segments.Add(new RawSegment(new List<TemplatePoint>
                        {
                            new(line.Geometry.StartPoint.X, line.Geometry.StartPoint.Y),
                            new(line.Geometry.EndPoint.X, line.Geometry.EndPoint.Y)
                        }, null, 0.0));
                        break;
                    case SketchArc arc:
                        segments.Add(new RawSegment(SampleArc(
                            arc.Geometry.Center, arc.Geometry.Radius,
                            arc.Geometry.StartAngle, arc.Geometry.SweepAngle, 36),
                            new TemplatePoint(arc.Geometry.Center.X, arc.Geometry.Center.Y),
                            arc.Geometry.Radius));
                        break;
                    case SketchControlPointSpline spline:
                        segments.Add(new RawSegment(SampleEvaluatedCurve(
                            spline.Geometry.Evaluator, sketch.Name, "Control Vertex Spline"),
                            null, 0.0));
                        break;
                    case SketchSpline spline:
                        segments.Add(new RawSegment(SampleEvaluatedCurve(
                            spline.Geometry.Evaluator, sketch.Name, "Interpolation Spline"),
                            null, 0.0));
                        break;
                    case SketchFixedSpline spline:
                        segments.Add(new RawSegment(SampleEvaluatedCurve(
                            spline.Geometry.Evaluator, sketch.Name, "Fixed Spline"),
                            null, 0.0));
                        break;
                    case SketchEllipticalArc arc:
                        segments.Add(new RawSegment(SampleEvaluatedCurve(
                            arc.Geometry.Evaluator, sketch.Name, "эллиптическая дуга"),
                            null, 0.0));
                        break;
                    default:
                        throw new InvalidOperationException(
                            $"резервное чтение не поддерживает {entity.Type}");
                }
            }
            return BuildShape(sketch.Name, segments);
        }
        catch (Exception fallbackException)
        {
            throw new InvalidOperationException(
                $"Inventor не создал Profile ({profileException.Message}); " +
                $"резервное чтение связных кривых также не удалось: {fallbackException.Message}",
                profileException);
        }
    }

    private static TemplateProfileShape BuildShape(string sketchName, List<RawSegment> segments)
    {
        var rawPoints = segments.SelectMany(segment => segment.Points).ToArray();
        if (rawPoints.Length == 0)
            throw new InvalidOperationException($"Рабочий контур '{sketchName}' не содержит кривых.");
        var rawWidth = rawPoints.Max(point => point.X) - rawPoints.Min(point => point.X);
        var rawHeight = rawPoints.Max(point => point.Y) - rawPoints.Min(point => point.Y);
        var reliefLimit = Math.Max(0.03, Math.Min(rawWidth, rawHeight) * 0.24);
        var reliefs = new List<TemplateRelief>();
        foreach (var segment in segments)
        {
            if (segment.Center is null || segment.Radius <= 1e-8 || segment.Radius > reliefLimit)
                continue;
            var id = reliefs.Count;
            reliefs.Add(new TemplateRelief(id, segment.Center.X, segment.Center.Y, segment.Radius));
            for (var index = 0; index < segment.Points.Count; index++)
                segment.Points[index] = segment.Points[index] with { ReliefId = id };
        }

        var points = StitchSegments(sketchName, segments.Select(segment => segment.Points).ToArray());
        // A shared endpoint can be contributed by the adjacent line and lose its arc tag.
        // Restore it from the sampled relief points so the contour stays exactly closed.
        var reliefPoints = segments.SelectMany(segment => segment.Points)
            .Where(point => point.ReliefId >= 0).ToArray();
        for (var index = 0; index < points.Count; index++)
        {
            var tagged = reliefPoints.FirstOrDefault(candidate => Distance(candidate, points[index]) < 1e-7);
            if (tagged is not null)
                points[index] = points[index] with { ReliefId = tagged.ReliefId };
        }
        if (points.Count > 1 && Distance(points[0], points[^1]) > 1e-4)
            throw new InvalidOperationException(
                $"Контур эскиза '{sketchName}' не замкнут в исходной точке.");
        if (points.Count > 1 && Distance(points[0], points[^1]) < 1e-8)
            points.RemoveAt(points.Count - 1);
        if (points.Count < 3)
            throw new InvalidOperationException($"Рабочий контур '{sketchName}' содержит меньше трёх точек.");

        return new TemplateProfileShape(
            points,
            points.Min(point => point.X), points.Max(point => point.X),
            points.Min(point => point.Y), points.Max(point => point.Y),
            reliefs);
    }

    private static List<TemplatePoint> SampleArc(
        Point2d center,
        double radius,
        double startAngle,
        double sweepAngle,
        int steps)
    {
        var points = new List<TemplatePoint>();
        steps = Math.Max(2, steps);
        for (var i = 0; i <= steps; i++)
        {
            var angle = startAngle + sweepAngle * i / steps;
            Add(points, center.X + radius * Math.Cos(angle), center.Y + radius * Math.Sin(angle));
        }
        return points;
    }

    private static List<TemplatePoint> SampleEvaluatedCurve(
            Curve2dEvaluator evaluator,
            string sketchName,
            string curveName)
    {
        try
        {
            evaluator.GetParamExtents(out var minimum, out var maximum);
            // GetStrokes on an Inventor Control Vertex Spline can marshal its
            // SAFEARRAY as the wrong COM variant (DISP_E_TYPEMISMATCH). Evaluate
            // an initialized parameter array instead; both arrays are [In, Out]
            // in the official 31.0 interop and marshal reliably when preallocated.
            const int segmentCount = 160;
            var parameters = new double[segmentCount + 1];
            for (var index = 0; index <= segmentCount; index++)
                parameters[index] = minimum + (maximum - minimum) * index / segmentCount;
            var coordinates = new double[parameters.Length * 2];
            evaluator.GetPointAtParam(ref parameters, ref coordinates);
            var vertexCount = Math.Min(parameters.Length, coordinates.Length / 2);
            if (vertexCount < 2)
                throw new InvalidOperationException("Оценщик кривой вернул недостаточно точек.");
            var points = new List<TemplatePoint>(vertexCount);
            for (var index = 0; index < vertexCount; index++)
                Add(points, coordinates[index * 2], coordinates[index * 2 + 1]);
            return points;
        }
        catch (Exception exception)
        {
            throw new InvalidOperationException(
                $"Не удалось считать {curveName} в эскизе '{sketchName}' через Inventor evaluator: " +
                exception.Message,
                exception);
        }
    }

    private static List<TemplatePoint> StitchSegments(
        string sketchName,
        IReadOnlyList<List<TemplatePoint>> source)
    {
        if (source.Count == 0)
            return new List<TemplatePoint>();

        var unused = source.Select(segment => new List<TemplatePoint>(segment)).ToList();
        var result = unused[0];
        unused.RemoveAt(0);
        while (unused.Count > 0)
        {
            var end = result[^1];
            var bestIndex = -1;
            var reverse = false;
            var bestDistance = double.MaxValue;
            for (var index = 0; index < unused.Count; index++)
            {
                var toStart = Distance(end, unused[index][0]);
                if (toStart < bestDistance)
                {
                    bestIndex = index;
                    reverse = false;
                    bestDistance = toStart;
                }
                var toEnd = Distance(end, unused[index][^1]);
                if (toEnd < bestDistance)
                {
                    bestIndex = index;
                    reverse = true;
                    bestDistance = toEnd;
                }
            }

            if (bestIndex < 0 || bestDistance > 1e-4)
                throw new InvalidOperationException(
                    $"Контур эскиза '{sketchName}' не удалось собрать в одну замкнутую границу.");

            var next = unused[bestIndex];
            unused.RemoveAt(bestIndex);
            if (reverse) next.Reverse();
            foreach (var point in next.Skip(1)) Add(result, point.X, point.Y, point.ReliefId);
        }
        return result;
    }

    private static void Add(List<TemplatePoint> points, double x, double y, int reliefId = -1)
    {
        var point = new TemplatePoint(x, y, reliefId);
        if (points.Count == 0 || Distance(points[^1], point) > 1e-8)
            points.Add(point);
        else if (reliefId >= 0 && points[^1].ReliefId < 0)
            points[^1] = point;
    }

    private static double Distance(TemplatePoint a, TemplatePoint b)
    {
        var dx = a.X - b.X;
        var dy = a.Y - b.Y;
        return Math.Sqrt(dx * dx + dy * dy);
    }

    private sealed record RawSegment(
        List<TemplatePoint> Points,
        TemplatePoint? Center,
        double Radius);
}
