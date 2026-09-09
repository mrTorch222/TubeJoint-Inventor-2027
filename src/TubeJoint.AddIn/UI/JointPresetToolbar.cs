using System.Drawing;
using System.Windows.Forms;
using TubeJoint.AddIn.Models;

namespace TubeJoint.AddIn.UI;

/// <summary>Inventor-style preset and sketch selectors with explicit action buttons.</summary>
internal sealed class JointPresetToolbar : UserControl
{
    private readonly ComboBox _presets = Selector();
    private readonly ComboBox _templates = Selector();
    private readonly Button _addPreset = IconButton("+");
    private readonly Button _options = IconButton("⚙");
    private readonly Button _addTemplate = IconButton("+");
    private readonly Button _editTemplate = IconButton("✎");
    private readonly ContextMenuStrip _menu = new();
    private readonly ToolStripMenuItem _saveCurrent;
    private readonly ToolStripMenuItem _renameCurrent;
    private readonly ToolStripMenuItem _deleteCurrent;
    private readonly Dictionary<JointPresetSortOrder, ToolStripMenuItem> _sortItems = new();
    private readonly Dictionary<JointPresetStartupMode, ToolStripMenuItem> _startupItems = new();
    private bool _binding;

    public JointPresetToolbar()
    {
        Height = 54;
        Margin = Padding.Empty;
        BackColor = Palette.PanelBack;

        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 3,
            RowCount = 2,
            BackColor = Palette.PanelBack,
            Margin = Padding.Empty,
            Padding = Padding.Empty
        };
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 27));
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 27));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 26));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 26));
        root.Controls.Add(_presets, 0, 0);
        root.Controls.Add(_addPreset, 1, 0);
        root.Controls.Add(_options, 2, 0);
        root.Controls.Add(_templates, 0, 1);
        root.Controls.Add(_addTemplate, 1, 1);
        root.Controls.Add(_editTemplate, 2, 1);
        Controls.Add(root);

        _addPreset.AccessibleName = "Сохранить новый пресет";
        _options.AccessibleName = "Действия с пресетом";
        _addTemplate.AccessibleName = "Создать вариант эскиза";
        _editTemplate.AccessibleName = "Открыть выбранный эскиз";
        new ToolTip().SetToolTip(_addPreset, "Сохранить текущие настройки как новый пресет");
        new ToolTip().SetToolTip(_options, "Управление пресетами");
        new ToolTip().SetToolTip(_addTemplate, "Создать копию выбранного варианта эскиза");
        new ToolTip().SetToolTip(_editTemplate, "Открыть выбранный вариант эскиза в Inventor");
        _options.BackColor = Palette.Accent;

        _addPreset.Click += (_, _) => SaveAsRequested?.Invoke(this, EventArgs.Empty);
        _options.Click += (_, _) =>
        {
            RefreshMenuState();
            var menuSize = _menu.GetPreferredSize(Size.Empty);
            _menu.Show(_options, new Point(_options.Width - menuSize.Width, _options.Height));
        };
        _addTemplate.Click += (_, _) => CreateTemplateRequested?.Invoke(this, EventArgs.Empty);
        _editTemplate.Click += (_, _) => EditTemplateRequested?.Invoke(this, EventArgs.Empty);
        _presets.SelectedIndexChanged += (_, _) =>
        {
            if (!_binding) PresetChanged?.Invoke(this, EventArgs.Empty);
        };
        _templates.SelectedIndexChanged += (_, _) =>
        {
            if (!_binding) TemplateChanged?.Invoke(this, EventArgs.Empty);
        };

        ConfigureMenu();
        _saveCurrent = MenuItem("▣  Сохранить текущий", (_, _) =>
            SaveCurrentRequested?.Invoke(this, EventArgs.Empty));
        _renameCurrent = MenuItem("✎  Переименовать", (_, _) =>
            RenameRequested?.Invoke(this, EventArgs.Empty));
        _deleteCurrent = MenuItem("▱  Удалить", (_, _) =>
            DeleteRequested?.Invoke(this, EventArgs.Empty));
        _menu.Items.AddRange(new ToolStripItem[]
        {
            _saveCurrent, _renameCurrent, _deleteCurrent, new ToolStripSeparator()
        });

        var sort = new ToolStripMenuItem("Порядок сортировки");
        AddSort(sort, JointPresetSortOrder.RecentlyUsed, "Недавно использованные");
        AddSort(sort, JointPresetSortOrder.Created, "Дата создания");
        AddSort(sort, JointPresetSortOrder.Alphabetical, "По алфавиту");
        AddSort(sort, JointPresetSortOrder.Modified, "Последние изменённые");
        _menu.Items.Add(sort);

        var startup = new ToolStripMenuItem("Новый шип-паз");
        AddStartup(startup, JointPresetStartupMode.LastUsed, "Последний использованный");
        AddStartup(startup, JointPresetStartupMode.SpecificPreset, "Использовать текущий пресет");
        AddStartup(startup, JointPresetStartupMode.NoPreset, "Без пресета");
        _menu.Items.Add(startup);
    }

    public event EventHandler? PresetChanged;
    public event EventHandler? TemplateChanged;
    public event EventHandler? SaveAsRequested;
    public event EventHandler? SaveCurrentRequested;
    public event EventHandler? RenameRequested;
    public event EventHandler? DeleteRequested;
    public event EventHandler? CreateTemplateRequested;
    public event EventHandler? EditTemplateRequested;
    public event Action<JointPresetSortOrder>? SortOrderRequested;
    public event Action<JointPresetStartupMode>? StartupModeRequested;

    public JointPreset? SelectedPreset => (_presets.SelectedItem as PresetItem)?.Preset;
    public JointTemplateDescriptor? SelectedTemplate => _templates.SelectedItem as JointTemplateDescriptor;

    public void Bind(
        JointPresetCatalog catalog,
        IReadOnlyList<JointTemplateDescriptor> templates,
        string? selectedPresetId,
        string? selectedTemplateFileName)
    {
        _binding = true;
        try
        {
            _presets.Items.Clear();
            _presets.Items.Add(new PresetItem(null, "Без пресета"));
            foreach (var preset in Services.JointPresetStore.Sort(catalog))
                _presets.Items.Add(new PresetItem(preset, preset.Name));
            _presets.SelectedItem = _presets.Items.Cast<PresetItem>()
                .FirstOrDefault(item => item.Preset?.Id == selectedPresetId) ?? _presets.Items[0];

            _templates.Items.Clear();
            foreach (var template in templates)
                _templates.Items.Add(template);
            _templates.SelectedItem = templates.FirstOrDefault(item =>
                string.Equals(item.FileName, selectedTemplateFileName,
                    StringComparison.OrdinalIgnoreCase)) ?? templates.FirstOrDefault();

            foreach (var pair in _sortItems)
                pair.Value.Checked = pair.Key == catalog.SortOrder;
            foreach (var pair in _startupItems)
                pair.Value.Checked = pair.Key == catalog.StartupMode;
        }
        finally { _binding = false; }
        RefreshMenuState();
    }

    public void SelectNoPreset()
    {
        if (_binding || _presets.SelectedIndex == 0) return;
        _binding = true;
        try { _presets.SelectedIndex = 0; }
        finally { _binding = false; }
        RefreshMenuState();
    }

    public void SelectTemplate(string fileName)
    {
        var template = _templates.Items.Cast<JointTemplateDescriptor>()
            .FirstOrDefault(item => string.Equals(
                item.FileName, fileName, StringComparison.OrdinalIgnoreCase));
        if (template is not null)
            _templates.SelectedItem = template;
    }

    private void ConfigureMenu()
    {
        _menu.BackColor = Palette.SectionBack;
        _menu.ForeColor = Palette.TextColor;
        _menu.ShowImageMargin = true;
        _menu.Font = new Font("Segoe UI", 9F);
    }

    private void RefreshMenuState()
    {
        var hasPreset = SelectedPreset is not null;
        _saveCurrent.Enabled = hasPreset;
        _renameCurrent.Enabled = hasPreset;
        _deleteCurrent.Enabled = hasPreset;
        if (_startupItems.TryGetValue(JointPresetStartupMode.SpecificPreset, out var current))
            current.Enabled = hasPreset;
    }

    private void AddSort(ToolStripMenuItem parent, JointPresetSortOrder order, string text)
    {
        var item = MenuItem(text, (_, _) => SortOrderRequested?.Invoke(order));
        item.CheckOnClick = false;
        _sortItems.Add(order, item);
        parent.DropDownItems.Add(item);
    }

    private void AddStartup(ToolStripMenuItem parent, JointPresetStartupMode mode, string text)
    {
        var item = MenuItem(text, (_, _) => StartupModeRequested?.Invoke(mode));
        item.CheckOnClick = false;
        _startupItems.Add(mode, item);
        parent.DropDownItems.Add(item);
    }

    private static ComboBox Selector()
    {
        var result = new ComboBox
        {
            Dock = DockStyle.Fill,
            DropDownStyle = ComboBoxStyle.DropDownList,
            FlatStyle = FlatStyle.Flat,
            BackColor = Palette.EditorBack,
            ForeColor = Palette.TextColor,
            DrawMode = DrawMode.OwnerDrawFixed,
            ItemHeight = 18,
            Margin = new Padding(2, 1, 1, 1)
        };
        result.DrawItem += DrawSelectorItem;
        return result;
    }

    private static void DrawSelectorItem(object? sender, DrawItemEventArgs e)
    {
        if (sender is not ComboBox combo || e.Index < 0) return;
        var selectedInOpenList = combo.DroppedDown &&
                                 (e.State & DrawItemState.Selected) != 0;
        using var background = new SolidBrush(
            selectedInOpenList ? Palette.Accent : Palette.EditorBack);
        e.Graphics.FillRectangle(background, e.Bounds);
        TextRenderer.DrawText(
            e.Graphics,
            Convert.ToString(combo.Items[e.Index]) ?? string.Empty,
            combo.Font,
            new Rectangle(e.Bounds.X + 4, e.Bounds.Y, e.Bounds.Width - 6, e.Bounds.Height),
            Palette.TextColor,
            TextFormatFlags.Left | TextFormatFlags.VerticalCenter |
            TextFormatFlags.NoPadding | TextFormatFlags.EndEllipsis);
    }

    private static Button IconButton(string text)
    {
        var result = new Button
        {
            Text = text,
            Dock = DockStyle.Fill,
            FlatStyle = FlatStyle.Flat,
            BackColor = Palette.PanelBack,
            ForeColor = Palette.TextColor,
            Font = new Font("Segoe UI Symbol", 10F),
            Margin = new Padding(1),
            UseVisualStyleBackColor = false
        };
        result.FlatAppearance.BorderSize = 0;
        result.FlatAppearance.MouseOverBackColor = Palette.SectionBack;
        result.FlatAppearance.MouseDownBackColor = Palette.Accent;
        return result;
    }

    private static ToolStripMenuItem MenuItem(string text, EventHandler clicked)
    {
        var result = new ToolStripMenuItem(text);
        result.Click += clicked;
        return result;
    }

    private static InventorThemePalette Palette => InventorThemePalette.Current;

    private sealed record PresetItem(JointPreset? Preset, string Text)
    {
        public override string ToString() => Text;
    }
}
