using Inventor;
using TubeJoint.AddIn.Models;
using Point = Inventor.Point;

namespace TubeJoint.AddIn.Services;

/// <summary>
/// Native placement manipulators in the selected female-face coordinate system.
/// Their values are consumed by preview, repository and final geometry.
/// </summary>
internal sealed class JointManipulatorService : IDisposable
{
    private readonly Inventor.Application _application;
    private InteractionEvents? _interaction;
    private TriadEvents? _triad;
    private Point? _startOrigin;
    private Vector? _xAxis;
    private Vector? _yAxis;
    private double _baseXmm;
    private double _baseYmm;
    private Action<double, double>? _moved;
    private Action? _ended;
    private TriadEventsSink_OnMoveEventHandler? _moveHandler;
    private TriadEventsSink_OnEndMoveEventHandler? _endMoveHandler;
    private TriadEventsSink_OnTerminateEventHandler? _terminateHandler;
    private int _generation;

    public JointManipulatorService(Inventor.Application application) => _application = application;

    public bool Active => _interaction is not null;

    public void StartHole(
        JointPairSelection selection,
        double jointOffsetYmm,
        double currentXmm,
        double currentYmm,
        Action<double, double> moved,
        Action ended)
        => Start(
            selection,
            "TubeJointHoleManipulator",
            "Переместите отверстие в плоскости выбранной грани",
            currentXmm,
            jointOffsetYmm + currentYmm,
            TriadSegmentEnum.kXAxisTranslationSegment |
            TriadSegmentEnum.kYAxisTranslationSegment |
            TriadSegmentEnum.kXYPlaneTranslationSegment,
            (xMm, combinedYmm) => moved(xMm, combinedYmm - jointOffsetYmm),
            ended);

    public void StartJoint(
        JointPairSelection selection,
        double currentOffsetMm,
        Action<double> moved)
        => Start(
            selection,
            "TubeJointCommonManipulator",
            "Сместите шипы и пазы вдоль выбранной грани",
            0.0,
            currentOffsetMm,
            TriadSegmentEnum.kYAxisTranslationSegment,
            (_, yMm) => moved(yMm));

    private void Start(
        JointPairSelection selection,
        string name,
        string statusBarText,
        double currentXmm,
        double currentYmm,
        TriadSegmentEnum degreesOfFreedom,
        Action<double, double> moved,
        Action? ended = null)
    {
        Stop();
        var (xAxis, yAxis) = TemplateProfileGeometryBuilder.BuildFemaleAxes(selection);
        xAxis.Normalize();
        yAxis.Normalize();
        var normal = xAxis.CrossProduct(yAxis);
        normal.Normalize();

        var origin = Offset(selection.JointPointAssembly, xAxis, currentXmm / 10.0);
        origin = Offset(origin, yAxis, currentYmm / 10.0);
        var matrix = _application.TransientGeometry.CreateMatrix();
        matrix.SetCoordinateSystem(origin, xAxis, yAxis, normal);

        _startOrigin = origin.Copy();
        _xAxis = xAxis.Copy();
        _yAxis = yAxis.Copy();
        _baseXmm = currentXmm;
        _baseYmm = currentYmm;
        _moved = moved;
        _ended = ended;
        var generation = ++_generation;

        _interaction = _application.CommandManager.CreateInteractionEvents();
        _interaction.Name = name;
        _interaction.SelectionActive = false;
        _interaction.InteractionDisabled = true;
        _interaction.StatusBarText = statusBarText;
        _triad = _interaction.TriadEvents;
        _triad.GlobalTransform = matrix;
        _triad.DegreesOfFreedom = degreesOfFreedom;
        _triad.Repeat = true;
        _triad.Enabled = true;
        _moveHandler = (
            TriadSegmentEnum selectedSegment,
            ShiftStateEnum shiftKeys,
            Matrix coordinateSystem,
            NameValueMap context,
            out HandlingCodeEnum handlingCode) =>
            TriadOnMove(generation, coordinateSystem, out handlingCode);
        _endMoveHandler = (
            TriadSegmentEnum selectedSegment,
            ShiftStateEnum shiftKeys,
            Matrix coordinateSystem,
            NameValueMap context,
            out HandlingCodeEnum handlingCode) =>
            TriadOnEndMove(generation, coordinateSystem, out handlingCode);
        _terminateHandler = (
            bool aborted,
            NameValueMap context,
            out HandlingCodeEnum handlingCode) =>
            TriadOnTerminate(generation, out handlingCode);
        _triad.OnMove += _moveHandler;
        _triad.OnEndMove += _endMoveHandler;
        _triad.OnTerminate += _terminateHandler;
        _interaction.Start();
    }

    public void Stop()
    {
        ++_generation;
        var triad = _triad;
        _triad = null;
        var moveHandler = _moveHandler;
        var endMoveHandler = _endMoveHandler;
        var terminateHandler = _terminateHandler;
        _moveHandler = null;
        _endMoveHandler = null;
        _terminateHandler = null;
        if (triad is not null)
        {
            if (moveHandler is not null)
                try { triad.OnMove -= moveHandler; } catch { }
            if (endMoveHandler is not null)
                try { triad.OnEndMove -= endMoveHandler; } catch { }
            if (terminateHandler is not null)
                try { triad.OnTerminate -= terminateHandler; } catch { }
            try { triad.Enabled = false; } catch { }
        }
        var interaction = _interaction;
        _interaction = null;
        try { interaction?.Stop(); } catch { }
        _startOrigin = null;
        _xAxis = null;
        _yAxis = null;
        _moved = null;
        _ended = null;
    }

    public void Dispose() => Stop();

    private void TriadOnMove(int generation, Matrix coordinateSystem, out HandlingCodeEnum handlingCode)
    {
        handlingCode = HandlingCodeEnum.kEventNotHandled;
        ApplyCoordinateSystem(generation, coordinateSystem);
    }

    private void ApplyCoordinateSystem(int generation, Matrix coordinateSystem)
    {
        if (generation != _generation)
            return;
        if (_startOrigin is null || _xAxis is null || _yAxis is null || _moved is null)
            return;
        coordinateSystem.GetCoordinateSystem(
            out var origin, out _, out _, out _);
        var delta = _startOrigin.VectorTo(origin);
        var xMm = _baseXmm + delta.DotProduct(_xAxis) * 10.0;
        var yMm = _baseYmm + delta.DotProduct(_yAxis) * 10.0;
        _moved(xMm, yMm);
    }

    private void TriadOnEndMove(
        int generation,
        Matrix coordinateSystem,
        out HandlingCodeEnum handlingCode)
    {
        handlingCode = HandlingCodeEnum.kEventNotHandled;
        if (generation != _generation)
            return;
        // Inventor can emit OnEndMove without one final OnMove for a very short drag.
        ApplyCoordinateSystem(generation, coordinateSystem);
        var ended = _ended;
        _ended = null;
        ended?.Invoke();
    }

    private void TriadOnTerminate(int generation, out HandlingCodeEnum handlingCode)
    {
        handlingCode = HandlingCodeEnum.kEventNotHandled;
        if (generation != _generation)
            return;
        var ended = _ended;
        _ended = null;
        ended?.Invoke();
    }

    private Point Offset(Point source, Vector direction, double distance) =>
        _application.TransientGeometry.CreatePoint(
            source.X + direction.X * distance,
            source.Y + direction.Y * distance,
            source.Z + direction.Z * distance);
}
