using Inventor;
using TubeJoint.AddIn.Models;

namespace TubeJoint.AddIn.Services;

internal sealed class JointTemplateService
{
    public const string MaleSketchName = "TJ_MALE_PROFILE";
    public const string FemaleSketchName = "TJ_FEMALE_PROFILE";
    public const string CenterVentSketchName = "TJ_CENTER_VENT";
    public const int CurrentTemplateVersion = 10;
    private readonly Inventor.Application _application;
    public JointTemplateService(Inventor.Application application) => _application = application;

    public string TemplatePath => System.IO.Path.Combine(
        System.Environment.GetFolderPath(System.Environment.SpecialFolder.ApplicationData),
        "Autodesk", "Inventor 2027", "TubeJoint", "Templates", "StandardSideTenonSlot_v6.ipt");

    public string EnsureDefaultTemplate()
    {
        if (System.IO.File.Exists(TemplatePath))
        {
            EnsureTemplateSchema(TemplatePath);
            return TemplatePath;
        }
        var directory = System.IO.Path.GetDirectoryName(TemplatePath)
            ?? throw new InvalidOperationException("Не удалось определить папку шаблонов.");
        System.IO.Directory.CreateDirectory(directory);
        var inventorTemplate = _application.FileManager.GetTemplateFile(DocumentTypeEnum.kPartDocumentObject);
        var document = (PartDocument)_application.Documents.Add(
            DocumentTypeEnum.kPartDocumentObject, inventorTemplate, false);
        try
        {
            BuildDefaultTemplate(document);
            document.SaveAs(TemplatePath, false);
        }
        finally { document.Close(true); }
        return TemplatePath;
    }

    public void ApplyParameters(PartDocument document, TubeJointParameters values)
    {
        EnsureParameterSchema(document);
        var p = document.ComponentDefinition.Parameters.UserParameters;
        SetMillimeters(p, "TJ_MaleWall", values.MaleWallMm);
        SetMillimeters(p, "TJ_FemaleWall", values.FemaleWallMm);
        SetMillimeters(p, "TJ_Clearance", values.ClearanceMm);
        SetMillimeters(p, "TJ_SlotTargetThickness", values.SlotTargetThicknessMm);
        SetUnitless(p, "TJ_ReliefFactor", values.ReliefFactorPercent / 100.0);
        // Legacy aliases stay synchronized until all user sketches are migrated.
        SetMillimeters(p, "TJ_TenonWidth", values.TenonWidthMm);
        SetMillimeters(p, "TJ_TenonHeight", values.TenonHeightMm);
        document.Update2(true);
    }

    public IDisposable TemporarilyApplyParameters(PartDocument document, TubeJointParameters values)
    {
        EnsureParameterSchema(document);
        var parameters = document.ComponentDefinition.Parameters.UserParameters;
        var names = new[]
        {
            "TJ_MaleWall",
            "TJ_FemaleWall",
            "TJ_Clearance",
            "TJ_SlotTargetThickness",
            "TJ_ReliefFactor",
            "TJ_TenonWidth",
            "TJ_TenonHeight"
        };
        var expressions = names.ToDictionary(name => name, name => parameters[name].Expression);

        void Restore()
        {
            foreach (var pair in expressions)
                parameters[pair.Key].Expression = pair.Value;
            document.Update2(true);
        }

        try
        {
            ApplyParameters(document, values);
            return new RestoreScope(Restore);
        }
        catch
        {
            Restore();
            throw;
        }
    }

    public void OpenTemplateForEditing()
    {
        var path = EnsureDefaultTemplate();
        foreach (Document document in _application.Documents)
        {
            if (!string.Equals(document.FullFileName, path, StringComparison.OrdinalIgnoreCase)) continue;
            document.Activate();
            return;
        }
        _application.Documents.Open(path, true);
    }

    public JointStandardSettings LoadSettings()
    {
        var path = EnsureDefaultTemplate();
        PartDocument? document = null;
        var openedHere = false;
        foreach (Document openDocument in _application.Documents)
        {
            if (!string.Equals(openDocument.FullFileName, path, StringComparison.OrdinalIgnoreCase)) continue;
            document = (PartDocument)openDocument;
            break;
        }

        if (document is null)
        {
            document = (PartDocument)_application.Documents.Open(path, false);
            openedHere = true;
        }

        try
        {
            var parameters = document.ComponentDefinition.Parameters.UserParameters;
            var settings = new JointStandardSettings
            {
                AutoWidthFactor = Unitless(parameters, "TJ_AutoWidthFactor"),
                AutoWidthMinimumMm = Millimeters(parameters, "TJ_AutoWidthMinimum"),
                AutoWidthMaximumMm = Millimeters(parameters, "TJ_AutoWidthMaximum"),
                AutoHeightFactor = Unitless(parameters, "TJ_AutoHeightFactor"),
                DimensionStepMm = Millimeters(parameters, "TJ_DimensionStep"),
                CompactPresetFactor = Unitless(parameters, "TJ_PresetCompactFactor"),
                ReinforcedPresetFactor = Unitless(parameters, "TJ_PresetReinforcedFactor")
            };
            if (settings.AutoWidthFactor <= 0 || settings.AutoHeightFactor <= 0 ||
                settings.AutoWidthMinimumMm <= 0 ||
                settings.AutoWidthMaximumMm < settings.AutoWidthMinimumMm ||
                settings.DimensionStepMm <= 0 || settings.CompactPresetFactor <= 0 ||
                settings.ReinforcedPresetFactor <= 0)
                throw new InvalidOperationException(
                    "Параметры правила TJ_Auto* в шаблоне имеют недопустимые значения.");
            return settings;
        }
        finally
        {
            if (openedHere) document.Close(true);
        }
    }

    private void BuildDefaultTemplate(PartDocument document)
    {
        var definition = document.ComponentDefinition;
        var p = definition.Parameters.UserParameters;
        AddLength(p, "TJ_MaleFaceWidth", "40 mm", "Поперечный размер трубы по нормали стенки");
        AddLength(p, "TJ_MaleFaceHeight", "40 mm", "Поперечный размер трубы в плоскости профиля");
        AddLength(p, "TJ_FemaleFaceWidth", "50 mm", "Размер грани паза по X");
        AddLength(p, "TJ_FemaleFaceHeight", "50 mm", "Размер грани паза по Y");
        AddLength(p, "TJ_MaleWall", "2 mm", "Толщина стенки шипа");
        AddLength(p, "TJ_FemaleWall", "2 mm", "Толщина стенки паза");
        AddLength(p, "TJ_Clearance", "0.2 mm", "Зазор на сторону");
        AddUnitless(p, "TJ_AutoWidthFactor", "0.50", "Авто: ширина X от поперечного размера трубы");
        AddLength(p, "TJ_AutoWidthMinimum", "8 mm", "Минимальная автоматическая ширина X");
        AddLength(p, "TJ_AutoWidthMaximum", "40 mm", "Максимальная автоматическая ширина X");
        AddUnitless(p, "TJ_AutoHeightFactor", "0.55", "Авто: высота Y от размера стенки трубы");
        AddLength(p, "TJ_DimensionStep", "0.5 mm", "Шаг округления автоматических размеров");
        AddUnitless(p, "TJ_PresetCompactFactor", "0.80", "Множитель компактного пресета");
        AddUnitless(p, "TJ_PresetReinforcedFactor", "1.15", "Множитель усиленного пресета");
        AddLength(p, "TJ_TenonWidth", "20 mm", "Горизонтальный вылет шипа X");
        AddLength(p, "TJ_TenonHeight", "22 mm", "Вертикальный размер шипа Y");
        AddLength(p, "TJ_SlotTargetThickness", "2 mm", "Стандартная толщина: 1; 1.5; 2; 2.5; 3 мм");
        AddLength(p, "TJ_SlotWidth", "TJ_SlotTargetThickness + 2 * TJ_Clearance", "Ширина паза");
        AddLength(p, "TJ_SlotHeight", "TJ_TenonHeight + 2 * TJ_Clearance", "Высота паза");
        AddLength(p, "TJ_VentWidth", "TJ_MaleFaceWidth * 0.38", "Ширина дополнительного отверстия");
        AddLength(p, "TJ_VentHeight", "TJ_TenonHeight * 0.12", "Высота дополнительного отверстия");
        AddParametricContract(p);

        var plane = definition.WorkPlanes[3];
        var male = definition.Sketches.Add(plane, false);
        male.Name = MaleSketchName;
        CreateMale(male);
        AddOriginMarker(male, 0.75, 0.75);
        var female = definition.Sketches.Add(plane, false);
        female.Name = FemaleSketchName;
        CreateFemale(female);
        AddOriginMarker(female, 0.75, 0.75);
        var vent = definition.Sketches.Add(plane, false);
        vent.Name = CenterVentSketchName;
        CreateCenterVent(vent);
        AddOriginMarker(vent, 0.75, 0.50);
        // Fail immediately while creating the template if either loop is open or
        // self-intersecting, instead of discovering it later in the user's part.
        _ = male.Profiles.AddForSolid();
        _ = female.Profiles.AddForSolid();
        _ = vent.Profiles.AddForSolid();
        document.Update2(true);
    }

    private void EnsureTemplateSchema(string path)
    {
        PartDocument? document = null;
        var openedHere = false;
        foreach (Document openDocument in _application.Documents)
        {
            if (!string.Equals(openDocument.FullFileName, path, StringComparison.OrdinalIgnoreCase)) continue;
            document = (PartDocument)openDocument;
            break;
        }
        if (document is null)
        {
            document = (PartDocument)_application.Documents.Open(path, false);
            openedHere = true;
        }

        try
        {
            var changed = EnsureParameterSchema(document);
            if (changed && openedHere) document.Save();
        }
        finally
        {
            if (openedHere) document.Close(true);
        }
    }

    private static bool EnsureParameterSchema(PartDocument document)
    {
        var p = document.ComponentDefinition.Parameters.UserParameters;
        var changed = false;
        changed |= EnsureLength(p, "TJ_TenonLength", "TJ_TenonWidth",
            "Длина шипа от корня до носика; главный размер X");
        changed |= EnsureLength(p, "TJ_TenonAcrossWidth", "TJ_TenonHeight",
            "Ширина шипа поперек; главный размер Y");
        changed |= EnsureLength(p, "TJ_TenonThickness", "TJ_MaleWall",
            "Толщина материала шипа");
        changed |= EnsureUnitless(p, "TJ_ReliefFactor", "0.35",
            "Коэффициент локальных прослаблений от толщины материала");
        changed |= EnsureLength(p, "TJ_TenonRelief", "TJ_TenonThickness * TJ_ReliefFactor",
            "Прослабление шипа; зависит только от толщины материала");
        changed |= EnsureLength(p, "TJ_SlotRelief", "TJ_TenonThickness * TJ_ReliefFactor",
            "Угловое прослабление паза; зависит только от толщины материала");
        changed |= EnsureLength(p, "TJ_SlotOverallWidth",
            "TJ_SlotTargetThickness + 2 * TJ_Clearance", "Полная ширина паза");
        changed |= EnsureLength(p, "TJ_SlotOverallLength",
            "TJ_TenonAcrossWidth + 2 * TJ_Clearance", "Полная длина паза");
        changed |= EnsureUnitless(p, "TJ_ProfileIsParametric", "1",
            "Маркер совместимости; геометрия всегда копируется из параметрического эскиза 1:1");
        changed |= EnsureSchemaVersion(p);
        changed |= EnsureUnitless(p, "TJ_PresetCompactFactor", "0.80",
            "Множитель компактного пресета");
        changed |= EnsureUnitless(p, "TJ_PresetReinforcedFactor", "1.15",
            "Множитель усиленного пресета");
        return changed;
    }

    private static void AddParametricContract(UserParameters p)
    {
        EnsureLength(p, "TJ_TenonLength", "TJ_TenonWidth", "Длина шипа от корня до носика");
        EnsureLength(p, "TJ_TenonAcrossWidth", "TJ_TenonHeight", "Ширина шипа поперек");
        EnsureLength(p, "TJ_TenonThickness", "TJ_MaleWall", "Толщина материала шипа");
        EnsureUnitless(p, "TJ_ReliefFactor", "0.35",
            "Коэффициент локальных прослаблений от толщины материала");
        EnsureLength(p, "TJ_TenonRelief", "TJ_TenonThickness * TJ_ReliefFactor",
            "Прослабление шипа от толщины материала");
        EnsureLength(p, "TJ_SlotRelief", "TJ_TenonThickness * TJ_ReliefFactor",
            "Прослабление паза от толщины материала");
        EnsureLength(p, "TJ_SlotOverallWidth", "TJ_SlotTargetThickness + 2 * TJ_Clearance",
            "Полная ширина паза");
        EnsureLength(p, "TJ_SlotOverallLength", "TJ_TenonAcrossWidth + 2 * TJ_Clearance",
            "Полная длина паза");
        EnsureUnitless(p, "TJ_ProfileIsParametric", "1",
            "Маркер совместимости; геометрия всегда копируется из параметрического эскиза 1:1");
        EnsureUnitless(p, "TJ_TemplateSchema", "10", "Версия контракта шаблона");
    }

    private static bool EnsureLength(UserParameters p, string name, string expression, string comment)
    {
        try { _ = p[name]; return false; }
        catch { AddLength(p, name, expression, comment); return true; }
    }

    private static bool EnsureUnitless(UserParameters p, string name, string expression, string comment)
    {
        try { _ = p[name]; return false; }
        catch { AddUnitless(p, name, expression, comment); return true; }
    }

    private static bool EnsureSchemaVersion(UserParameters p)
    {
        try
        {
            var parameter = p["TJ_TemplateSchema"];
            if (Convert.ToDouble(parameter.Value) >= CurrentTemplateVersion)
                return false;
            parameter.Expression = CurrentTemplateVersion.ToString(
                System.Globalization.CultureInfo.InvariantCulture);
            return true;
        }
        catch
        {
            AddUnitless(p, "TJ_TemplateSchema", CurrentTemplateVersion.ToString(
                System.Globalization.CultureInfo.InvariantCulture), "Версия контракта шаблона");
            return true;
        }
    }

    private static void SetMillimeters(UserParameters p, string name, double value) =>
        p[name].Value = value / 10.0;

    private static void SetUnitless(UserParameters p, string name, double value) =>
        p[name].Value = value;

    private sealed class RestoreScope : IDisposable
    {
        private Action? _restore;

        public RestoreScope(Action restore) => _restore = restore;

        public void Dispose()
        {
            var restore = _restore;
            _restore = null;
            restore?.Invoke();
        }
    }

    private void CreateMale(PlanarSketch sketch)
    {
        var g = _application.TransientGeometry;
        var points = new List<Point2d>
        {
            g.CreatePoint2d(-0.12, -0.50),
            g.CreatePoint2d(0.00, -0.50),
            g.CreatePoint2d(0.68, -0.45)
        };
        // The working sides stay almost parallel. Only the rounded nose narrows,
        // avoiding the pinched funnel shape of the previous template.
        for (var i = 1; i <= 12; i++)
        {
            var angle = -Math.PI / 2.0 + Math.PI * i / 12.0;
            points.Add(g.CreatePoint2d(0.68 + 0.45 * Math.Cos(angle), 0.45 * Math.Sin(angle)));
        }
        points.Add(g.CreatePoint2d(0.00, 0.50));
        points.Add(g.CreatePoint2d(-0.12, 0.50));
        AddClosedPolyline(sketch, points);
    }

    private void CreateFemale(PlanarSketch sketch)
    {
        // Temporary base standard: a plain rectangle centered exactly on (0,0).
        // The user can later replace this one closed contour in TJ_FEMALE_PROFILE;
        // placement and one-to-one copying continue to use the marked sketch origin.
        var g = _application.TransientGeometry;
        var points = new List<Point2d>
        {
            g.CreatePoint2d(-0.20, -0.50),
            g.CreatePoint2d( 0.20, -0.50),
            g.CreatePoint2d( 0.20,  0.50),
            g.CreatePoint2d(-0.20,  0.50)
        };
        AddClosedPolyline(sketch, points);
    }

    private void CreateCenterVent(PlanarSketch sketch)
    {
        var g = _application.TransientGeometry;
        const double centerX = 0.0;
        const double halfStraight = 0.36;
        const double radius = 0.18;
        var points = new List<Point2d>
        {
            g.CreatePoint2d(centerX - halfStraight, -radius),
            g.CreatePoint2d(centerX + halfStraight, -radius)
        };
        for (var i = 1; i <= 12; i++)
        {
            var angle = -Math.PI / 2.0 + Math.PI * i / 12.0;
            points.Add(g.CreatePoint2d(
                centerX + halfStraight + radius * Math.Cos(angle), radius * Math.Sin(angle)));
        }
        points.Add(g.CreatePoint2d(centerX - halfStraight, radius));
        for (var i = 1; i <= 12; i++)
        {
            var angle = Math.PI / 2.0 + Math.PI * i / 12.0;
            points.Add(g.CreatePoint2d(
                centerX - halfStraight + radius * Math.Cos(angle), radius * Math.Sin(angle)));
        }
        AddClosedPolyline(sketch, points);
    }

    private void AddOriginMarker(PlanarSketch sketch, double halfWidth, double halfHeight)
    {
        var g = _application.TransientGeometry;
        var horizontal = sketch.SketchLines.AddByTwoPoints(
            g.CreatePoint2d(-halfWidth, 0), g.CreatePoint2d(halfWidth, 0));
        var vertical = sketch.SketchLines.AddByTwoPoints(
            g.CreatePoint2d(0, -halfHeight), g.CreatePoint2d(0, halfHeight));
        horizontal.Construction = true;
        vertical.Construction = true;

        // HoleCenter renders as an explicit center mark and is ignored by solid profiles.
        var center = sketch.SketchPoints.Add(g.CreatePoint2d(0, 0), true);
        center.HoleCenter = true;
    }

    private static void AddClosedPolyline(PlanarSketch sketch, IReadOnlyList<Point2d> geometry)
    {
        if (geometry.Count < 3) throw new InvalidOperationException("Профиль должен иметь минимум три точки.");
        var count = geometry.Count;
        if (geometry[0].DistanceTo(geometry[^1]) < 1e-8) count--;
        var points = geometry.Take(count).Select(point => sketch.SketchPoints.Add(point, false)).ToList();
        for (var i = 0; i < points.Count; i++)
            sketch.SketchLines.AddByTwoPoints(points[i], points[(i + 1) % points.Count]);
    }
    private static void AddLength(UserParameters p,string n,string e,string c)
    { var x=p.AddByExpression(n,e,UnitsTypeEnum.kMillimeterLengthUnits); x.Comment=c; }
    private static void AddUnitless(UserParameters p,string n,string e,string c)
    { var x=p.AddByExpression(n,e,UnitsTypeEnum.kUnitlessUnits); x.Comment=c; }
    private static double Millimeters(UserParameters p, string name) =>
        Convert.ToDouble(p[name].Value) * 10.0;
    private static double Unitless(UserParameters p, string name) =>
        Convert.ToDouble(p[name].Value);
}
