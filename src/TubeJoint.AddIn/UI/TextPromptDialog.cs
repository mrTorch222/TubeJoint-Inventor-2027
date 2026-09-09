using System.Drawing;
using System.Windows.Forms;

namespace TubeJoint.AddIn.UI;

internal sealed class TextPromptDialog : Form
{
    private readonly TextBox _value;

    private TextPromptDialog(string title, string prompt, string initialValue)
    {
        var palette = InventorThemePalette.Current;
        Text = title;
        StartPosition = FormStartPosition.CenterScreen;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        ClientSize = new Size(390, 126);
        MaximizeBox = false;
        MinimizeBox = false;
        ShowIcon = false;
        ShowInTaskbar = false;
        BackColor = palette.PanelBack;
        ForeColor = palette.TextColor;
        Font = new Font("Segoe UI", 9F);

        var label = new Label
        {
            Text = prompt,
            Location = new Point(12, 12),
            Size = new Size(366, 20),
            ForeColor = palette.TextColor
        };
        _value = new TextBox
        {
            Text = initialValue,
            Location = new Point(12, 37),
            Size = new Size(366, 24),
            BorderStyle = BorderStyle.FixedSingle,
            BackColor = palette.EditorBack,
            ForeColor = palette.TextColor
        };
        var ok = Button("OK", palette.Accent, 202, DialogResult.OK);
        var cancel = Button("Отмена", palette.SectionBack, 294, DialogResult.Cancel);
        Controls.AddRange(new Control[] { label, _value, ok, cancel });
        AcceptButton = ok;
        CancelButton = cancel;
        Shown += (_, _) =>
        {
            _value.Focus();
            _value.SelectAll();
        };
    }

    public static string? Show(string title, string prompt, string initialValue = "")
    {
        using var dialog = new TextPromptDialog(title, prompt, initialValue);
        if (dialog.ShowDialog() != DialogResult.OK)
            return null;
        var value = dialog._value.Text.Trim();
        return string.IsNullOrWhiteSpace(value) ? null : value;
    }

    private static Button Button(
        string text, Color background, int left, DialogResult result)
    {
        var palette = InventorThemePalette.Current;
        var button = new Button
        {
            Text = text,
            Location = new Point(left, 78),
            Size = new Size(84, 29),
            DialogResult = result,
            FlatStyle = FlatStyle.Flat,
            BackColor = background,
            ForeColor = palette.TextColor,
            UseVisualStyleBackColor = false
        };
        button.FlatAppearance.BorderColor = palette.Border;
        return button;
    }
}
