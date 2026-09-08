using Inventor;
using TubeJoint.AddIn.Models;
using Point = Inventor.Point;

namespace TubeJoint.AddIn.Services;

internal sealed class GenericTubeAnalyzer : ITubeMemberAnalyzer
{
    private const double ParallelTolerance = 0.995;
    private const double LengthTolerance = 1e-7;
    private readonly Inventor.Application _application;
    private readonly TubeMemberSource _source;

    public GenericTubeAnalyzer(
        Inventor.Application application,
        TubeMemberSource source = TubeMemberSource.GenericSolid)
    {
        _application = application;
        _source = source;
    }

    public bool CanAnalyze(ComponentOccurrence occurrence) =>
        occurrence.DefinitionDocumentType == DocumentTypeEnum.kPartDocumentObject;

    public TubeMemberAnalysis Analyze(ComponentOccurrence occurrence)
    {
        if (!CanAnalyze(occurrence))
            throw new InvalidOperationException($"'{occurrence.Name}' не является деталью.");

        var solidBodies = new List<SurfaceBody>();
        foreach (SurfaceBody body in occurrence.SurfaceBodies)
            if (body.IsSolid) solidBodies.Add(body);

        if (solidBodies.Count == 0)
        {
            throw new InvalidOperationException(
                $"В детали '{occurrence.Name}' нет замкнутого solid-тела. " +
                "Поверхностные и сеточные модели пока не поддерживаются.");
        }

        if (solidBodies.Count > 1)
        {
            throw new InvalidOperationException(
                $"В детали '{occurrence.Name}' найдено несколько solid-тел ({solidBodies.Count}). " +
                "Для соединения выберите деталь с одним телом трубы.");
        }

        var lineGroups = BuildLinearDirectionGroups(solidBodies[0]);
        Vector axis;
        string method;
        double confidence;

        if (lineGroups.Count > 0)
        {
            var ordered = lineGroups
                .OrderByDescending(group => group.Score)
                .ThenByDescending(group => group.LongestEdge)
                .ToList();
            axis = CopyUnit(ordered[0].Direction);
            confidence = ordered.Count == 1
                ? double.PositiveInfinity
                : ordered[0].Score / Math.Max(ordered[1].Score, LengthTolerance);
            method = "LinearEdges";
        }
        else if (TryFindCylindricalAxis(solidBodies[0], out var cylinderAxis))
        {
            axis = cylinderAxis;
            confidence = double.PositiveInfinity;
            method = "CylindricalFaces";
        }
        else
        {
            throw new InvalidOperationException(
                $"Не удалось определить продольную ось трубы '{occurrence.Name}'. " +
                "Нужна прямая призматическая или цилиндрическая solid-деталь.");
        }

        return new TubeMemberAnalysis
        {
            Source = _source,
            AxisAssembly = axis,
            CenterAssembly = CenterOf(occurrence.RangeBox),
            AxisMethod = method,
            SolidBodyCount = solidBodies.Count,
            LinearEdgeCount = lineGroups.Sum(group => group.EdgeCount),
            AxisConfidence = confidence
        };
    }

    private List<DirectionGroup> BuildLinearDirectionGroups(SurfaceBody body)
    {
        var groups = new List<DirectionGroup>();
        foreach (Edge edge in body.Edges)
        {
            if (edge.Geometry is not LineSegment line) continue;
            var length = line.StartPoint.DistanceTo(line.EndPoint);
            if (length <= LengthTolerance) continue;

            var direction = line.StartPoint.VectorTo(line.EndPoint);
            direction.Normalize();
            var group = groups.FirstOrDefault(candidate =>
                Math.Abs(Dot(candidate.Direction, direction)) >= ParallelTolerance);
            if (group is null)
            {
                group = new DirectionGroup(CopyUnit(direction));
                groups.Add(group);
            }

            // Squared length prevents numerous short chamfer and relief edges from
            // outweighing the fewer long edges that describe the tube extrusion.
            group.Score += length * length;
            group.LongestEdge = Math.Max(group.LongestEdge, length);
            group.EdgeCount++;
        }
        return groups;
    }

    private bool TryFindCylindricalAxis(SurfaceBody body, out Vector axis)
    {
        Cylinder? bestCylinder = null;
        var bestArea = -1.0;
        foreach (Face face in body.Faces)
        {
            if (face.SurfaceType != SurfaceTypeEnum.kCylinderSurface ||
                face.Geometry is not Cylinder cylinder)
                continue;
            var area = Convert.ToDouble(face.Evaluator.Area);
            if (area <= bestArea) continue;
            bestCylinder = cylinder;
            bestArea = area;
        }

        if (bestCylinder is null)
        {
            axis = null!;
            return false;
        }

        axis = CopyUnit(bestCylinder.AxisVector.AsVector());
        return true;
    }

    private Vector CopyUnit(Vector source)
    {
        var result = _application.TransientGeometry.CreateVector(source.X, source.Y, source.Z);
        result.Normalize();
        return result;
    }

    private Point CenterOf(Box box) => _application.TransientGeometry.CreatePoint(
        (box.MinPoint.X + box.MaxPoint.X) / 2.0,
        (box.MinPoint.Y + box.MaxPoint.Y) / 2.0,
        (box.MinPoint.Z + box.MaxPoint.Z) / 2.0);

    private static double Dot(Vector first, Vector second) =>
        first.X * second.X + first.Y * second.Y + first.Z * second.Z;

    private sealed class DirectionGroup(Vector direction)
    {
        public Vector Direction { get; } = direction;
        public double Score { get; set; }
        public double LongestEdge { get; set; }
        public int EdgeCount { get; set; }
    }
}
