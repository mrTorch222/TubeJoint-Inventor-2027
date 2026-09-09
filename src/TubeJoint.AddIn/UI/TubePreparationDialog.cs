using System.Drawing;
using System.Windows.Forms;

namespace TubeJoint.AddIn.UI;

internal sealed class TubePreparationDialog : Form
{
    private static readonly Color PanelBack = Color.FromArgb(47, 57, 71);
    private static readonly Color HeaderBack = Color.FromArgb(65, 77, 95);
    private static readonly Color GridBack = Color.FromArgb(37, 47, 61);
    private static readonly Color TextColor = Color.FromArgb(235, 239, 244);
    private static readonly Color MutedText = Color.FromArgb(185, 194, 205);
    private static readonly Color Accent = Color.FromArgb(27, 159, 202);
    private readonly IReadOnlyList<TubePreparationViewRow> _rows;
    private readonly DataGridView _grid = new();
    private readonly CheckBox _renameFiles = new();
    private readonly Button _apply = new();

    public TubePreparationDialog(IReadOnlyList<TubePreparationViewRow> rows)
    {
        _rows = rows;
        Text = "Подготовка труб";
        StartPosition = FormStartPosition.CenterScreen;
        MinimumSize = new Size(900, 440);
        Size = new Size(1180, 620);
        BackColor = PanelBack;
        ForeColor = TextColor;
        Font = new Font("Segoe UI", 9F, FontStyle.Regular, GraphicsUnit.Point);
        ShowIcon = false;
        ShowInTaskbar = false;

        ConfigureGrid();
        ConfigureFooter();
        PopulateRows();
    }

    public IReadOnlySet<string> SelectedDocumentKeys { get; private set; } =
        new HashSet<string>(StringComparer.OrdinalIgnoreCase);

    public bool RenameFiles => _renameFiles.Checked;

    private void ConfigureGrid()
    {
        _grid.Dock = DockStyle.Fill;
        _grid.BackgroundColor = GridBack;
        _grid.BorderStyle = BorderStyle.None;
        _grid.AllowUserToAddRows = false;
        _grid.AllowUserToDeleteRows = false;
        _grid.AllowUserToOrderColumns = true;
        _grid.AllowUserToResizeRows = false;
        _grid.AutoGenerateColumns = false;
        _grid.AutoSizeRowsMode = DataGridViewAutoSizeRowsMode.AllCells;
        _grid.MultiSelect = false;
        _grid.RowHeadersVisible = false;
        _grid.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
        _grid.EnableHeadersVisualStyles = false;
        _grid.ColumnHeadersDefaultCellStyle.BackColor = HeaderBack;
        _grid.ColumnHeadersDefaultCellStyle.ForeColor = TextColor;
        _grid.ColumnHeadersDefaultCellStyle.SelectionBackColor = HeaderBack;
        _grid.ColumnHeadersHeight = 34;
        _grid.DefaultCellStyle.BackColor = GridBack;
        _grid.DefaultCellStyle.ForeColor = TextColor;
        _grid.DefaultCellStyle.SelectionBackColor = Color.FromArgb(60, 91, 112);
        _grid.DefaultCellStyle.SelectionForeColor = TextColor;
        _grid.DefaultCellStyle.WrapMode = DataGridViewTriState.False;
        _grid.GridColor = HeaderBack;

        _grid.Columns.Add(new DataGridViewCheckBoxColumn
        {
            Name = "Included",
            HeaderText = "✓",
            Width = 38,
            Resizable = DataGridViewTriState.False,
            SortMode = DataGridViewColumnSortMode.NotSortable
        });
        AddTextColumn("Current", "Текущее имя", 165);
        AddTextColumn("Measured", "Точный замер", 105);
        AddTextColumn("Nominal", "Размер", 90);
        AddTextColumn("Wall", "Стенка", 70);
        AddTextColumn("Quantity", "Кол-во", 60);
        AddTextColumn("Proposed", "Новое имя", 235);
        AddTextColumn("Status", "Статус", 220, DataGridViewAutoSizeColumnMode.Fill);
        Controls.Add(_grid);
    }

    private void ConfigureFooter()
    {
        var footer = new TableLayoutPanel
        {
            Dock = DockStyle.Bottom,
            Height = 104,
            ColumnCount = 2,
            RowCount = 2,
            BackColor = PanelBack,
            Padding = new Padding(12, 9, 12, 9)
        };
        footer.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        footer.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        footer.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        footer.RowStyles.Add(new RowStyle(SizeType.Absolute, 34));

        var explanation = new Label
        {
            AutoSize = true,
            ForeColor = MutedText,
            Text = "Выбранные трубы будут отцентрированы и направлены продольной осью вдоль +Z.\n" +
                   "Положение всех их вхождений в сборке сохранится.",
            Margin = new Padding(0, 2, 12, 4)
        };
        footer.Controls.Add(explanation, 0, 0);

        _renameFiles.Text = "Переименовать файлы";
        _renameFiles.Checked = false;
        _renameFiles.AutoSize = true;
        _renameFiles.ForeColor = TextColor;
        _renameFiles.FlatStyle = FlatStyle.Flat;
        _renameFiles.Margin = new Padding(0, 5, 12, 0);
        footer.Controls.Add(_renameFiles, 0, 1);

        var buttons = new FlowLayoutPanel
        {
            AutoSize = true,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false,
            Anchor = AnchorStyles.Right | AnchorStyles.Bottom,
            Margin = Padding.Empty
        };
        _apply.Text = "Применить";
        _apply.Size = new Size(104, 30);
        _apply.FlatStyle = FlatStyle.Flat;
        _apply.BackColor = Accent;
        _apply.ForeColor = Color.White;
        _apply.Click += ApplyClicked;
        var cancel = new Button
        {
            Text = "Отмена",
            DialogResult = DialogResult.Cancel,
            Size = new Size(92, 30),
            FlatStyle = FlatStyle.Flat,
            BackColor = HeaderBack,
            ForeColor = TextColor
        };
        buttons.Controls.Add(_apply);
        buttons.Controls.Add(cancel);
        footer.Controls.Add(buttons, 1, 1);
        Controls.Add(footer);

        AcceptButton = _apply;
        CancelButton = cancel;
    }

    private void PopulateRows()
    {
        foreach (var item in _rows)
        {
            var index = _grid.Rows.Add(
                item.CanPrepare,
                item.CurrentFileName,
                item.MeasuredSection,
                item.NominalSection,
                item.WallThickness,
                item.Quantity,
                item.ProposedFileName,
                item.Status);
            var row = _grid.Rows[index];
            row.Tag = item;
            if (item.CanPrepare) continue;
            row.Cells["Included"].ReadOnly = true;
            row.DefaultCellStyle.ForeColor = Color.FromArgb(150, 158, 169);
        }

        _apply.Enabled = _rows.Any(row => row.CanPrepare);
    }

    private void ApplyClicked(object? sender, EventArgs e)
    {
        var selected = _grid.Rows.Cast<DataGridViewRow>()
            .Where(row => Convert.ToBoolean(row.Cells["Included"].Value))
            .Select(row => (TubePreparationViewRow)row.Tag!)
            .ToList();
        if (selected.Count == 0)
        {
            MessageBox.Show(this, "Выберите хотя бы одну распознанную трубу.", Text,
                MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        if (_renameFiles.Checked && selected.Any(row => !row.CanRename))
        {
            MessageBox.Show(this,
                "Для переименования сначала сохраните все выбранные детали на диск.", Text,
                MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        SelectedDocumentKeys = selected.Select(row => row.DocumentKey)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        DialogResult = DialogResult.OK;
        Close();
    }

    private void AddTextColumn(
        string name,
        string header,
        int width,
        DataGridViewAutoSizeColumnMode autoSizeMode = DataGridViewAutoSizeColumnMode.None) =>
        _grid.Columns.Add(new DataGridViewTextBoxColumn
        {
            Name = name,
            HeaderText = header,
            Width = width,
            AutoSizeMode = autoSizeMode,
            ReadOnly = true,
            SortMode = DataGridViewColumnSortMode.Automatic
        });
}
