using System.Windows.Forms;
using Inventor;
using TubeJoint.AddIn.Services;

namespace TubeJoint.AddIn.Commands;

internal sealed class UpdateTubeJointsCommand
{
    private readonly Inventor.Application _application;
    private readonly JointRepository _repository = new();
    public UpdateTubeJointsCommand(Inventor.Application application) => _application = application;

    public void Execute()
    {
        if (_application.ActiveDocument is not AssemblyDocument assembly)
        {
            MessageBox.Show("Откройте сборку IAM.", "Шип-паз труб");
            return;
        }
        var records = _repository.ReadAll(assembly);
        var missing = records.Count(record =>
            !System.IO.File.Exists(record.MaleDocumentPath) || !System.IO.File.Exists(record.FemaleDocumentPath));
        var angled = records.Count(record => record.InsertionDeviationDegrees > 0.5);
        MessageBox.Show(
            $"Соединений записано: {records.Count}\n" +
            $"С потерянными файлами: {missing}\n" +
            $"Наклонных (компенсация позже): {angled}\n\n" +
            "В этой итерации команда проверяет записи; повторное перестроение будет добавлено после проверки базовой геометрии.",
            "Проверка соединений");
    }
}
