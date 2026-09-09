using System.Windows.Forms;
using Inventor;
using TubeJoint.AddIn.Services;
using TubeJoint.AddIn.TubeAnalysis;
using IOFile = System.IO.File;
using IOPath = System.IO.Path;

namespace TubeJoint.AddIn.Commands;

internal sealed class RecognizeTubeCommand
{
    private readonly Inventor.Application _application;
    private readonly ITubeAnalyzer _tubeAnalyzer;
    private readonly TubePreparationService _preparation;

    public RecognizeTubeCommand(Inventor.Application application)
    {
        _application = application;
        _tubeAnalyzer = new CachedTubeAnalyzer(new GenericSolidTubeAnalyzer());
        _preparation = new TubePreparationService(application);
    }

    public void Execute()
    {
        Inventor.Transaction? transaction = null;
        try
        {
            var context = GetTargets();
            var renamePlans = BuildRenamePlans(context.Targets);
            var lines = renamePlans.Take(8)
                .Select(plan => $"• {plan.Target.Document.DisplayName} → " +
                                IOPath.GetFileName(plan.TargetPath))
                .ToList();
            if (renamePlans.Count > lines.Count)
                lines.Add($"• …ещё {renamePlans.Count - lines.Count}");
            var details = string.Join("\n", lines);
            var assemblyText = context.Assembly is null
                ? string.Empty
                : $"\n\nВхождений в сборке: {context.OccurrenceCount}. " +
                  "Их положение будет сохранено обратной трансформацией.";
            var confirmation = MessageBox.Show(
                $"Распознано файлов труб: {context.Targets.Count}\n\n{details}\n\n" +
                "Трубы будут отцентрированы, направлены вдоль +Z, а iProperties заполнены.\n\n" +
                "Да — также назначить показанные имена файлов.\n" +
                "Нет — оставить текущие имена файлов.\n" +
                "Отмена — ничего не менять." + assemblyText,
                "Распознать и нормализовать трубу",
                MessageBoxButtons.YesNoCancel,
                MessageBoxIcon.Question,
                MessageBoxDefaultButton.Button2);
            if (confirmation == DialogResult.Cancel) return;
            var renameFiles = confirmation == DialogResult.Yes;

            transaction = context.Assembly is not null
                ? _application.TransactionManager.StartGlobalTransaction(
                    (Inventor._Document)context.Assembly,
                    "TubeJoint: нормализовать все трубы")
                : _application.TransactionManager.StartTransaction(
                    (Inventor._Document)context.Targets[0].Document,
                    "TubeJoint: распознать и нормализовать трубу");

            var snapshots = CaptureOccurrences(context.Targets);
            var movedCount = 0;
            foreach (var target in context.Targets)
            {
                var normalization = _preparation.CreateNormalizationTransform(target.Analysis);
                if (!_preparation.NormalizeAndWriteProperties(target.Document, target.Analysis)) continue;
                movedCount++;
                CompensateOccurrences(target.Occurrences, normalization);
            }
            if (context.Assembly is not null)
            {
                if (!context.Assembly.Update2(true))
                    throw new InvalidOperationException("Inventor сообщил об ошибке пересчёта сборки.");
                VerifyOccurrencePositions(snapshots);
            }
            transaction.End();
            transaction = null;

            var renamedCount = renameFiles ? ApplyRenames(renamePlans, context.Assembly) : 0;

            MessageBox.Show(
                $"Обработано файлов труб: {context.Targets.Count}.\n" +
                $"Нормализовано тел: {movedCount}.\n" +
                (renameFiles ? $"Назначено новых имён: {renamedCount}.\n" : "Имена файлов сохранены.\n") +
                (context.Assembly is null
                    ? "Тело выровнено по локальным осям XYZ."
                    : $"Положение {context.OccurrenceCount} вхождений в сборке сохранено.") +
                (renameFiles ? "\nСтарые IPT оставлены рядом как резервные копии." : string.Empty),
                "Распознавание трубы",
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);
        }
        catch (OperationCanceledException) { }
        catch (Exception exception)
        {
            try { transaction?.Abort(); } catch { }
            MessageBox.Show(exception.Message, "Распознавание трубы",
                MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private RecognitionContext GetTargets()
    {
        if (_application.ActiveDocument is PartDocument part)
            return new RecognitionContext(null,
                new List<TubeTarget> { new(part, _tubeAnalyzer.Analyze(part), new List<ComponentOccurrence>()) });
        if (_application.ActiveDocument is not AssemblyDocument assembly)
            throw new InvalidOperationException("Откройте деталь IPT или сборку IAM.");

        var groups = EnumerateLeafOccurrences(assembly.ComponentDefinition.Occurrences)
            .GroupBy(occurrence => DocumentKey(OccurrenceSelectionService.GetPartDocument(occurrence)),
                StringComparer.OrdinalIgnoreCase);
        var targets = new List<TubeTarget>();
        foreach (var group in groups)
        {
            var occurrences = group.ToList();
            try
            {
                var document = OccurrenceSelectionService.GetPartDocument(occurrences[0]);
                if (!document.IsModifiable) continue;
                targets.Add(new TubeTarget(document, _tubeAnalyzer.Analyze(document), occurrences));
            }
            catch
            {
                // Assemblies commonly contain plates, fasteners and reference
                // components. Only geometry positively recognized as a tube is changed.
            }
        }
        if (targets.Count == 0)
            throw new InvalidOperationException(
                "В сборке не найдено доступных прямых полых труб с одним solid-телом.");
        return new RecognitionContext(assembly, targets);
    }

    private static IEnumerable<ComponentOccurrence> EnumerateLeafOccurrences(
        System.Collections.IEnumerable occurrences)
    {
        foreach (var item in occurrences)
        {
            if (item is not ComponentOccurrence occurrence) continue;
            if (occurrence.Suppressed) continue;
            if (occurrence.DefinitionDocumentType == DocumentTypeEnum.kPartDocumentObject)
            {
                yield return occurrence;
                continue;
            }
            foreach (var child in EnumerateLeafOccurrences(occurrence.SubOccurrences))
                yield return child;
        }
    }

    private static string DocumentKey(PartDocument document) =>
        string.IsNullOrWhiteSpace(document.FullFileName)
            ? document.DisplayName
            : document.FullFileName;

    private static List<RenamePlan> BuildRenamePlans(IReadOnlyList<TubeTarget> targets)
    {
        foreach (var target in targets)
            if (string.IsNullOrWhiteSpace(target.Document.FullFileName))
                throw new InvalidOperationException(
                    $"Сначала сохраните деталь на диск: {target.Document.DisplayName}");

        var plans = new List<RenamePlan>();
        var reserved = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var target in targets.OrderBy(item => item.Document.FullFileName,
                     StringComparer.OrdinalIgnoreCase))
        {
            var suffix = 1;
            string targetPath;
            do
            {
                var fileName = BuildTubeFileName(target, suffix++);
                targetPath = IOPath.Combine(
                    IOPath.GetDirectoryName(target.Document.FullFileName)!, fileName);
            }
            while (reserved.Contains(targetPath) ||
                   (IOFile.Exists(targetPath) &&
                    !string.Equals(targetPath, target.Document.FullFileName,
                        StringComparison.OrdinalIgnoreCase)));

            reserved.Add(targetPath);
            plans.Add(new RenamePlan(target, target.Document.FullFileName, targetPath));
        }
        return plans;
    }

    private static string BuildTubeFileName(TubeTarget target, int collisionSuffix)
    {
        var tube = target.Analysis;
        var size = tube.SectionKind == TubeSectionKind.Round
            ? $"Ø{TubePreparationService.Format(tube.NominalWidthMm)}"
            : $"{TubePreparationService.Format(tube.NominalWidthMm)}x" +
              $"{TubePreparationService.Format(tube.NominalHeightMm)}";
        var quantity = Math.Max(1, target.Occurrences.Count);
        var originalName = GetOriginalFileName(target.Document);
        var collision = collisionSuffix > 1 ? $"_{collisionSuffix}" : string.Empty;
        var name = $"{size}x{TubePreparationService.Format(tube.NominalWallThicknessMm)}" +
                   $"_{originalName}_{quantity} шт{collision}";
        foreach (var invalid in IOPath.GetInvalidFileNameChars()) name = name.Replace(invalid, '_');
        return name + ".ipt";
    }

    private static string GetOriginalFileName(PartDocument document)
    {
        var custom = document.PropertySets["{D5CDD505-2E9C-101B-9397-08002B2CF9AE}"];
        try
        {
            var value = Convert.ToString(custom["TubeJoint.OriginalFileName"].Value)?.Trim();
            if (!string.IsNullOrWhiteSpace(value)) return value;
        }
        catch { }
        return IOPath.GetFileNameWithoutExtension(document.FullFileName);
    }

    private static int ApplyRenames(IEnumerable<RenamePlan> plans, AssemblyDocument? assembly)
    {
        var count = 0;
        foreach (var plan in plans)
        {
            if (string.Equals(plan.SourcePath, plan.TargetPath, StringComparison.OrdinalIgnoreCase))
                continue;
            plan.Target.Document.SaveAs(plan.TargetPath, false);
            count++;
        }

        if (assembly is not null) assembly.Save2(true, Type.Missing);
        else
        {
            var part = plans.First().Target.Document;
            part.Save2(true, Type.Missing);
        }
        return count;
    }

    private static List<OccurrenceSnapshot> CaptureOccurrences(IEnumerable<TubeTarget> targets) =>
        targets.SelectMany(target => target.Occurrences).Select(occurrence =>
        {
            var box = occurrence.PreciseRangeBox;
            return new OccurrenceSnapshot(
                occurrence,
                box.MinPoint.X, box.MinPoint.Y, box.MinPoint.Z,
                box.MaxPoint.X, box.MaxPoint.Y, box.MaxPoint.Z);
        }).ToList();

    private static void CompensateOccurrences(
        IEnumerable<ComponentOccurrence> occurrences, Matrix normalization)
    {
        var inverse = normalization.Copy();
        inverse.Invert();
        foreach (var occurrence in occurrences)
        {
            // Component point before: T_old * p. Body point after: M * p.
            // T_new = T_old * M^-1 keeps T_new * M * p == T_old * p.
            var compensated = occurrence.Transformation.Copy();
            compensated.PostMultiplyBy(inverse);
            occurrence.SetTransformWithoutConstraints(compensated);
        }
    }

    private static void VerifyOccurrencePositions(IEnumerable<OccurrenceSnapshot> snapshots)
    {
        const double toleranceCm = 0.005; // 0.05 mm
        foreach (var snapshot in snapshots)
        {
            var box = snapshot.Occurrence.PreciseRangeBox;
            var maximumError = new[]
            {
                Math.Abs(box.MinPoint.X - snapshot.MinX),
                Math.Abs(box.MinPoint.Y - snapshot.MinY),
                Math.Abs(box.MinPoint.Z - snapshot.MinZ),
                Math.Abs(box.MaxPoint.X - snapshot.MaxX),
                Math.Abs(box.MaxPoint.Y - snapshot.MaxY),
                Math.Abs(box.MaxPoint.Z - snapshot.MaxZ)
            }.Max();
            if (maximumError <= toleranceCm) continue;
            throw new InvalidOperationException(
                $"Не удалось сохранить положение '{snapshot.Occurrence.Name}' в сборке. " +
                "Все изменения отменены; проверьте зависимости или заземление этого вхождения.");
        }
    }

    private sealed record TubeTarget(
        PartDocument Document,
        TubeAnalysisResult Analysis,
        List<ComponentOccurrence> Occurrences);

    private sealed record RenamePlan(
        TubeTarget Target, string SourcePath, string TargetPath);

    private sealed record RecognitionContext(
        AssemblyDocument? Assembly, List<TubeTarget> Targets)
    {
        public int OccurrenceCount => Targets.Sum(target => target.Occurrences.Count);
    }

    private sealed record OccurrenceSnapshot(
        ComponentOccurrence Occurrence,
        double MinX, double MinY, double MinZ,
        double MaxX, double MaxY, double MaxZ);
}
