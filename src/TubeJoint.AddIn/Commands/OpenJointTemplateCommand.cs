using System.Windows.Forms;
using Inventor;
using TubeJoint.AddIn.Services;

namespace TubeJoint.AddIn.Commands;

internal sealed class OpenJointTemplateCommand
{
    private readonly JointTemplateService _templates;
    public OpenJointTemplateCommand(Inventor.Application application) =>
        _templates = new JointTemplateService(application);

    public void Execute()
    {
        try { _templates.OpenTemplateForEditing(); }
        catch (Exception exception)
        {
            MessageBox.Show(exception.Message, "Шип-паз труб", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }
}
