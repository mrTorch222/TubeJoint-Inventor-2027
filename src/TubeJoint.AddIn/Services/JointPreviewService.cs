using Inventor;
using TubeJoint.AddIn.Models;
using Point = Inventor.Point;

namespace TubeJoint.AddIn.Services;

/// <summary>
/// Non-transacting preview built from the actual editable template contours.
/// It deliberately uses triangle client graphics instead of transient B-Rep:
/// concave hand-drawn profiles and small laser-relief arcs remain stable.
/// </summary>
internal sealed class JointPreviewService : IDisposable
{
    private const double CutOverlapCm = 0.02;
    private const string GraphicsId = "TubeJoint.LivePreview.V2";
    private const string OldGraphicsId = "TubeJoint.LivePreview.V1";
    private readonly Inventor.Application _application;
    private readonly AssemblyDocument _assembly;
    private readonly JointTemplateService _templates;
    private string _templatePath;
    private TemplateProfileSet? _profiles;
    private ClientGraphics? _graphics;
    private GraphicsDataSets? _dataSets;
    private int _nodeId;
    private int _dataId;

    public JointPreviewService(
        Inventor.Application application,
        AssemblyDocument assembly,
        JointTemplateService templates,
        string templatePath)
    {
        _application = application;
        _assembly = assembly;
        _templates = templates;
        _templatePath = templates.EnsureTemplate(templatePath);
    }

    public void SelectTemplate(string templatePath)
    {
        _templatePath = _templates.EnsureTemplate(templatePath);
        _profiles = null;
    }

    public string? Rebuild(JointPairSelection selection, TubeJointParameters parameters)
    {
        Clear();
        try
        {
            TryDeleteStale();
            _dataSets = _assembly.NonTransactingGraphicsDataSetsCollection.Add(GraphicsId);
            _graphics = _assembly.NonTransactingClientGraphicsCollection.Add(GraphicsId);
            _nodeId = 0;
            _dataId = 0;
            // The Inventor template is the only sizing authority. Re-apply TJ_*
            // values and resample its solved sketches for every preview rebuild.
            _profiles = TemplateProfileSampler.Load(
                _application, _templatePath, _templates, parameters);
            var profiles = _profiles;

            var tenonX = Unit(selection.TenonDirectionAssembly);
            var tenonY = Unit(selection.MaleProfileYAxis);
            var wallAtoB = Unit(selection.SideAAnchorAssembly.VectorTo(selection.SideBAnchorAssembly));
            var tenonThickness = Math.Max(parameters.MaleWallMm / 10.0, 0.03);
            var pairedRootGap = Math.Max(selection.SideAGapMm, selection.SideBGapMm) / 10.0;

            if (profiles.Male is not null &&
                parameters.SideMode is JointSideMode.SideA or JointSideMode.Both)
            {
                var origin = OffsetAlong(
                    selection.SideAAnchorAssembly, tenonY,
                    (parameters.JointOffsetYMm + parameters.SideAOffsetMm) / 10.0);
                AddProfilePreview(
                    origin, tenonX, tenonY, wallAtoB,
                    ParametricMale(profiles.Male, pairedRootGap),
                    0.0, tenonThickness, 35, 160, 245, 0.62);
            }

            if (profiles.Male is not null &&
                parameters.SideMode is JointSideMode.SideB or JointSideMode.Both)
            {
                var origin = OffsetAlong(
                    selection.SideBAnchorAssembly, tenonY,
                    (parameters.JointOffsetYMm + parameters.SideBOffsetMm) / 10.0);
                AddProfilePreview(
                    origin, tenonX, tenonY, Negated(wallAtoB),
                    ParametricMale(profiles.Male, pairedRootGap),
                    0.0, tenonThickness, 55, 205, 135, 0.62);
            }

            var (slotX, slotY) = TemplateProfileGeometryBuilder.BuildFemaleAxes(selection);
            var slotNormal = Unit(((Plane)selection.Female.ProxyFace.Geometry).Normal.AsVector());
            var slotDepth = Math.Max(parameters.FemaleWallMm / 10.0 + CutOverlapCm, 0.04);

            if (profiles.Female is not null &&
                parameters.SideMode is JointSideMode.SideA or JointSideMode.Both)
            {
                var origin = OffsetAlong(
                    selection.SideASlotCenterAssembly, tenonY,
                    (parameters.JointOffsetYMm + parameters.SideAOffsetMm) / 10.0);
                AddProfilePreview(
                    origin, slotX, slotY, slotNormal,
                    profiles.Female.Points,
                    -slotDepth / 2.0, slotDepth / 2.0, 245, 125, 30, 0.52);
            }

            if (profiles.Female is not null &&
                parameters.SideMode is JointSideMode.SideB or JointSideMode.Both)
            {
                var origin = OffsetAlong(
                    selection.SideBSlotCenterAssembly, tenonY,
                    (parameters.JointOffsetYMm + parameters.SideBOffsetMm) / 10.0);
                AddProfilePreview(
                    origin, slotX, slotY, slotNormal,
                    profiles.Female.Points,
                    -slotDepth / 2.0, slotDepth / 2.0, 245, 125, 30, 0.52);
            }

            if (parameters.CreateCenterVent && profiles.Vent is not null)
            {
                var ventOrigin = OffsetAlong(
                    selection.JointPointAssembly, slotX, parameters.HoleOffsetXMm / 10.0);
                ventOrigin = OffsetAlong(
                    ventOrigin, slotY,
                    (parameters.JointOffsetYMm + parameters.HoleOffsetYMm) / 10.0);
                AddProfilePreview(
                    ventOrigin, slotX, slotY, slotNormal,
                    ScaleFromOrigin(profiles.Vent.Points, parameters.VentScalePercent / 100.0),
                    -slotDepth / 2.0, slotDepth / 2.0, 235, 205, 40, 0.52);
            }

            _application.ActiveView.Update();
            return profiles.Errors.Count == 0
                ? null
                : "Не удалось показать часть превью: " + string.Join(" | ", profiles.Errors);
        }
        catch (Exception exception)
        {
            // Preview is auxiliary and must never block creation of real features.
            Clear();
            return "Не удалось построить превью: " + exception.Message;
        }
    }

    public void Clear()
    {
        try { _graphics?.Delete(); } catch { }
        try { _dataSets?.Delete(); } catch { }
        _graphics = null;
        _dataSets = null;
        // Also remove collections left behind when Inventor hid the dockable window
        // before the normal Dispose path could delete the cached RCWs.
        TryDeleteStale();
        try { _application.ActiveView.Update(); } catch { }
    }

    public void Dispose() => Clear();

    private void AddProfilePreview(
        Point origin,
        Vector x,
        Vector y,
        Vector z,
        IReadOnlyList<TemplatePoint> sourcePoints,
        double firstZ,
        double secondZ,
        byte red,
        byte green,
        byte blue,
        double opacity)
    {
        if (_graphics is null || _dataSets is null || sourcePoints.Count < 3)
            return;

        var points = CleanPolygon(sourcePoints);
        if (points.Count < 3)
            return;
        if (SignedArea(points) < 0)
            points.Reverse();

        var triangles2d = Triangulate(points);
        var coordinates = new List<double>();
        foreach (var triangle in triangles2d)
        {
            AddTriangle(coordinates, origin, x, y, z,
                points[triangle.A], secondZ, points[triangle.B], secondZ, points[triangle.C], secondZ);
            AddTriangle(coordinates, origin, x, y, z,
                points[triangle.C], firstZ, points[triangle.B], firstZ, points[triangle.A], firstZ);
        }

        for (var index = 0; index < points.Count; index++)
        {
            var next = (index + 1) % points.Count;
            AddTriangle(coordinates, origin, x, y, z,
                points[index], firstZ, points[next], firstZ, points[next], secondZ);
            AddTriangle(coordinates, origin, x, y, z,
                points[index], firstZ, points[next], secondZ, points[index], secondZ);
        }

        if (coordinates.Count == 0)
            return;

        var node = _graphics.AddNode(++_nodeId);
        node.Selectable = false;
        node.ExcludedFromViewAll = true;
        node.OverrideOpacity = opacity;
        node.DisplayName = "TubeJoint preview";

        var coordinateSet = CreateCoordinateSet(coordinates);
        var colorSet = _dataSets.CreateColorSet(++_dataId);
        colorSet.Add(1, red, green, blue);
        var triangles = node.AddTriangleGraphics();
        triangles.CoordinateSet = coordinateSet;
        triangles.ColorSet = colorSet;
        triangles.ColorBinding = ColorBindingEnum.kOverallColor;
        triangles.BurnThrough = true;
        triangles.DepthPriority = 5;

        var outline = BuildOutlineCoordinates(origin, x, y, z, points, firstZ, secondZ);
        var lineSet = CreateCoordinateSet(outline);
        var lineColor = _dataSets.CreateColorSet(++_dataId);
        lineColor.Add(1, Lighten(red), Lighten(green), Lighten(blue));
        var lines = node.AddLineGraphics();
        lines.CoordinateSet = lineSet;
        lines.ColorSet = lineColor;
        lines.ColorBinding = ColorBindingEnum.kOverallColor;
        lines.LineWeight = 2.0;
        lines.BurnThrough = true;
        lines.DepthPriority = 6;
    }

    private GraphicsCoordinateSet CreateCoordinateSet(IReadOnlyList<double> values)
    {
        if (_dataSets is null)
            throw new InvalidOperationException("Набор данных превью не создан.");
        var result = _dataSets.CreateCoordinateSet(++_dataId);
        // Inventor exposes SAFEARRAY differently between interop versions.
        // Dynamic invocation keeps the same source compatible with Inventor 2027.
        ((dynamic)result).PutCoordinates(values.ToArray());
        return result;
    }

    private static List<double> BuildOutlineCoordinates(
        Point origin,
        Vector x,
        Vector y,
        Vector z,
        IReadOnlyList<TemplatePoint> points,
        double firstZ,
        double secondZ)
    {
        var result = new List<double>();
        for (var index = 0; index < points.Count; index++)
        {
            var next = (index + 1) % points.Count;
            AddLine(result, origin, x, y, z, points[index], firstZ, points[next], firstZ);
            AddLine(result, origin, x, y, z, points[index], secondZ, points[next], secondZ);
            AddLine(result, origin, x, y, z, points[index], firstZ, points[index], secondZ);
        }
        return result;
    }

    private static void AddTriangle(
        ICollection<double> target,
        Point origin,
        Vector x,
        Vector y,
        Vector z,
        TemplatePoint a,
        double az,
        TemplatePoint b,
        double bz,
        TemplatePoint c,
        double cz)
    {
        AddPoint(target, origin, x, y, z, a, az);
        AddPoint(target, origin, x, y, z, b, bz);
        AddPoint(target, origin, x, y, z, c, cz);
    }

    private static void AddLine(
        ICollection<double> target,
        Point origin,
        Vector x,
        Vector y,
        Vector z,
        TemplatePoint a,
        double az,
        TemplatePoint b,
        double bz)
    {
        AddPoint(target, origin, x, y, z, a, az);
        AddPoint(target, origin, x, y, z, b, bz);
    }

    private static void AddPoint(
        ICollection<double> target,
        Point origin,
        Vector x,
        Vector y,
        Vector z,
        TemplatePoint point,
        double localZ)
    {
        target.Add(origin.X + x.X * point.X + y.X * point.Y + z.X * localZ);
        target.Add(origin.Y + x.Y * point.X + y.Y * point.Y + z.Y * localZ);
        target.Add(origin.Z + x.Z * point.X + y.Z * point.Y + z.Z * localZ);
    }

    private static List<TemplatePoint> CleanPolygon(IReadOnlyList<TemplatePoint> source)
    {
        var result = new List<TemplatePoint>(source.Count);
        foreach (var point in source)
        {
            if (result.Count == 0 || DistanceSquared(result[^1], point) > 1e-12)
                result.Add(point);
        }
        if (result.Count > 1 && DistanceSquared(result[0], result[^1]) <= 1e-12)
            result.RemoveAt(result.Count - 1);
        return result;
    }

    private static IReadOnlyList<Triangle2d> Triangulate(IReadOnlyList<TemplatePoint> points)
    {
        var remaining = Enumerable.Range(0, points.Count).ToList();
        var result = new List<Triangle2d>();
        var guard = points.Count * points.Count;
        while (remaining.Count > 3 && guard-- > 0)
        {
            var earFound = false;
            for (var cursor = 0; cursor < remaining.Count; cursor++)
            {
                var previous = remaining[(cursor - 1 + remaining.Count) % remaining.Count];
                var current = remaining[cursor];
                var next = remaining[(cursor + 1) % remaining.Count];
                if (Cross(points[previous], points[current], points[next]) <= 1e-10)
                    continue;

                var containsPoint = false;
                foreach (var candidate in remaining)
                {
                    if (candidate == previous || candidate == current || candidate == next)
                        continue;
                    if (PointInTriangle(points[candidate], points[previous], points[current], points[next]))
                    {
                        containsPoint = true;
                        break;
                    }
                }
                if (containsPoint)
                    continue;

                result.Add(new Triangle2d(previous, current, next));
                remaining.RemoveAt(cursor);
                earFound = true;
                break;
            }
            if (!earFound)
                break;
        }
        if (remaining.Count == 3)
            result.Add(new Triangle2d(remaining[0], remaining[1], remaining[2]));
        return result;
    }

    private static bool PointInTriangle(
        TemplatePoint point,
        TemplatePoint a,
        TemplatePoint b,
        TemplatePoint c)
    {
        const double epsilon = 1e-10;
        return Cross(a, b, point) >= -epsilon &&
               Cross(b, c, point) >= -epsilon &&
               Cross(c, a, point) >= -epsilon;
    }

    private static double SignedArea(IReadOnlyList<TemplatePoint> points)
    {
        var area = 0.0;
        for (var index = 0; index < points.Count; index++)
        {
            var next = (index + 1) % points.Count;
            area += points[index].X * points[next].Y - points[next].X * points[index].Y;
        }
        return area / 2.0;
    }

    private static double Cross(TemplatePoint a, TemplatePoint b, TemplatePoint c) =>
        (b.X - a.X) * (c.Y - a.Y) - (b.Y - a.Y) * (c.X - a.X);

    private static double DistanceSquared(TemplatePoint a, TemplatePoint b)
    {
        var dx = a.X - b.X;
        var dy = a.Y - b.Y;
        return dx * dx + dy * dy;
    }

    private static IReadOnlyList<TemplatePoint> ParametricMale(
        TemplateProfileShape shape,
        double forwardGap)
    {
        if (forwardGap > 1e-6 && -shape.MinX + 1e-6 < forwardGap)
            throw new InvalidOperationException(
                "TJ_MALE_PROFILE не перекрывает зазор Trim/Notch.");
        return shape.Points;
    }

    private static IReadOnlyList<TemplatePoint> ScaleFromOrigin(
        IReadOnlyList<TemplatePoint> points,
        double scale)
    {
        scale = Math.Clamp(scale, 0.1, 5.0);
        return points.Select(point => new TemplatePoint(
            point.X * scale, point.Y * scale, point.ReliefId)).ToArray();
    }

    private Point OffsetAlong(Point source, Vector direction, double distance) =>
        _application.TransientGeometry.CreatePoint(
            source.X + direction.X * distance,
            source.Y + direction.Y * distance,
            source.Z + direction.Z * distance);

    private static Vector Unit(Vector vector)
    {
        var result = vector.Copy();
        result.Normalize();
        return result;
    }

    private static Vector Negated(Vector vector)
    {
        var result = vector.Copy();
        result.ScaleBy(-1.0);
        return result;
    }

    private static byte Lighten(byte value) => (byte)Math.Min(255, value + 55);

    private void TryDeleteStale()
    {
        TryDeleteGraphics(GraphicsId);
        TryDeleteData(GraphicsId);
        TryDeleteGraphics(OldGraphicsId);
        TryDeleteData(OldGraphicsId);
    }

    private void TryDeleteGraphics(string id)
    {
        try { _assembly.NonTransactingClientGraphicsCollection[id].Delete(); } catch { }
    }

    private void TryDeleteData(string id)
    {
        try { _assembly.NonTransactingGraphicsDataSetsCollection[id].Delete(); } catch { }
    }

    private sealed record Triangle2d(int A, int B, int C);
}
