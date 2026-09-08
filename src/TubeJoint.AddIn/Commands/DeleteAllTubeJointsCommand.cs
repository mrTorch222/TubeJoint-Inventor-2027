using System.Windows.Forms;
using Inventor;
using TubeJoint.AddIn.Services;

namespace TubeJoint.AddIn.Commands;

internal sealed class DeleteAllTubeJointsCommand
{
    private readonly Inventor.Application _application;
    private readonly JointRepository _repository = new();

    public DeleteAllTubeJointsCommand(Inventor.Application application) => _application = application;

    public void Execute()
    {
        if (_application.ActiveDocument is not AssemblyDocument assembly)
        {
            MessageBox.Show("Откройте сборку IAM.", "Шип-паз труб");
            return;
        }

        var records = _repository.ReadAll(assembly).Count;
        var features = CountJointFeatures(assembly);
        if (records == 0 && features == 0)
        {
            MessageBox.Show("В текущей сборке не найдено соединений TubeJoint.", "Шип-паз труб");
            return;
        }

        var confirmation = MessageBox.Show(
            $"Удалить все соединения TubeJoint в этой сборке?\n\n" +
            $"Записей: {records}\nОпераций в деталях: {features}\n\n" +
            "Frame Generator Trim/Notch и другая геометрия не удаляются.",
            "Удалить все соединения",
            MessageBoxButtons.YesNo,
            MessageBoxIcon.Warning,
            MessageBoxDefaultButton.Button2);
        if (confirmation != DialogResult.Yes) return;

        Inventor.Transaction? transaction = null;
        try
        {
            transaction = _application.TransactionManager.StartGlobalTransaction(
                AsDocument(assembly), "TubeJoint: удалить все соединения");

            foreach (var document in ReferencedPartDocuments(assembly))
            {
                if (!ContainsJointFeatures(document)) continue;
                if (!document.IsModifiable)
                    throw new InvalidOperationException($"Нельзя изменить деталь: {document.DisplayName}");
                DeleteJointFeatures(document);
                document.Update2(true);
            }

            _repository.DeleteAll(assembly);
            assembly.Update2(true);
            transaction.End();
            transaction = null;
        }
        catch (Exception exception)
        {
            try { transaction?.Abort(); } catch { }
            transaction = null;
            MessageBox.Show(exception.Message, "Шип-паз труб", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private static int CountJointFeatures(AssemblyDocument assembly) =>
        ReferencedPartDocuments(assembly).Sum(document =>
        {
            var count = 0;
            foreach (ExtrudeFeature feature in document.ComponentDefinition.Features.ExtrudeFeatures)
                if (IsJointFeature(feature.Name)) count++;
            return count;
        });

    private static bool ContainsJointFeatures(PartDocument document)
    {
        foreach (ExtrudeFeature feature in document.ComponentDefinition.Features.ExtrudeFeatures)
            if (IsJointFeature(feature.Name)) return true;
        foreach (PlanarSketch sketch in document.ComponentDefinition.Sketches)
            if (IsJointSketch(sketch.Name)) return true;
        return false;
    }

    private static void DeleteJointFeatures(PartDocument document)
    {
        var definition = document.ComponentDefinition;
        var extrudes = definition.Features.ExtrudeFeatures;
        for (var index = extrudes.Count; index >= 1; index--)
        {
            var feature = extrudes[index];
            if (IsJointFeature(feature.Name))
                feature.Delete(false, false, false);
        }

        // Normally consumed sketches are removed with their extrusion. This pass
        // also removes leftovers from interrupted prototype builds.
        var sketches = definition.Sketches;
        for (var index = sketches.Count; index >= 1; index--)
        {
            var sketch = sketches[index];
            if (!IsJointSketch(sketch.Name)) continue;
            try { sketch.Delete(); } catch { }
        }
    }

    private static IReadOnlyList<PartDocument> ReferencedPartDocuments(AssemblyDocument assembly)
    {
        var result = new List<PartDocument>();
        var paths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (Document document in assembly.AllReferencedDocuments)
        {
            if (document is not PartDocument part) continue;
            var key = string.IsNullOrWhiteSpace(part.FullFileName) ? part.DisplayName : part.FullFileName;
            if (paths.Add(key)) result.Add(part);
        }
        return result;
    }

    private static bool IsJointFeature(string name) =>
        name.StartsWith("TJ_TENON_", StringComparison.OrdinalIgnoreCase) ||
        name.StartsWith("TJ_SLOT_", StringComparison.OrdinalIgnoreCase) ||
        name.StartsWith("TJ_VENT_CUT_", StringComparison.OrdinalIgnoreCase);

    private static bool IsJointSketch(string name) =>
        name.StartsWith("TJ_MALE_", StringComparison.OrdinalIgnoreCase) ||
        name.StartsWith("TJ_FEMALE_", StringComparison.OrdinalIgnoreCase) ||
        name.StartsWith("TJ_VENT_", StringComparison.OrdinalIgnoreCase);

    private static Inventor._Document AsDocument(object document) => (Inventor._Document)document;
}
