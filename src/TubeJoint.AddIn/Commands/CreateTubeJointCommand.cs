using System.Windows.Forms;
using Inventor;
using TubeJoint.AddIn.Models;
using TubeJoint.AddIn.Services;
using TubeJoint.AddIn.UI;

namespace TubeJoint.AddIn.Commands;

internal sealed class CreateTubeJointCommand : IDisposable
{
    private readonly Inventor.Application _application;
    private readonly OccurrenceSelectionService _selection;
    private readonly FrameTrimService _trim;
    private readonly JointTemplateService _templates;
    private readonly JointRepository _repository;
    private readonly IJointGeometryBuilder _geometry;
    private NativeJointInput? _activeInput;
    private AssemblyDocument? _activeAssembly;
    private JointPairSelection? _activeSelection;
    private string? _activeTemplatePath;
    private bool _creating;

    public CreateTubeJointCommand(Inventor.Application application)
    {
        _application = application;
        _selection = new OccurrenceSelectionService(application);
        _trim = new FrameTrimService(application);
        _templates = new JointTemplateService(application);
        _repository = new JointRepository();
        _geometry = new TemplateProfileGeometryBuilder(application, _templates);
    }

    public void Execute()
    {
        if (_activeInput is not null)
        {
            _activeInput.BringToFront();
            return;
        }

        try
        {
            if (_application.ActiveDocument is not AssemblyDocument assembly)
                throw new InvalidOperationException("Откройте сборку IAM и повторите команду.");

            var selection = _selection.PickPair();
            if (!_trim.CanContinueOrLaunchTrim(assembly, selection)) return;

            var templatePath = _templates.EnsureDefaultTemplate();
            var settings = _templates.LoadSettings();
            var input = new NativeJointInput(_application, assembly, selection, settings, _templates);
            _activeAssembly = assembly;
            _activeSelection = selection;
            _activeTemplatePath = templatePath;
            _activeInput = input;
            input.Accepted += InputOnAccepted;
            input.Cancelled += InputOnCancelled;
            input.Show();
        }
        catch (OperationCanceledException)
        {
            // Native Pick cancellation is a normal command exit.
            CloseActiveInput();
        }
        catch (Exception exception)
        {
            CloseActiveInput();
            MessageBox.Show(exception.Message, "Шип-паз труб", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    public void Dispose() => CloseActiveInput();

    private void InputOnAccepted(TubeJointParameters parameters, JointPairSelection selectedPair)
    {
        if (_creating || _activeAssembly is null || _activeSelection is null ||
            string.IsNullOrWhiteSpace(_activeTemplatePath)) return;

        try
        {
            Validate(parameters, selectedPair);
        }
        catch (Exception exception)
        {
            MessageBox.Show(exception.Message, "Шип-паз труб", MessageBoxButtons.OK, MessageBoxIcon.Error);
            return;
        }

        var assembly = _activeAssembly;
        var selection = selectedPair;
        var templatePath = _activeTemplatePath;
        _creating = true;
        CloseActiveInput();
        try
        {
            var record = CreateRecord(selection, parameters, templatePath);
            Inventor.Transaction? transaction = null;
            try
            {
                // A global transaction groups edits made in both referenced IPTs
                // and the IAM attribute record into one assembly-level Undo item.
                transaction = _application.TransactionManager.StartGlobalTransaction(
                    AsDocument(assembly), "TubeJoint: создать шип-паз");
                _geometry.CreateOrUpdate(assembly, selection, record);
                _repository.Save(assembly, record);
                assembly.Update2(true);
                transaction.End();
                transaction = null;
            }
            catch
            {
                try { transaction?.Abort(); } catch { }
                transaction = null;
                throw;
            }

            // Successful completion is intentionally silent, like Inventor's native tools.
        }
        catch (Exception exception)
        {
            MessageBox.Show(exception.Message, "Шип-паз труб", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        finally
        {
            _creating = false;
        }
    }

    private void InputOnCancelled() => CloseActiveInput();

    private void CloseActiveInput()
    {
        var input = _activeInput;
        _activeInput = null;
        _activeAssembly = null;
        _activeSelection = null;
        _activeTemplatePath = null;
        if (input is null) return;
        input.Accepted -= InputOnAccepted;
        input.Cancelled -= InputOnCancelled;
        input.Dispose();
    }

    private static JointPairRecord CreateRecord(
        JointPairSelection selection,
        TubeJointParameters parameters,
        string templatePath) => new()
    {
        MaleOccurrenceName = selection.Male.Occurrence.Name,
        MaleDocumentPath = OccurrenceSelectionService.GetPartDocument(selection.Male.Occurrence).FullFileName,
        FemaleOccurrenceName = selection.Female.Occurrence.Name,
        FemaleDocumentPath = OccurrenceSelectionService.GetPartDocument(selection.Female.Occurrence).FullFileName,
        TemplatePath = templatePath,
        TemplateVersion = JointTemplateService.CurrentTemplateVersion,
        Parameters = parameters,
        GeometryStatus = "CreatedFromParametricTemplateV8",
        JointPointX = selection.JointPointAssembly.X,
        JointPointY = selection.JointPointAssembly.Y,
        JointPointZ = selection.JointPointAssembly.Z,
        MaleAxisX = selection.MaleAxisTowardFemale.X,
        MaleAxisY = selection.MaleAxisTowardFemale.Y,
        MaleAxisZ = selection.MaleAxisTowardFemale.Z,
        TenonDirectionX = selection.TenonDirectionAssembly.X,
        TenonDirectionY = selection.TenonDirectionAssembly.Y,
        TenonDirectionZ = selection.TenonDirectionAssembly.Z,
        ProfileYAxisX = selection.MaleProfileYAxis.X,
        ProfileYAxisY = selection.MaleProfileYAxis.Y,
        ProfileYAxisZ = selection.MaleProfileYAxis.Z,
        InsertionDeviationDegrees = selection.InsertionDeviationDegrees,
        SideAGapMm = selection.SideAGapMm,
        SideBGapMm = selection.SideBGapMm
    };

    private static void Validate(TubeJointParameters parameters, JointPairSelection selection)
    {
        if (parameters.TenonWidthMm <= 0) throw new InvalidOperationException("Ширина шипа должна быть больше нуля.");
        if (parameters.TenonHeightMm <= 0) throw new InvalidOperationException("Высота шипа должна быть больше нуля.");
        if (parameters.TenonHeightMm > selection.MaleProfileSpanMm + 1e-6)
            throw new InvalidOperationException(
                $"Высота шипа не может превышать размер стенки {selection.MaleProfileSpanMm:0.#} мм.");
        if (parameters.ClearanceMm < 0) throw new InvalidOperationException("Зазор не может быть отрицательным.");
        if (parameters.SlotTargetThicknessMm <= 0)
            throw new InvalidOperationException("Не удалось определить толщину стенки трубы с шипом.");
        if (!parameters.AutoSlotThickness &&
            (parameters.SlotTargetThicknessMm is < 1.0 or > 3.0 ||
             Math.Abs(parameters.SlotTargetThicknessMm * 2.0 -
                      Math.Round(parameters.SlotTargetThicknessMm * 2.0)) > 1e-6))
            throw new InvalidOperationException("Толщина для паза должна быть 1–3 мм с шагом 0,5 мм.");
        if (parameters.MaleWallMm <= 0 || parameters.FemaleWallMm <= 0)
            throw new InvalidOperationException("Толщина стенок должна быть больше нуля.");
        if (parameters.VentScalePercent is < 10.0 or > 500.0)
            throw new InvalidOperationException("Размер дополнительного выреза должен быть 10–500%.");
        if (parameters.ReliefFactorPercent is < 0.0 or > 200.0)
            throw new InvalidOperationException("Прослабление должно быть 0–200% толщины материала.");
    }

    private static Inventor._Document AsDocument(object document) => (Inventor._Document)document;
}
