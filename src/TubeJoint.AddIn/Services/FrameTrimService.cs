using System.Windows.Forms;
using Inventor;
using TubeJoint.AddIn.Models;

namespace TubeJoint.AddIn.Services;

internal sealed class FrameTrimService
{
    private readonly Inventor.Application _application;
    public FrameTrimService(Inventor.Application application) => _application = application;

    public bool CanContinueOrLaunchTrim(AssemblyDocument assembly, JointPairSelection selection)
    {
        var document = OccurrenceSelectionService.GetPartDocument(selection.Male.Occurrence);
        if (!FrameGeneratorTubeAnalyzer.IsFrameMember(document) || HasEndTreatment(document))
            return true;

        var command = FindTrimCommand();
        if (command is null)
        {
            MessageBox.Show(
                "Не удалось автоматически открыть Frame Generator Trim/Extend. " +
                "Можно продолжить с текущим торцом, но перед построением лучше выполнить " +
                "Design → Frame → Trim/Extend вручную.",
                "Шип-паз труб");
            return true;
        }

        assembly.SelectSet.Clear();
        assembly.SelectSet.Select(selection.Female.ProxyFace);
        assembly.SelectSet.Select(selection.Male.Occurrence);
        MessageBox.Show(
            "На трубе с шипом нет торцевой обработки. Сейчас откроется штатная Trim/Extend. " +
            "Подтвердите её и повторно нажмите «Новое соединение». Существующую обработку аддон не меняет.",
            "Шип-паз труб — подготовка");
        command.Execute();
        return false;
    }

    private static bool HasEndTreatment(PartDocument doc)
    {
        // Frame Generator keeps the Trim-Extend node at assembly level, while the
        // member IPT receives a real Split feature (visible as Split1 in its tree).
        // CUTDETAIL is useful for drawings but is not guaranteed to be present or
        // up to date immediately after every Frame Generator operation.
        try
        {
            if (doc.ComponentDefinition.Features.SplitFeatures.Count > 0)
                return true;
        }
        catch { }

        // Frame Generator gives CUTDETAIL properties GUID-like internal names.
        // The visible DisplayName is the reliable identifier for Trim, Miter and Notch.
        foreach (PropertySet set in doc.PropertySets)
        foreach (Inventor.Property property in set)
            if (property.DisplayName.StartsWith("CUTDETAIL", StringComparison.OrdinalIgnoreCase) ||
                property.Name.StartsWith("CUTDETAIL", StringComparison.OrdinalIgnoreCase))
                return true;
        return false;
    }
    private ButtonDefinition? FindTrimCommand()
    {
        // Stable internal name used by Frame Generator for Trim/Extend To Face.
        try
        {
            if (_application.CommandManager.ControlDefinitions["AFG_Cut_Profile_To_Face"]
                is ButtonDefinition exact && exact.Enabled)
                return exact;
        }
        catch { }

        foreach (ControlDefinition definition in _application.CommandManager.ControlDefinitions)
        {
            if (!definition.BuiltIn || definition is not ButtonDefinition button || !button.Enabled) continue;
            var text = (definition.InternalName + " " + definition.DisplayName + " " +
                        definition.DescriptionText + " " + definition.ToolTipText).ToLowerInvariant();
            if (((text.Contains("trim") && text.Contains("extend")) ||
                 (text.Contains("обрез") && text.Contains("удлин"))) &&
                !text.Contains("sketch") && !text.Contains("эскиз")) return button;
        }
        return null;
    }
}
