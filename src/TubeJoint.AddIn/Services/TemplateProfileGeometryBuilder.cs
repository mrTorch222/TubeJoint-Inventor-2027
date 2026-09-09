using Inventor;
using System.Runtime.InteropServices;
using TubeJoint.AddIn.Models;
using Point = Inventor.Point;

namespace TubeJoint.AddIn.Services;

/// <summary>
/// Builds real part features from the three named sketches in the editable IPT template.
/// All assembly-space directions are transformed into each occurrence's native part space,
/// so rotating a frame member does not rotate or displace the generated joint.
/// </summary>
internal sealed class TemplateProfileGeometryBuilder : IJointGeometryBuilder
{
    // Fixed 0.2 mm overlap only guarantees that a one-sided Cut crosses the wall.
    // TJ_Clearance changes the 2D slot contour and must never change cut depth.
    private const double CutOverlapCm = 0.02;
    // Preserve this much of the profile's negative-X root inside the original tube.
    // It gives the subsequent Join a real volume overlap instead of edge contact.
    private const double RootAttachmentOverlapCm = 0.02;
    private readonly Inventor.Application _application;
    private readonly JointTemplateService _templates;

    public TemplateProfileGeometryBuilder(Inventor.Application application, JointTemplateService templates)
    {
        _application = application;
        _templates = templates;
    }

    public void CreateOrUpdate(AssemblyDocument assembly, JointPairSelection selection, JointPairRecord record)
    {
        var templatePath = string.IsNullOrWhiteSpace(record.TemplatePath)
            ? _templates.EnsureDefaultTemplate()
            : _templates.EnsureTemplate(record.TemplatePath);
        PartDocument? template = null;
        var openedHere = false;
        foreach (Document openDocument in _application.Documents)
        {
            if (!string.Equals(openDocument.FullFileName, templatePath, StringComparison.OrdinalIgnoreCase))
                continue;
            template = (PartDocument)openDocument;
            break;
        }
        if (template is null)
        {
            template = (PartDocument)_application.Documents.Open(templatePath, false);
            openedHere = true;
        }
        try
        {
            using var applied = RunStage("применение параметров шаблона", () =>
                _templates.TemporarilyApplyParameters(template, record.Parameters));
            var maleSource = template.ComponentDefinition.Sketches[JointTemplateService.MaleSketchName];
            var femaleSource = template.ComponentDefinition.Sketches[JointTemplateService.FemaleSketchName];
            var ventSource = template.ComponentDefinition.Sketches[JointTemplateService.CenterVentSketchName];
            var maleDocument = OccurrenceSelectionService.GetPartDocument(selection.Male.Occurrence);
            var femaleDocument = OccurrenceSelectionService.GetPartDocument(selection.Female.Occurrence);
            // A/B must have one root envelope. Trim/Notch can report different
            // clipped end gaps on the two faces; using either value independently
            // makes one tenon look longitudinally shifted even though its anchor is aligned.
            var pairedRootGapMm = Math.Max(selection.SideAGapMm, selection.SideBGapMm);
            var firstSideName = selection.WallPair == JointWallPair.AB ? "A" : "C";
            var secondSideName = selection.WallPair == JointWallPair.AB ? "B" : "D";

            if (record.Parameters.SideMode is JointSideMode.SideA or JointSideMode.Both)
                RunStage($"создание шипа {firstSideName}", () => CreateMaleFeature(
                    maleDocument, selection, selection.Male,
                    OffsetAssemblyPoint(selection.SideAAnchorAssembly, selection.MaleProfileYAxis,
                        (record.Parameters.JointOffsetYMm + record.Parameters.SideAOffsetMm) / 10.0),
                    maleSource, record, firstSideName, pairedRootGapMm));
            if (record.Parameters.SideMode is JointSideMode.SideB or JointSideMode.Both)
                RunStage($"создание шипа {secondSideName}", () => CreateMaleFeature(
                    maleDocument, selection, selection.MaleOpposite,
                    OffsetAssemblyPoint(selection.SideBAnchorAssembly, selection.MaleProfileYAxis,
                        (record.Parameters.JointOffsetYMm + record.Parameters.SideBOffsetMm) / 10.0),
                    maleSource, record, secondSideName, pairedRootGapMm));
            RunStage("обновление детали шипа", () => maleDocument.Update2(true));

            RunStage("создание пазов и дополнительного выреза", () =>
                CreateFemaleFeature(femaleDocument, selection, femaleSource, ventSource, record));
            RunStage("обновление детали пазов", () => femaleDocument.Update2(true));
        }
        finally
        {
            if (openedHere) template.Close(false);
        }
    }

    private void CreateMaleFeature(
        PartDocument document,
        JointPairSelection selection,
        JointFaceSelection targetFace,
        Point anchorAssembly,
        PlanarSketch source,
        JointPairRecord record,
        string sideName,
        double pairedRootGapMm)
    {
        var definition = document.ComponentDefinition;
        var sketch = definition.Sketches.Add(targetFace.NativeFace, false);
        sketch.Name = $"TJ_MALE_{sideName}_{record.Id[..8]}";

        var anchor = ToNativePoint(targetFace.Occurrence, anchorAssembly);
        var xAxis = ToNativeVector(targetFace.Occurrence, selection.TenonDirectionAssembly);
        var yAxis = ToNativeVector(targetFace.Occurrence, selection.MaleProfileYAxis);
        var basis = BuildSketchBasis(sketch, anchor, xAxis, yAxis);
        RunStage($"копирование профиля шипа {sideName}", () => CopyMaleProfile(
            source,
            sketch,
            basis,
            pairedRootGapMm / 10.0));

        var profile = RunStage($"создание замкнутого профиля шипа {sideName}", () =>
            sketch.Profiles.AddForSolid());
        var firstSide = sideName is "A" or "C";
        var actualRootGapCm = (firstSide ? selection.SideAGapMm : selection.SideBGapMm) / 10.0;
        var distance = Math.Max(record.Parameters.MaleWallMm / 10.0, 0.01);
        var slotCenterAssembly = firstSide
            ? selection.SideASlotCenterAssembly
            : selection.SideBSlotCenterAssembly;
        slotCenterAssembly = OffsetAssemblyPoint(
            slotCenterAssembly,
            selection.MaleProfileYAxis,
            (record.Parameters.JointOffsetYMm +
             (firstSide ? record.Parameters.SideAOffsetMm : record.Parameters.SideBOffsetMm)) / 10.0);
        var inwardAssembly = anchorAssembly.VectorTo(slotCenterAssembly);
        PartFeatureExtentDirectionEnum? preferredDirection = null;
        if (inwardAssembly.Length >= 1e-8)
        {
            var inwardNative = ToNativeVector(targetFace.Occurrence, inwardAssembly);
            var sketchPlane = sketch.PlanarEntityGeometry as Plane
                ?? throw new InvalidOperationException("Не удалось получить плоскость эскиза шипа.");
            preferredDirection = sketchPlane.Normal.AsVector().DotProduct(inwardNative) >= 0
                ? PartFeatureExtentDirectionEnum.kPositiveExtentDirection
                : PartFeatureExtentDirectionEnum.kNegativeExtentDirection;
        }

        // TJ_MALE_PROFILE is the desired final silhouette, not merely material to
        // add. Clear the original wall inside its bounding replacement window first,
        // leaving a narrow negative-X attachment band, then Join the exact native
        // profile back. This preserves arbitrary concave lines/arcs/splines without
        // classifying or redrawing special "relief" geometry.
        var replacementProfile = RunStage($"подготовка окна замены шипа {sideName}", () =>
            CreateMaleReplacementProfile(
                definition, targetFace.NativeFace, source, anchor, xAxis, yAxis,
                actualRootGapCm, sideName, record.Id[..8]));

        if (replacementProfile is not null)
            RunStage($"операция Cut окна замены шипа {sideName}", () => AddExtrudeTryingBothDirections(
                definition.Features.ExtrudeFeatures,
                replacementProfile,
                distance + CutOverlapCm,
                PartFeatureOperationEnum.kCutOperation,
                $"TJ_TENON_REPLACE_CUT_{sideName}_{record.Id[..8]}",
                preferredDirection));

        RunStage($"операция Join шипа {sideName}", () => AddExtrudeTryingBothDirections(
            definition.Features.ExtrudeFeatures,
            profile,
            distance,
            PartFeatureOperationEnum.kJoinOperation,
            $"TJ_TENON_{sideName}_{record.Id[..8]}",
            preferredDirection));
    }

    private Profile? CreateMaleReplacementProfile(
        PartComponentDefinition definition,
        object targetFace,
        PlanarSketch source,
        Point nativeAnchor,
        Vector nativeXAxis,
        Vector nativeYAxis,
        double actualRootGapCm,
        string sideName,
        string recordId)
    {
        var shape = TemplateProfileSampler.SampleSketch(source);
        var cutStartX = shape.MinX + RootAttachmentOverlapCm;
        var existingWallEndX = -Math.Max(0.0, actualRootGapCm);
        if (cutStartX >= existingWallEndX - 1e-6)
            return null;

        var cutEndX = Math.Max(0.0, shape.MaxX) + CutOverlapCm;
        if (cutEndX - cutStartX <= 1e-6 || shape.Height <= 1e-6)
            throw new InvalidOperationException(
                $"Неверное окно замены по TJ_MALE_PROFILE: " +
                $"X={cutStartX:F4}..{cutEndX:F4} см, Y={shape.MinY:F4}..{shape.MaxY:F4} см.");

        var sketch = definition.Sketches.Add(targetFace, false);
        sketch.Name = $"TJ_TENON_REPLACE_{sideName}_{recordId}";
        var basis = BuildSketchBasis(sketch, nativeAnchor, nativeXAxis, nativeYAxis);
        sketch.SketchLines.AddAsTwoPointRectangle(
            basis.Map(cutStartX, shape.MinY),
            basis.Map(cutEndX, shape.MaxY));

        try
        {
            sketch.UpdateProfiles();
            return sketch.Profiles.AddForSolid(false);
        }
        catch (Exception exception)
        {
            throw new InvalidOperationException(
                $"Не удалось создать прямоугольное окно замены из габаритов TJ_MALE_PROFILE: " +
                $"X={cutStartX:F4}..{cutEndX:F4} см, " +
                $"Y={shape.MinY:F4}..{shape.MaxY:F4} см, зазор={actualRootGapCm:F4} см. " +
                exception.Message,
                exception);
        }
    }

    private void CreateFemaleFeature(
        PartDocument document,
        JointPairSelection selection,
        PlanarSketch source,
        PlanarSketch ventSource,
        JointPairRecord record)
    {
        var (femaleXAxisAssembly, femaleYAxisAssembly) = BuildFemaleAxes(selection);
        var xAxis = ToNativeVector(selection.Female.Occurrence, femaleXAxisAssembly);
        var yAxis = ToNativeVector(selection.Female.Occurrence, femaleYAxisAssembly);
        var distance = Math.Max(record.Parameters.FemaleWallMm / 10.0 + CutOverlapCm, CutOverlapCm);

        if (record.Parameters.SideMode is JointSideMode.SideA or JointSideMode.Both)
        {
            var anchorAAssembly = OffsetAssemblyPoint(
                selection.SideASlotCenterAssembly, selection.MaleProfileYAxis,
                (record.Parameters.JointOffsetYMm + record.Parameters.SideAOffsetMm) / 10.0);
            CreateFemaleSlotFeature(
                document, selection, source, record, anchorAAssembly,
                xAxis, yAxis, distance, selection.WallPair == JointWallPair.AB ? "A" : "C");
        }
        if (record.Parameters.SideMode is JointSideMode.SideB or JointSideMode.Both)
        {
            var anchorBAssembly = OffsetAssemblyPoint(
                selection.SideBSlotCenterAssembly, selection.MaleProfileYAxis,
                (record.Parameters.JointOffsetYMm + record.Parameters.SideBOffsetMm) / 10.0);
            CreateFemaleSlotFeature(
                document, selection, source, record, anchorBAssembly,
                xAxis, yAxis, distance, selection.WallPair == JointWallPair.AB ? "B" : "D");
        }
        var ventProfile = record.Parameters.CreateCenterVent
            ? CreateVentProfile(
                document, selection, ventSource, record,
                femaleXAxisAssembly, femaleYAxisAssembly, xAxis, yAxis)
            : null;

        if (ventProfile is not null)
            RunStage("операция Cut отдельного выреза на глубину стенки", () =>
                AddExtrudeTryingBothDirections(
                    document.ComponentDefinition.Features.ExtrudeFeatures,
                    ventProfile,
                    distance,
                    PartFeatureOperationEnum.kCutOperation,
                    "TJ_VENT_CUT_" + record.Id[..8]));
    }

    private void CreateFemaleSlotFeature(
        PartDocument document,
        JointPairSelection selection,
        PlanarSketch source,
        JointPairRecord record,
        Point anchorAssembly,
        Vector xAxis,
        Vector yAxis,
        double distance,
        string sideName)
    {
        var definition = document.ComponentDefinition;
        var sketch = definition.Sketches.Add(selection.Female.NativeFace, false);
        sketch.Name = $"TJ_FEMALE_{sideName}_{record.Id[..8]}";
        var anchor = ToNativePoint(selection.Female.Occurrence, anchorAssembly);
        RunStage($"копирование паза {sideName}", () =>
            CopySolvedProfile(source, sketch, BuildSketchBasis(sketch, anchor, xAxis, yAxis)));
        var profile = RunStage($"создание замкнутого профиля паза {sideName}", () =>
            CreateSingleClosedProfile(sketch));
        RunStage($"операция Cut паза {sideName}", () => AddExtrudeTryingBothDirections(
            definition.Features.ExtrudeFeatures,
            profile,
            distance,
            PartFeatureOperationEnum.kCutOperation,
            $"TJ_SLOT_{sideName}_{record.Id[..8]}"));
    }

    private static Profile CreateSingleClosedProfile(PlanarSketch sketch)
    {
        var workingCurves = new List<SketchEntity>();
        foreach (SketchEntity entity in sketch.SketchEntities)
        {
            if (!entity.Construction && !entity.Reference && IsProfileCurve(entity))
                workingCurves.Add(entity);
        }
        var diagnostics = DescribeSketch(sketch, workingCurves.Count);

        Exception? solidFailure;
        try
        {
            sketch.UpdateProfiles();
            var requested = (Inventor.Application)sketch.Application;
            var paths = requested.TransientObjects.CreateObjectCollection();
            foreach (var curve in workingCurves)
                paths.Add(curve);
            return sketch.Profiles.AddForSolid(false, paths);
        }
        catch (Exception exception)
        {
            solidFailure = exception;
        }

        var chainErrors = new List<string>();
        for (var index = 0; index < workingCurves.Count; index++)
        {
            Profile? candidate = null;
            try
            {
                candidate = sketch.Profiles.AddForSurface(workingCurves[index]);
                var hasPath = false;
                var allClosed = true;
                var pathCount = 0;
                foreach (ProfilePath path in candidate)
                {
                    hasPath = true;
                    pathCount++;
                    allClosed &= path.Closed;
                }
                if (hasPath && allClosed)
                {
                    foreach (ProfilePath path in candidate)
                        path.AddsMaterial = true;
                    var accepted = candidate;
                    candidate = null;
                    return accepted;
                }
                chainErrors.Add($"{CurveKind(workingCurves[index])}[{index + 1}]: paths={pathCount}, closed={allClosed}");
            }
            catch (Exception exception)
            {
                chainErrors.Add($"{CurveKind(workingCurves[index])}[{index + 1}]: {exception.Message}");
            }
            finally
            {
                try { candidate?.Delete(); }
                catch { }
            }
        }

        var chainSummary = chainErrors.Count == 0
            ? "нет пригодных рабочих кривых"
            : string.Join(" | ", chainErrors.Take(6));
        throw new InvalidOperationException(
            $"Inventor отклонил явно заданный замкнутый solid-профиль. " +
            $"Solid: {solidFailure.Message}. Эскиз: {diagnostics}. " +
            $"Проверка связанных цепей: {chainSummary}",
            solidFailure);
    }

    private static bool IsProfileCurve(SketchEntity entity) => entity is
        SketchLine or SketchArc or SketchCircle or SketchEllipse or SketchEllipticalArc or
        SketchControlPointSpline or SketchSpline or SketchFixedSpline;

    private static string CurveKind(SketchEntity entity) => entity switch
    {
        SketchLine => "Line",
        SketchArc => "Arc",
        SketchCircle => "Circle",
        SketchEllipse => "Ellipse",
        SketchEllipticalArc => "EllipticalArc",
        SketchControlPointSpline => "CVSpline",
        SketchSpline => "InterpolationSpline",
        SketchFixedSpline => "FixedSpline",
        _ => entity.Type.ToString()
    };

    private static string DescribeSketch(PlanarSketch sketch, int workingCurveCount)
    {
        var kinds = new Dictionary<string, int>(StringComparer.Ordinal);
        var construction = 0;
        var reference = 0;
        foreach (SketchEntity entity in sketch.SketchEntities)
        {
            if (entity.Construction) construction++;
            if (entity.Reference) reference++;
            var kind = CurveKind(entity);
            kinds[kind] = kinds.GetValueOrDefault(kind) + 1;
        }
        return $"name={sketch.Name}, entities={sketch.SketchEntities.Count}, " +
               $"workingCurves={workingCurveCount}, construction={construction}, reference={reference}, " +
               $"types=[{string.Join(",", kinds.Select(pair => pair.Key + "=" + pair.Value))}]";
    }

    private Profile CreateVentProfile(
        PartDocument document,
        JointPairSelection selection,
        PlanarSketch ventSource,
        JointPairRecord record,
        Vector femaleXAxisAssembly,
        Vector femaleYAxisAssembly,
        Vector xAxis,
        Vector yAxis)
    {
        var definition = document.ComponentDefinition;
        var sketch = definition.Sketches.Add(selection.Female.NativeFace, false);
        sketch.Name = "TJ_VENT_" + record.Id[..8];
        var ventAnchorAssembly = OffsetAssemblyPoint(
            selection.JointPointAssembly, femaleXAxisAssembly,
            record.Parameters.HoleOffsetXMm / 10.0);
        ventAnchorAssembly = OffsetAssemblyPoint(
            ventAnchorAssembly, femaleYAxisAssembly,
            (record.Parameters.JointOffsetYMm + record.Parameters.HoleOffsetYMm) / 10.0);
        var ventAnchor = ToNativePoint(selection.Female.Occurrence, ventAnchorAssembly);
        var ventBasis = BuildSketchBasis(sketch, ventAnchor, xAxis, yAxis);
        RunStage("копирование отдельного выреза", () =>
            CopySolvedProfile(
                ventSource, sketch, ventBasis, record.Parameters.VentScalePercent / 100.0));
        return RunStage("создание профиля отдельного выреза", () =>
            CreateSingleClosedProfile(sketch));
    }

    private static void AddExtrudeTryingBothDirections(
        ExtrudeFeatures extrudes,
        Profile profile,
        double distance,
        PartFeatureOperationEnum operation,
        string name,
        PartFeatureExtentDirectionEnum? preferredDirection = null)
    {
        Exception? first = null;
        var firstDirection = preferredDirection ?? PartFeatureExtentDirectionEnum.kNegativeExtentDirection;
        var secondDirection = firstDirection == PartFeatureExtentDirectionEnum.kNegativeExtentDirection
            ? PartFeatureExtentDirectionEnum.kPositiveExtentDirection
            : PartFeatureExtentDirectionEnum.kNegativeExtentDirection;
        foreach (var direction in new[] { firstDirection, secondDirection })
        {
            try
            {
                var definition = extrudes.CreateExtrudeDefinition(profile, operation);
                definition.SetDistanceExtent(distance, direction);
                var feature = extrudes.Add(definition);
                feature.Name = name;
                return;
            }
            catch (Exception exception)
            {
                first ??= exception;
            }
        }

        throw new InvalidOperationException(
            "Inventor не смог построить операцию из профиля шаблона ни в одном направлении. " +
            $"Первая ошибка: {first?.Message}", first);
    }

    private void CopyMaleProfile(
        PlanarSketch source,
        PlanarSketch target,
        SketchBasis basis,
        double forwardGap)
    {
        var shape = TemplateProfileSampler.SampleSketch(source);
        if (shape.MaxX <= 1e-9 || shape.Height <= 1e-9)
            throw new InvalidOperationException("Эскиз TJ_MALE_PROFILE имеет неверный размер.");
        if (forwardGap > 1e-6 && shape.MinX >= -1e-9)
            throw new InvalidOperationException(
                "Между торцом и пазом есть зазор, но TJ_MALE_PROFILE не заходит левее X=0. " +
                "Продлите корень профиля в отрицательное направление X.");

        if (-shape.MinX + 1e-6 < forwardGap)
            throw new InvalidOperationException(
                "TJ_MALE_PROFILE не перекрывает зазор Trim/Notch. " +
                "Увеличьте параметрическую отрицательную часть X у корня шипа.");
        CopyExactProfile(source, target, basis);
    }

    private void CopySolvedProfile(
        PlanarSketch source,
        PlanarSketch target,
        SketchBasis basis,
        double scale = 1.0)
    {
        var shape = TemplateProfileSampler.SampleSketch(source);
        if (shape.Width <= 1e-9 || shape.Height <= 1e-9)
            throw new InvalidOperationException("Эскиз TJ_FEMALE_PROFILE имеет неверный размер.");

        scale = Math.Clamp(scale, 0.1, 5.0);
        CopyExactProfile(source, target, basis, scale);
    }

    private void CopyExactProfile(
        PlanarSketch source,
        PlanarSketch target,
        SketchBasis basis,
        double scale = 1.0)
    {
        scale = Math.Clamp(scale, 0.1, 5.0);

        // Copy the complete connected contour through Inventor itself.  Rebuilding a
        // mixed line/arc/spline loop curve-by-curve loses the shared topological
        // endpoints of open CV/interpolation splines even when their coordinates are
        // identical.  The resulting sketch looks closed but Profiles.AddForSolid
        // rejects it with E_INVALIDARG.  A temporary sketch block lets Inventor retain
        // the native curve types and connectivity while applying our full affine map.
        CopyNativeConnectedContour(source, target, basis, scale);
        return;

#pragma warning disable CS0162 // Kept as a compatibility reference for older Inventor versions.
        Profile profile;
        try { profile = source.Profiles.AddForSolid(); }
        catch
        {
            CopyConnectedSketchGeometry(source, target, basis, scale);
            return;
        }
        ProfilePath? selected = null;
        foreach (ProfilePath path in profile)
        {
            if (path.TextBoxPath || !path.Closed || !path.AddsMaterial) continue;
            if (selected is not null)
                throw new InvalidOperationException(
                    $"Эскиз '{source.Name}' должен содержать ровно один внешний закрытый контур.");
            selected = path;
        }
        if (selected is null)
        {
            CopyConnectedSketchGeometry(source, target, basis, scale);
            return;
        }

        scale = Math.Clamp(scale, 0.1, 5.0);
        var entities = selected.Cast<ProfileEntity>().ToList();
        if (entities.Count > 1 && entities.All(entity =>
                entity.Curve is LineSegment2d or Arc2d or BSplineCurve2d))
        {
            CopyConnectedProfile(entities, source.Name, target, basis, scale);
            return;
        }

        foreach (ProfileEntity entity in selected)
        {
            switch (entity.Curve)
            {
                case LineSegment2d line:
                    target.SketchLines.AddByTwoPoints(
                        MapPoint(basis, line.StartPoint, scale),
                        MapPoint(basis, line.EndPoint, scale));
                    break;
                case Arc2d arc:
                {
                    var center = MapPoint(basis, arc.Center, scale);
                    var start = MapPoint(basis, arc.StartPoint, scale);
                    var startAngle = Math.Atan2(start.Y - center.Y, start.X - center.X);
                    var sweep = arc.SweepAngle * basis.Handedness;
                    target.SketchArcs.AddByCenterStartSweepAngle(
                        center, arc.Radius * scale, startAngle, sweep);
                    break;
                }
                case Circle2d circle:
                    target.SketchCircles.AddByCenterRadius(
                        MapPoint(basis, circle.Center, scale), circle.Radius * scale);
                    break;
                case EllipticalArc2d arc:
                    target.SketchEllipticalArcs.Add(
                        MapPoint(basis, arc.Center, scale),
                        MapUnitVector(basis, arc.MajorAxis),
                        arc.MajorRadius * scale,
                        arc.MinorRadius * scale,
                        basis.Handedness > 0 ? arc.StartAngle : -arc.StartAngle,
                        arc.SweepAngle * basis.Handedness);
                    break;
                case EllipseFull2d ellipse:
                {
                    var major = ellipse.MajorAxisVector;
                    var majorRadius = major.Length * scale;
                    var direction = _application.TransientGeometry.CreateUnitVector2d(
                        basis.Ux * major.X + basis.Vx * major.Y,
                        basis.Uy * major.X + basis.Vy * major.Y);
                    target.SketchEllipses.Add(
                        MapPoint(basis, ellipse.Center, scale),
                        direction,
                        majorRadius,
                        majorRadius * ellipse.MinorMajorRatio);
                    break;
                }
                case BSplineCurve2d spline:
                {
                    if (entity.SketchEntity is SketchSpline interpolationSpline)
                    {
                        CopyInterpolationSpline(
                            interpolationSpline, target, basis, scale, source.Name);
                        break;
                    }

                    // A closed Inventor Control Vertex Spline must remain a
                    // SketchControlPointSpline.  Adding its BSplineCurve2d snapshot
                    // as SketchFixedSpline succeeds, but Inventor then treats the
                    // coincident endpoints as an open topological curve and
                    // Profiles.AddForSolid fails with E_INVALIDARG.
                    if (entity.SketchEntity is SketchControlPointSpline controlSpline)
                    {
                        CopyControlPointSpline(controlSpline, target, basis, scale, source.Name);
                        break;
                    }

                    BSplineCurve2d transformed;
                    try
                    {
                        transformed = TransformSpline(spline, basis, scale);
                    }
                    catch (Exception exception)
                    {
                        throw new InvalidOperationException(
                            $"Не удалось преобразовать NURBS из '{source.Name}': {exception.Message}",
                            exception);
                    }
                    try
                    {
                        // Autodesk's API sample deliberately omits both optional
                        // Variant endpoints. Passing Type.Missing is rejected for
                        // this closed non-periodic CV spline with E_INVALIDARG.
                        target.SketchFixedSplines.Add(transformed);
                    }
                    catch (Exception exception)
                    {
                        throw new InvalidOperationException(
                            $"Не удалось добавить одну SketchFixedSpline из '{source.Name}': " +
                            exception.Message,
                            exception);
                    }
                    break;
                }
                default:
                    throw new InvalidOperationException(
                        $"Финальное копирование эскиза '{source.Name}' не поддерживает " +
                        $"Inventor CurveType={entity.CurveType} без преобразования в отрезки.");
            }
        }
#pragma warning restore CS0162
    }

    private void CopyNativeConnectedContour(
        PlanarSketch source,
        PlanarSketch target,
        SketchBasis basis,
        double scale)
    {
        var unused = new List<(SketchEntity Entity, object Curve)>();
        foreach (SketchEntity entity in source.SketchEntities)
        {
            if (entity.Construction || entity.Reference)
                continue;

            object? curve = entity switch
            {
                SketchPoint => null,
                SketchLine line => line.Geometry,
                SketchArc arc => arc.Geometry,
                SketchCircle circle => circle.Geometry,
                SketchEllipse ellipse => ellipse.Geometry,
                SketchEllipticalArc arc => arc.Geometry,
                SketchControlPointSpline spline => spline.Geometry,
                SketchSpline spline => spline.Geometry,
                SketchFixedSpline spline => spline.Geometry,
                _ => throw new InvalidOperationException(
                    $"Нативное копирование '{source.Name}' не поддерживает {entity.Type}.")
            };
            if (curve is not null)
                unused.Add((entity, curve));
        }

        if (unused.Count == 0)
            throw new InvalidOperationException($"В эскизе '{source.Name}' нет рабочих кривых.");

        // Full circles and ellipses are already closed single-entity contours.
        List<(SketchEntity Entity, object Curve)> ordered;
        if (unused.Count == 1 && unused[0].Curve is Circle2d or EllipseFull2d)
        {
            ordered = unused;
        }
        else
        {
            ordered = new List<(SketchEntity Entity, object Curve)> { unused[0] };
            var loopStart = CurveStartPoint(unused[0].Curve);
            var current = CurveEndPoint(unused[0].Curve);
            unused.RemoveAt(0);
            while (unused.Count > 0)
            {
                var found = -1;
                for (var index = 0; index < unused.Count; index++)
                {
                    if (PointsCoincide(current, CurveStartPoint(unused[index].Curve)))
                    {
                        found = index;
                        current = CurveEndPoint(unused[index].Curve);
                        break;
                    }
                    if (PointsCoincide(current, CurveEndPoint(unused[index].Curve)))
                    {
                        found = index;
                        current = CurveStartPoint(unused[index].Curve);
                        break;
                    }
                }
                if (found < 0)
                    throw new InvalidOperationException(
                        $"Рабочие кривые '{source.Name}' не образуют один связанный контур.");
                ordered.Add(unused[found]);
                unused.RemoveAt(found);
            }
            if (!PointsCoincide(current, loopStart))
                throw new InvalidOperationException($"Рабочий контур '{source.Name}' не замкнут.");
        }

        var sourceObjects = _application.TransientObjects.CreateObjectCollection();
        foreach (var item in ordered)
            sourceObjects.Add(item.Entity);

        var firstNewIndex = target.SketchEntities.Count + 1;
        try
        {
            source.CopyEntitiesTo(sourceObjects, (Sketch)(object)target);
        }
        catch (Exception exception)
        {
            throw new InvalidOperationException(
                $"Inventor не смог нативно скопировать смешанный контур '{source.Name}': {exception.Message}",
                exception);
        }

        var copiedObjects = _application.TransientObjects.CreateObjectCollection();
        for (var index = firstNewIndex; index <= target.SketchEntities.Count; index++)
            copiedObjects.Add(target.SketchEntities[index]);

        // CopyEntitiesTo also returns Inventor-owned dependent sketch entities for
        // CV/interpolation splines (control/fit topology and handles).  Their count
        // is therefore intentionally larger than the number of source profile
        // curves.  They must travel through the temporary block together with the
        // visible boundary or the spline connections are lost again.
        if (copiedObjects.Count < ordered.Count)
            throw new InvalidOperationException(
                $"Inventor скопировал не все элементы '{source.Name}': " +
                $"ожидалось не меньше {ordered.Count}, получено {copiedObjects.Count}.");

        SketchBlock? block = null;
        SketchBlockDefinition? definition = null;
        try
        {
            block = target.SketchBlocks.Add(
                copiedObjects,
                "TJ_NATIVE_" + Guid.NewGuid().ToString("N"),
                _application.TransientGeometry.CreatePoint2d(0, 0));
            definition = block.Definition;

            var transform = _application.TransientGeometry.CreateMatrix2d();
            transform.SetCoordinateSystem(
                basis.Origin,
                _application.TransientGeometry.CreateVector2d(basis.Ux * scale, basis.Uy * scale),
                _application.TransientGeometry.CreateVector2d(basis.Vx * scale, basis.Vy * scale));
            block.Transformation = transform;
            block.Explode();
            block = null;

            StitchCopiedContour(target, source.Name, ordered.Count);

            // Explode retains the transformed native entities.  The generated block
            // definition is only a transport container and must not clutter the part.
            try { definition.Delete(); }
            catch { /* An unused definition is harmless if Inventor keeps it referenced. */ }
        }
        catch (Exception exception)
        {
            try { block?.Delete(); }
            catch { /* The surrounding Inventor transaction will roll back the sketch. */ }
            throw new InvalidOperationException(
                $"Inventor не смог преобразовать нативную копию '{source.Name}': {exception.Message}",
                exception);
        }
    }

    private void StitchCopiedContour(PlanarSketch sketch, string sourceName, int expectedCurveCount)
    {
        var unused = new List<NativeSketchCurve>();
        foreach (SketchEntity entity in sketch.SketchEntities)
        {
            if (entity.Construction || entity.Reference || !IsProfileCurve(entity))
                continue;
            unused.Add(ToNativeSketchCurve(entity));
        }

        if (unused.Count != expectedCurveCount)
            throw new InvalidOperationException(
                $"После нативного копирования '{sourceName}' состав рабочей границы изменился: " +
                $"ожидалось кривых={expectedCurveCount}, найдено={unused.Count}. " +
                DescribeSketch(sketch, unused.Count));

        if (unused.Count == 1 && unused[0].IsClosed)
            return;
        if (unused.Any(curve => curve.IsClosed))
            throw new InvalidOperationException(
                $"Контур '{sourceName}' смешивает отдельную замкнутую кривую с другими элементами. " +
                DescribeSketch(sketch, unused.Count));

        var ordered = new List<(NativeSketchCurve Curve, bool Reversed)> { (unused[0], false) };
        var loopStart = unused[0].Start;
        var current = unused[0].End;
        unused.RemoveAt(0);
        while (unused.Count > 0)
        {
            var found = -1;
            var reversed = false;
            var nearestGap = double.MaxValue;
            for (var index = 0; index < unused.Count; index++)
            {
                var toStart = PointDistance(current, unused[index].Start);
                var toEnd = PointDistance(current, unused[index].End);
                nearestGap = Math.Min(nearestGap, Math.Min(toStart, toEnd));
                if (PointsCoincide(current, unused[index].Start))
                {
                    found = index;
                    break;
                }
                if (PointsCoincide(current, unused[index].End))
                {
                    found = index;
                    reversed = true;
                    break;
                }
            }
            if (found < 0)
                throw new InvalidOperationException(
                    $"После копирования '{sourceName}' цепь оборвалась на элементе {ordered.Count}/{expectedCurveCount}; " +
                    $"ближайший геометрический разрыв={nearestGap * 10.0:F6} мм. " +
                    DescribeSketch(sketch, expectedCurveCount));
            var next = unused[found];
            unused.RemoveAt(found);
            ordered.Add((next, reversed));
            current = reversed ? next.Start : next.End;
        }

        var closingGap = PointDistance(current, loopStart);
        if (!PointsCoincide(current, loopStart))
            throw new InvalidOperationException(
                $"После копирования '{sourceName}' последний стык не совпал с первым; " +
                $"разрыв={closingGap * 10.0:F6} мм. " + DescribeSketch(sketch, expectedCurveCount));

        var mergeErrors = new List<string>();
        for (var index = 0; index < ordered.Count; index++)
        {
            var currentCurve = ordered[index];
            var nextCurve = ordered[(index + 1) % ordered.Count];
            var end = currentCurve.Reversed
                ? currentCurve.Curve.StartSketchPoint!
                : currentCurve.Curve.EndSketchPoint!;
            var start = nextCurve.Reversed
                ? nextCurve.Curve.EndSketchPoint!
                : nextCurve.Curve.StartSketchPoint!;
            if (SameComObject(end, start))
                continue;
            try
            {
                end.Merge(start);
            }
            catch (Exception exception)
            {
                mergeErrors.Add($"стык {index + 1}: {exception.Message}");
            }
        }

        sketch.Solve();
        sketch.UpdateProfiles();
        if (mergeErrors.Count > 0)
            throw new InvalidOperationException(
                $"Inventor не объединил {mergeErrors.Count} конечных точек '{sourceName}': " +
                string.Join(" | ", mergeErrors.Take(4)) + ". " + DescribeSketch(sketch, expectedCurveCount));
    }

    private static NativeSketchCurve ToNativeSketchCurve(SketchEntity entity) => entity switch
    {
        SketchLine line => new NativeSketchCurve(
            entity, line.Geometry, line.StartSketchPoint.Geometry, line.EndSketchPoint.Geometry,
            line.StartSketchPoint, line.EndSketchPoint, false),
        SketchArc arc => new NativeSketchCurve(
            entity, arc.Geometry, arc.StartSketchPoint.Geometry, arc.EndSketchPoint.Geometry,
            arc.StartSketchPoint, arc.EndSketchPoint, false),
        SketchEllipticalArc arc => new NativeSketchCurve(
            entity, arc.Geometry, arc.StartSketchPoint.Geometry, arc.EndSketchPoint.Geometry,
            arc.StartSketchPoint, arc.EndSketchPoint, false),
        SketchControlPointSpline spline => new NativeSketchCurve(
            entity, spline.Geometry, spline.StartSketchPoint.Geometry, spline.EndSketchPoint.Geometry,
            spline.StartSketchPoint, spline.EndSketchPoint, spline.IsClosed),
        SketchSpline spline => new NativeSketchCurve(
            entity, spline.Geometry, spline.StartSketchPoint.Geometry, spline.EndSketchPoint.Geometry,
            spline.StartSketchPoint, spline.EndSketchPoint, spline.Closed),
        SketchFixedSpline spline => new NativeSketchCurve(
            entity, spline.Geometry, spline.StartSketchPoint.Geometry, spline.EndSketchPoint.Geometry,
            spline.StartSketchPoint, spline.EndSketchPoint, spline.Closed),
        SketchCircle circle => new NativeSketchCurve(
            entity, circle.Geometry, circle.CenterSketchPoint.Geometry, circle.CenterSketchPoint.Geometry,
            null, null, true),
        SketchEllipse ellipse => new NativeSketchCurve(
            entity, ellipse.Geometry, ellipse.CenterSketchPoint.Geometry, ellipse.CenterSketchPoint.Geometry,
            null, null, true),
        _ => throw new InvalidOperationException($"Неподдерживаемая кривая эскиза: {entity.Type}.")
    };

    private static double PointDistance(Point2d first, Point2d second)
    {
        var dx = first.X - second.X;
        var dy = first.Y - second.Y;
        return Math.Sqrt(dx * dx + dy * dy);
    }

    private static bool SameComObject(object first, object second)
    {
        if (ReferenceEquals(first, second))
            return true;
        var firstUnknown = IntPtr.Zero;
        var secondUnknown = IntPtr.Zero;
        try
        {
            firstUnknown = Marshal.GetIUnknownForObject(first);
            secondUnknown = Marshal.GetIUnknownForObject(second);
            return firstUnknown == secondUnknown;
        }
        finally
        {
            if (firstUnknown != IntPtr.Zero) Marshal.Release(firstUnknown);
            if (secondUnknown != IntPtr.Zero) Marshal.Release(secondUnknown);
        }
    }

    private void CopyConnectedSketchGeometry(
        PlanarSketch source,
        PlanarSketch target,
        SketchBasis basis,
        double scale)
    {
        var unused = new List<object>();
        foreach (SketchEntity entity in source.SketchEntities)
        {
            if (entity.Construction || entity.Reference)
                continue;
            switch (entity)
            {
                case SketchPoint: break;
                case SketchLine line: unused.Add(line.Geometry); break;
                case SketchArc arc: unused.Add(arc.Geometry); break;
                case SketchControlPointSpline spline: unused.Add(spline.Geometry); break;
                case SketchSpline spline: unused.Add(spline.Geometry); break;
                case SketchFixedSpline spline: unused.Add(spline.Geometry); break;
                default:
                    throw new InvalidOperationException(
                        $"Резервное копирование '{source.Name}' не поддерживает {entity.Type}.");
            }
        }
        if (unused.Count == 0)
            throw new InvalidOperationException($"В эскизе '{source.Name}' нет рабочих кривых.");

        var ordered = new List<(object Curve, bool Reversed)> { (unused[0], false) };
        var loopStart = CurveStartPoint(unused[0]);
        var current = CurveEndPoint(unused[0]);
        unused.RemoveAt(0);
        while (unused.Count > 0)
        {
            var found = -1;
            var reversed = false;
            for (var index = 0; index < unused.Count; index++)
            {
                if (PointsCoincide(current, CurveStartPoint(unused[index])))
                {
                    found = index;
                    break;
                }
                if (PointsCoincide(current, CurveEndPoint(unused[index])))
                {
                    found = index;
                    reversed = true;
                    break;
                }
            }
            if (found < 0)
                throw new InvalidOperationException(
                    $"Рабочие кривые '{source.Name}' не образуют одну связанную цепь.");
            var curve = unused[found];
            unused.RemoveAt(found);
            ordered.Add((curve, reversed));
            current = reversed ? CurveStartPoint(curve) : CurveEndPoint(curve);
        }
        if (!PointsCoincide(current, loopStart))
            throw new InvalidOperationException($"Рабочая цепь '{source.Name}' не замкнута.");

        var firstTargetPoint = target.SketchPoints.Add(MapPoint(basis, loopStart, scale), false);
        var currentTargetPoint = firstTargetPoint;
        for (var index = 0; index < ordered.Count; index++)
        {
            var (curve, reversed) = ordered[index];
            var sourceEnd = reversed ? CurveStartPoint(curve) : CurveEndPoint(curve);
            var endTargetPoint = index == ordered.Count - 1
                ? firstTargetPoint
                : target.SketchPoints.Add(MapPoint(basis, sourceEnd, scale), false);
            switch (curve)
            {
                case LineSegment2d:
                    target.SketchLines.AddByTwoPoints(currentTargetPoint, endTargetPoint);
                    break;
                case Arc2d arc:
                    target.SketchArcs.AddByCenterStartEndPoint(
                        MapPoint(basis, arc.Center, scale),
                        currentTargetPoint,
                        endTargetPoint,
                        (reversed ? -arc.SweepAngle : arc.SweepAngle) * basis.Handedness < 0);
                    break;
                case BSplineCurve2d spline:
                    var transformed = TransformSpline(spline, basis, scale);
                    target.SketchFixedSplines.Add(transformed);
                    break;
            }
            currentTargetPoint = endTargetPoint;
        }
    }

    private void CopyConnectedProfile(
        IReadOnlyList<ProfileEntity> entities,
        string sourceSketchName,
        PlanarSketch target,
        SketchBasis basis,
        double scale)
    {
        var firstSourcePoint = CurveStartPoint(entities[0].Curve);
        var firstTargetPoint = target.SketchPoints.Add(
            MapPoint(basis, firstSourcePoint, scale), false);
        var currentTargetPoint = firstTargetPoint;

        for (var index = 0; index < entities.Count; index++)
        {
            var entity = entities[index];
            var sourceEndPoint = CurveEndPoint(entity.Curve);
            var closesLoop = index == entities.Count - 1 &&
                             PointsCoincide(sourceEndPoint, firstSourcePoint);
            var endTargetPoint = closesLoop
                ? firstTargetPoint
                : target.SketchPoints.Add(MapPoint(basis, sourceEndPoint, scale), false);

            switch (entity.Curve)
            {
                case LineSegment2d:
                    target.SketchLines.AddByTwoPoints(currentTargetPoint, endTargetPoint);
                    break;
                case Arc2d arc:
                    target.SketchArcs.AddByCenterStartEndPoint(
                        MapPoint(basis, arc.Center, scale),
                        currentTargetPoint,
                        endTargetPoint,
                        arc.SweepAngle * basis.Handedness < 0);
                    break;
                case BSplineCurve2d spline:
                    try
                    {
                        // For a mixed loop copy the exact oriented ProfileEntity NURBS.
                        // A CV/fitted spline's end point is not generally its first/last
                        // defining point, so substituting shared points into that defining
                        // collection changes the curve and can leave Profiles.AddForSolid open.
                        // Preserve the NURBS' own exact endpoints. Substituting
                        // external SketchPoints can reshape a non-clamped CV spline.
                        target.SketchFixedSplines.Add(TransformSpline(spline, basis, scale));
                    }
                    catch (Exception exception)
                    {
                        throw new InvalidOperationException(
                            $"Не удалось скопировать участок сплайна из '{sourceSketchName}' " +
                            "с общими конечными точками: " + exception.Message,
                            exception);
                    }
                    break;
            }

            currentTargetPoint = endTargetPoint;
        }
    }

    private Point2d CurveStartPoint(object curve) => curve switch
    {
        LineSegment2d line => line.StartPoint,
        Arc2d arc => arc.StartPoint,
        EllipticalArc2d arc => arc.StartPoint,
        BSplineCurve2d spline => BSplineEndPoint(spline, false),
        _ => throw new InvalidOperationException("У кривой нет поддерживаемой начальной точки.")
    };

    private Point2d CurveEndPoint(object curve) => curve switch
    {
        LineSegment2d line => line.EndPoint,
        Arc2d arc => arc.EndPoint,
        EllipticalArc2d arc => arc.EndPoint,
        BSplineCurve2d spline => BSplineEndPoint(spline, true),
        _ => throw new InvalidOperationException("У кривой нет поддерживаемой конечной точки.")
    };

    private static bool PointsCoincide(Point2d first, Point2d second)
    {
        var dx = first.X - second.X;
        var dy = first.Y - second.Y;
        return dx * dx + dy * dy <= 1e-12;
    }

    private Point2d BSplineEndPoint(BSplineCurve2d spline, bool end)
    {
        var evaluator = spline.Evaluator;
        evaluator.GetParamExtents(out var minimum, out var maximum);
        var parameters = new[] { end ? maximum : minimum };
        var coordinates = new double[2];
        evaluator.GetPointAtParam(ref parameters, ref coordinates);
        return _application.TransientGeometry.CreatePoint2d(coordinates[0], coordinates[1]);
    }

    private void CopyControlPointSpline(
        SketchControlPointSpline source,
        PlanarSketch target,
        SketchBasis basis,
        double scale,
        string sourceSketchName,
        SketchPoint? startPoint = null,
        SketchPoint? endPoint = null,
        bool reversed = false)
    {
        try
        {
            var points = _application.TransientObjects.CreateObjectCollection();
            for (var offset = 0; offset < source.ControlPointCount; offset++)
            {
                var index = reversed ? source.ControlPointCount - offset : offset + 1;
                if (offset == 0 && startPoint is not null)
                    points.Add(startPoint);
                else if (offset == source.ControlPointCount - 1 && endPoint is not null)
                    points.Add(endPoint);
                else
                    points.Add(MapPoint(basis, source.ControlPoint[index].Geometry, scale));
            }

            var copied = target.SketchControlPointSplines.Add(points);
            if (source.IsClosed && !copied.IsClosed)
                throw new InvalidOperationException("Inventor создал незамкнутую копию сплайна.");
        }
        catch (Exception exception)
        {
            throw new InvalidOperationException(
                $"Не удалось скопировать Control Vertex Spline из '{sourceSketchName}' " +
                $"как одну замкнутую кривую: {exception.Message}",
                exception);
        }
    }

    private void CopyInterpolationSpline(
        SketchSpline source,
        PlanarSketch target,
        SketchBasis basis,
        double scale,
        string sourceSketchName,
        SketchPoint? startPoint = null,
        SketchPoint? endPoint = null,
        bool reversed = false)
    {
        try
        {
            var points = _application.TransientObjects.CreateObjectCollection();
            for (var offset = 0; offset < source.FitPointCount; offset++)
            {
                var index = reversed ? source.FitPointCount - offset : offset + 1;
                if (offset == 0 && startPoint is not null)
                    points.Add(startPoint);
                else if (offset == source.FitPointCount - 1 && endPoint is not null)
                    points.Add(endPoint);
                else
                    points.Add(MapPoint(basis, source.FitPoint[index].Geometry, scale));
            }

            var copied = target.SketchSplines.Add(points, source.FitMethod);
            copied.Tension = source.Tension;
            if (source.Closed)
                copied.Closed = true;
            if (source.Closed && !copied.Closed)
                throw new InvalidOperationException("Inventor создал незамкнутую копию сплайна.");
        }
        catch (Exception exception)
        {
            throw new InvalidOperationException(
                $"Не удалось скопировать Interpolation Spline из '{sourceSketchName}' " +
                $"как одну кривую: {exception.Message}",
                exception);
        }
    }

    private BSplineCurve2d TransformSpline(BSplineCurve2d source, SketchBasis basis, double scale)
    {
        source.GetBSplineInfo(out _, out var poleCount, out _, out _, out _, out _);
        var transformed = source.Copy();
        for (var index = 1; index <= poleCount; index++)
            transformed.PoleAtIndex[index] = MapPoint(basis, source.PoleAtIndex[index], scale);
        return transformed;
    }

    private static void RunStage(string stage, System.Action action)
    {
        try
        {
            action();
        }
        catch (Exception exception)
        {
            throw StageException(stage, exception);
        }
    }

    private static T RunStage<T>(string stage, Func<T> action)
    {
        try
        {
            return action();
        }
        catch (Exception exception)
        {
            throw StageException(stage, exception);
        }
    }

    private static InvalidOperationException StageException(string stage, Exception exception) =>
        new($"Ошибка на этапе «{stage}»: {exception.Message} " +
            $"(HRESULT 0x{exception.HResult:X8})", exception);

    private static Point2d MapPoint(SketchBasis basis, Point2d point, double scale) =>
        basis.Map(point.X * scale, point.Y * scale);

    private UnitVector2d MapUnitVector(SketchBasis basis, UnitVector2d vector) =>
        _application.TransientGeometry.CreateUnitVector2d(
            basis.Ux * vector.X + basis.Vx * vector.Y,
            basis.Uy * vector.X + basis.Vy * vector.Y);

    private SketchBasis BuildSketchBasis(
        PlanarSketch sketch,
        Point modelAnchor,
        Vector modelX,
        Vector modelY)
    {
        var origin = sketch.ModelToSketchSpace(modelAnchor);
        var xEnd = _application.TransientGeometry.CreatePoint(
            modelAnchor.X + modelX.X, modelAnchor.Y + modelX.Y, modelAnchor.Z + modelX.Z);
        var yEnd = _application.TransientGeometry.CreatePoint(
            modelAnchor.X + modelY.X, modelAnchor.Y + modelY.Y, modelAnchor.Z + modelY.Z);
        var x2 = sketch.ModelToSketchSpace(xEnd);
        var y2 = sketch.ModelToSketchSpace(yEnd);
        var ux = x2.X - origin.X;
        var uy = x2.Y - origin.Y;
        var xLength = Math.Sqrt(ux * ux + uy * uy);
        if (xLength < 1e-8)
            throw new InvalidOperationException("Ось профиля перпендикулярна выбранной грани.");
        ux /= xLength;
        uy /= xLength;

        var vx = y2.X - origin.X;
        var vy = y2.Y - origin.Y;
        var projection = vx * ux + vy * uy;
        vx -= projection * ux;
        vy -= projection * uy;
        var yLength = Math.Sqrt(vx * vx + vy * vy);
        if (yLength < 1e-8)
        {
            vx = -uy;
            vy = ux;
        }
        else
        {
            vx /= yLength;
            vy /= yLength;
        }
        return new SketchBasis(_application.TransientGeometry, origin, ux, uy, vx, vy);
    }

    private static Point ToNativePoint(ComponentOccurrence occurrence, Point assemblyPoint)
    {
        var result = assemblyPoint.Copy();
        var inverse = occurrence.Transformation;
        inverse.Invert();
        result.TransformBy(inverse);
        return result;
    }

    private Point OffsetAssemblyPoint(Point source, Vector direction, double distance) =>
        _application.TransientGeometry.CreatePoint(
            source.X + direction.X * distance,
            source.Y + direction.Y * distance,
            source.Z + direction.Z * distance);

    private static Vector ToNativeVector(ComponentOccurrence occurrence, Vector assemblyVector)
    {
        var result = assemblyVector.Copy();
        var inverse = occurrence.Transformation;
        inverse.Invert();
        result.TransformBy(inverse);
        result.Normalize();
        return result;
    }

    internal static (Vector XAxis, Vector YAxis) BuildFemaleAxes(JointPairSelection selection)
    {
        if (selection.Female.ProxyFace.Geometry is not Plane plane)
            throw new InvalidOperationException("Грань паза должна быть плоской.");

        var normal = plane.Normal.AsVector();
        normal.Normalize();
        var x = selection.SideASlotCenterAssembly.VectorTo(selection.SideBSlotCenterAssembly);
        x = ProjectToPlane(x, normal);
        if (x.Length < 1e-8)
            x = ProjectToPlane(selection.MaleSideNormal, normal);
        if (x.Length < 1e-8)
            throw new InvalidOperationException("Не удалось определить направление паза на выбранной грани.");
        x.Normalize();

        var desiredY = ProjectToPlane(selection.MaleProfileYAxis, normal);
        var y = normal.CrossProduct(x);
        y.Normalize();
        if (desiredY.Length > 1e-8 && y.DotProduct(desiredY) < 0)
            y.ScaleBy(-1);
        return (x, y);
    }

    private static Vector ProjectToPlane(Vector source, Vector planeNormal)
    {
        var result = source.Copy();
        var normalPart = planeNormal.Copy();
        normalPart.ScaleBy(result.DotProduct(planeNormal));
        result.SubtractVector(normalPart);
        return result;
    }

    private static double MeasureSpan(ComponentOccurrence occurrence, Vector assemblyDirection)
    {
        var direction = assemblyDirection.Copy();
        direction.Normalize();
        var min = double.MaxValue;
        var max = double.MinValue;
        foreach (SurfaceBody body in occurrence.SurfaceBodies)
        foreach (Vertex vertex in body.Vertices)
        {
            var p = vertex.Point;
            var value = p.X * direction.X + p.Y * direction.Y + p.Z * direction.Z;
            min = Math.Min(min, value);
            max = Math.Max(max, value);
        }
        if (min == double.MaxValue || max - min < 1e-6)
            throw new InvalidOperationException("Не удалось измерить поперечный размер трубы.");
        return max - min;
    }

    private sealed record SketchBasis(
        TransientGeometry Geometry,
        Point2d Origin,
        double Ux,
        double Uy,
        double Vx,
        double Vy)
    {
        public double Handedness => Math.Sign(Ux * Vy - Uy * Vx);

        public Point2d Map(double x, double y) => Geometry.CreatePoint2d(
            Origin.X + Ux * x + Vx * y,
            Origin.Y + Uy * x + Vy * y);
    }

    private sealed record NativeSketchCurve(
        SketchEntity Entity,
        object Geometry,
        Point2d Start,
        Point2d End,
        SketchPoint? StartSketchPoint,
        SketchPoint? EndSketchPoint,
        bool IsClosed);

}
