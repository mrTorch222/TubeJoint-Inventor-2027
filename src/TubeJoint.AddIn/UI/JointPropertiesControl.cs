using System.Drawing;
using System.Windows.Forms;
using TubeJoint.AddIn.Models;

namespace TubeJoint.AddIn.UI;

internal sealed class JointPropertiesControl : UserControl
{
    private static readonly Color PanelBack = Color.FromArgb(47, 57, 71);
    private static readonly Color SectionBack = Color.FromArgb(65, 77, 95);
    private static readonly Color EditorBack = Color.FromArgb(37, 47, 61);
    private static readonly Color Border = Color.FromArgb(83, 97, 116);
    private static readonly Color TextColor = Color.FromArgb(235, 239, 244);
    private static readonly Color MutedText = Color.FromArgb(185, 194, 205);
    private static readonly Color Accent = Color.FromArgb(27, 159, 202);

    private readonly FlatNumericBox _tenonWidth;
    private readonly FlatNumericBox _tenonHeight;
    private readonly ComboBox _preset = new();
    private readonly CheckBox _autoWidth = AutoCheckBox();
    private readonly CheckBox _autoHeight = AutoCheckBox();
    private readonly FlatNumericBox _clearance = Numeric(0.20m, 0m, 20m, 0.05m, 2);
    private readonly RadioButton _sideA = SideButton("A");
    private readonly RadioButton _sideB = SideButton("B");
    private readonly RadioButton _bothSides = SideButton("A+B");
    private readonly Button _rotatePair = SmallButton("↻ 90°");
    private readonly FlatNumericBox _jointOffset = Numeric(0m, -500m, 500m, 0.5m, 1);
    private readonly FlatNumericBox _reliefFactor = Numeric(35m, 0m, 200m, 5m, 0);
    private readonly CheckBox _centerVent;
    private readonly FlatNumericBox _holeOffsetX = Numeric(0m, -500m, 500m, 0.5m, 1);
    private readonly FlatNumericBox _holeOffsetY = Numeric(0m, -500m, 500m, 0.5m, 1);
    private readonly FlatNumericBox _ventScale = Numeric(100m, 10m, 500m, 5m, 0);
    private readonly CheckBox _holeManipulator = ToggleButton("3D-манипулятор отверстия");
    private readonly Label _previewWarning = new()
    {
        AutoSize = true,
        ForeColor = Color.FromArgb(255, 190, 90),
        MaximumSize = new Size(570, 0),
        Margin = new Padding(5, 2, 5, 4),
        Visible = false
    };
    private readonly TableLayoutPanel _root;
    private readonly JointStandardSettings _settings;
    private bool _applyingPreset;
    private bool _settingManipulatorValues;

    public JointPropertiesControl(JointPairSelection selection, JointStandardSettings settings)
    {
        _settings = settings;
        var automaticWidth = CalculateAutomaticWidth(selection, settings);
        var automaticHeight = CalculateAutomaticHeight(selection, settings);
        var dimensionStep = (decimal)Math.Max(0.1, settings.DimensionStepMm);
        _tenonWidth = Numeric((decimal)automaticWidth, 1m, 500m, dimensionStep, 1);
        _tenonHeight = Numeric((decimal)automaticHeight, 1m, 500m, dimensionStep, 1);
        // Automatic rules remain available, but standards/presets are the default workflow.
        _autoWidth.Checked = false;
        _autoHeight.Checked = false;
        _tenonWidth.Enabled = true;
        _tenonHeight.Enabled = true;
        _autoWidth.CheckedChanged += (_, _) =>
        {
            _tenonWidth.Enabled = !_autoWidth.Checked;
            ParametersChanged?.Invoke(this, EventArgs.Empty);
        };
        _autoHeight.CheckedChanged += (_, _) =>
        {
            _tenonHeight.Enabled = !_autoHeight.Checked;
            ParametersChanged?.Invoke(this, EventArgs.Empty);
        };

        BackColor = PanelBack;
        ForeColor = TextColor;
        Font = new Font("Segoe UI", 8.25F, FontStyle.Regular, GraphicsUnit.Point);
        Dock = DockStyle.Fill;
        AutoScroll = true;
        MinimumSize = new Size(420, 650);

        _root = new TableLayoutPanel
        {
            Dock = DockStyle.Top,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            ColumnCount = 1,
            RowCount = 0,
            GrowStyle = TableLayoutPanelGrowStyle.AddRows,
            BackColor = PanelBack,
            Padding = new Padding(7, 6, 7, 8),
            Margin = Padding.Empty
        };
        _root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));

        AddRoot(new LinkLabel
        {
            Text = "Шип-паз труб",
            LinkColor = Accent,
            ActiveLinkColor = Accent,
            VisitedLinkColor = Accent,
            AutoSize = true,
            Margin = new Padding(2, 2, 2, 5)
        }, SizeType.AutoSize);

        ConfigurePresets(automaticWidth, automaticHeight);
        AddRoot(_preset, SizeType.Absolute, 27);

        AddRoot(Section("▼  Выбор"), SizeType.Absolute, 23);
        AddRoot(ReadOnlyRow(
            $"Шип · {selection.MaleWallThicknessMm:0.###} мм",
            selection.Male.Occurrence.Name), SizeType.Absolute, 29);
        AddRoot(ReadOnlyRow(
            $"Паз · {selection.FemaleWallThicknessMm:0.###} мм",
            selection.Female.Occurrence.Name), SizeType.Absolute, 29);
        AddRoot(new Label
        {
            Text = $"Угол: {selection.InsertionDeviationDegrees:0.0}°  ·  " +
                   $"зазор торца A/B: {selection.SideAGapMm:0.##}/{selection.SideBGapMm:0.##} мм",
            ForeColor = MutedText,
            TextAlign = ContentAlignment.MiddleLeft,
            Margin = new Padding(5, 0, 2, 4)
        }, SizeType.Absolute, 23);

        AddRoot(Section("▼  Стороны"), SizeType.Absolute, 23);
        var sides = new TableLayoutPanel
        {
            ColumnCount = 4,
            RowCount = 1,
            Margin = new Padding(2, 3, 2, 4),
            BackColor = PanelBack
        };
        sides.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 25F));
        sides.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 25F));
        sides.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 25F));
        sides.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 25F));
        sides.Controls.Add(_sideA, 0, 0);
        sides.Controls.Add(_sideB, 1, 0);
        sides.Controls.Add(_bothSides, 2, 0);
        sides.Controls.Add(_rotatePair, 3, 0);
        _rotatePair.Enabled = selection.RotatedPair is not null;
        new ToolTip().SetToolTip(_rotatePair,
            _rotatePair.Enabled
                ? "Переключить на другую пару противоположных стенок"
                : "Для этой трубы не найдена вторая пара наружных стенок.");
        _rotatePair.Click += (_, _) => RotationRequested?.Invoke(this, EventArgs.Empty);
        _bothSides.Checked = true;
        _sideA.CheckedChanged += SideCheckedChanged;
        _sideB.CheckedChanged += SideCheckedChanged;
        _bothSides.CheckedChanged += SideCheckedChanged;
        AddRoot(sides, SizeType.Absolute, 34);

        AddRoot(Section("▼  Размеры"), SizeType.Absolute, 23);
        AddRoot(new JointDiagramControl(
            _reliefFactor,
            _ventScale,
            _tenonHeight,
            _tenonWidth,
            _clearance), SizeType.Absolute, 260);
        AddRoot(_previewWarning, SizeType.AutoSize);
        var automatic = new FlowLayoutPanel
        {
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false,
            Margin = new Padding(5, 0, 2, 2),
            BackColor = PanelBack
        };
        _autoWidth.Text = "Авто X";
        _autoHeight.Text = "Авто Y";
        automatic.Controls.Add(_autoWidth);
        automatic.Controls.Add(_autoHeight);
        AddRoot(automatic, SizeType.Absolute, 24);
        AddRoot(new Label
        {
            Text = $"Сечение трубы: {selection.MaleCrossSpanMm:0.#} × {selection.MaleProfileSpanMm:0.#} мм",
            ForeColor = MutedText,
            TextAlign = ContentAlignment.MiddleLeft,
            Margin = new Padding(5, 0, 2, 4)
        }, SizeType.Absolute, 22);

        AddRoot(Section("▼  Размещение"), SizeType.Absolute, 23);
        AddRoot(EditorRow("Смещение вдоль", _jointOffset, "мм"), SizeType.Absolute, 29);

        AddRoot(Section("▼  Дополнительно"), SizeType.Absolute, 23);
        _centerVent = new CheckBox
        {
            Text = "Отверстие",
            Checked = true,
            ForeColor = TextColor,
            FlatStyle = FlatStyle.Flat,
            Margin = new Padding(5, 3, 2, 3)
        };
        AddRoot(_centerVent, SizeType.Absolute, 27);
        AddRoot(CompactHoleOffsets(_holeOffsetX, _holeOffsetY), SizeType.Absolute, 27);
        AddRoot(CompactCentered(_holeManipulator, 210), SizeType.Absolute, 29);

        var buttons = new FlowLayoutPanel
        {
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false,
            Padding = new Padding(0, 3, 0, 0),
            Margin = new Padding(2)
        };
        var ok = ActionButton("OK", Color.FromArgb(26, 126, 166));
        var cancel = ActionButton("Cancel", Color.FromArgb(54, 64, 79));
        ok.Click += (_, _) => Accepted?.Invoke(this, EventArgs.Empty);
        cancel.Click += (_, _) => Cancelled?.Invoke(this, EventArgs.Empty);
        buttons.Controls.Add(ok);
        buttons.Controls.Add(cancel);
        AddRoot(buttons, SizeType.Absolute, 36);

        Controls.Add(_root);

        _tenonWidth.ValueChanged += ParameterValueChanged;
        _tenonHeight.ValueChanged += ParameterValueChanged;
        _clearance.ValueChanged += ParameterValueChanged;
        _jointOffset.ValueChanged += JointOffsetValueChanged;
        _centerVent.CheckedChanged += ParameterValueChanged;
        _centerVent.CheckedChanged += (_, _) =>
        {
            _holeOffsetX.Enabled = _centerVent.Checked;
            _holeOffsetY.Enabled = _centerVent.Checked;
            _ventScale.Enabled = _centerVent.Checked;
            _holeManipulator.Enabled = _centerVent.Checked;
            if (!_centerVent.Checked && _holeManipulator.Checked)
                _holeManipulator.Checked = false;
        };
        _holeOffsetX.ValueChanged += HoleOffsetValueChanged;
        _holeOffsetY.ValueChanged += HoleOffsetValueChanged;
        _reliefFactor.ValueChanged += ParameterValueChanged;
        _ventScale.ValueChanged += ParameterValueChanged;
        _holeManipulator.CheckedChanged += (_, _) =>
            HoleManipulatorRequested?.Invoke(_holeManipulator.Checked);
        _preset.SelectedIndexChanged += PresetChanged;
    }

    public event EventHandler? Accepted;
    public event EventHandler? Cancelled;
    public event EventHandler? SideModeChanged;
    public event EventHandler? RotationRequested;
    public event EventHandler? ParametersChanged;
    public event EventHandler? JointOffsetEdited;
    public event EventHandler? HoleOffsetsEdited;
    public event Action<bool>? HoleManipulatorRequested;

    public void SetHoleOffsets(double xMm, double yMm)
    {
        _settingManipulatorValues = true;
        try
        {
            _holeOffsetX.Value = Math.Clamp((decimal)xMm, _holeOffsetX.Minimum, _holeOffsetX.Maximum);
            _holeOffsetY.Value = Math.Clamp((decimal)yMm, _holeOffsetY.Minimum, _holeOffsetY.Maximum);
        }
        finally { _settingManipulatorValues = false; }
    }

    public void SetHoleManipulatorActive(bool active)
    {
        if (_holeManipulator.Checked != active)
            _holeManipulator.Checked = active;
    }

    public void SetJointOffset(double offsetMm)
    {
        _settingManipulatorValues = true;
        try
        {
            _jointOffset.Value = Math.Clamp(
                (decimal)offsetMm, _jointOffset.Minimum, _jointOffset.Maximum);
        }
        finally { _settingManipulatorValues = false; }
    }

    public void SetPreviewWarning(string? message)
    {
        _previewWarning.Text = message ?? string.Empty;
        _previewWarning.Visible = !string.IsNullOrWhiteSpace(message);
    }

    public void SetWallPair(JointWallPair pair)
    {
        var first = pair == JointWallPair.AB ? "A" : "C";
        var second = pair == JointWallPair.AB ? "B" : "D";
        _sideA.Text = first;
        _sideB.Text = second;
        _bothSides.Text = first + "+" + second;
    }

    public JointSideMode SelectedSideMode =>
        _sideA.Checked ? JointSideMode.SideA :
        _sideB.Checked ? JointSideMode.SideB : JointSideMode.Both;

    public TubeJointParameters GetParameters(JointPairSelection selection) => new()
    {
        PresetName = Convert.ToString(_preset.SelectedItem) ?? "Стандартный",
        TenonWidthMm = Decimal.ToDouble(_tenonWidth.Value),
        TenonHeightMm = Decimal.ToDouble(_tenonHeight.Value),
        AutoTenonWidth = _autoWidth.Checked,
        AutoTenonHeight = _autoHeight.Checked,
        SlotTargetThicknessMm = selection.MaleWallThicknessMm,
        AutoSlotThickness = true,
        ClearanceMm = Decimal.ToDouble(_clearance.Value),
        MaleWallMm = selection.MaleWallThicknessMm,
        FemaleWallMm = selection.FemaleWallThicknessMm,
        SideMode = SelectedSideMode,
        WallMask = (selection.WallPair, SelectedSideMode) switch
        {
            (JointWallPair.AB, JointSideMode.SideA) => JointWallMask.A,
            (JointWallPair.AB, JointSideMode.SideB) => JointWallMask.B,
            (JointWallPair.AB, _) => JointWallMask.OppositeAB,
            (JointWallPair.CD, JointSideMode.SideA) => JointWallMask.C,
            (JointWallPair.CD, JointSideMode.SideB) => JointWallMask.D,
            _ => JointWallMask.C | JointWallMask.D
        },
        CreateCenterVent = _centerVent.Checked,
        InsertionMode = JointInsertionMode.AlongFemaleFaceNormal,
        SideAOffsetMm = 0.0,
        SideBOffsetMm = 0.0,
        JointOffsetXMm = 0.0,
        JointOffsetYMm = Decimal.ToDouble(_jointOffset.Value),
        HoleOffsetXMm = Decimal.ToDouble(_holeOffsetX.Value),
        HoleOffsetYMm = Decimal.ToDouble(_holeOffsetY.Value),
        ReliefFactorPercent = Decimal.ToDouble(_reliefFactor.Value),
        VentScalePercent = Decimal.ToDouble(_ventScale.Value)
    };

    private void ConfigurePresets(double standardWidth, double standardHeight)
    {
        _preset.DropDownStyle = ComboBoxStyle.DropDownList;
        _preset.BackColor = EditorBack;
        _preset.ForeColor = TextColor;
        _preset.FlatStyle = FlatStyle.Flat;
        _preset.Margin = new Padding(2, 0, 2, 7);
        _preset.Tag = new Dictionary<string, (double Width, double Height)>
        {
            ["Стандартный"] = (standardWidth, standardHeight),
            ["Компактный"] = (RoundToStep(standardWidth * _settings.CompactPresetFactor,
                    _settings.DimensionStepMm),
                RoundToStep(standardHeight * _settings.CompactPresetFactor, _settings.DimensionStepMm)),
            ["Усиленный"] = (RoundToStep(standardWidth * _settings.ReinforcedPresetFactor,
                    _settings.DimensionStepMm),
                RoundToStep(standardHeight * _settings.ReinforcedPresetFactor, _settings.DimensionStepMm)),
            ["Пользовательский"] = (standardWidth, standardHeight)
        };
        _preset.Items.AddRange(new object[] { "Стандартный", "Компактный", "Усиленный", "Пользовательский" });
        _preset.SelectedIndex = 0;
    }

    private void PresetChanged(object? sender, EventArgs e)
    {
        if (_preset.Tag is not Dictionary<string, (double Width, double Height)> presets ||
            _preset.SelectedItem is not string name || name == "Пользовательский" ||
            !presets.TryGetValue(name, out var values))
            return;

        _applyingPreset = true;
        _autoWidth.Checked = false;
        _autoHeight.Checked = false;
        _tenonWidth.Value = Math.Clamp((decimal)values.Width, _tenonWidth.Minimum, _tenonWidth.Maximum);
        _tenonHeight.Value = Math.Clamp((decimal)values.Height, _tenonHeight.Minimum, _tenonHeight.Maximum);
        _applyingPreset = false;
        ParametersChanged?.Invoke(this, EventArgs.Empty);
    }

    private static double CalculateAutomaticWidth(
        JointPairSelection selection,
        JointStandardSettings settings) => RoundToStep(
        Math.Clamp(selection.MaleCrossSpanMm * settings.AutoWidthFactor,
            settings.AutoWidthMinimumMm, settings.AutoWidthMaximumMm),
        settings.DimensionStepMm);

    private static double CalculateAutomaticHeight(
        JointPairSelection selection,
        JointStandardSettings settings)
    {
        var available = Math.Max(4.0, selection.MaleProfileSpanMm - 2.0 * selection.MaleWallThicknessMm);
        return RoundToStep(Math.Clamp(
            Math.Min(selection.MaleProfileSpanMm * settings.AutoHeightFactor, available), 1.0, 500.0),
            settings.DimensionStepMm);
    }

    private static double RoundToStep(double value, double step) =>
        step <= 1e-9 ? value : Math.Round(value / step, MidpointRounding.AwayFromZero) * step;

    private void AddRoot(Control control, SizeType sizeType, float height = 0)
    {
        var rowIndex = _root.RowCount++;
        _root.RowStyles.Add(new RowStyle(sizeType, height));
        control.Dock = DockStyle.Fill;
        _root.Controls.Add(control, 0, rowIndex);
    }

    private void SideCheckedChanged(object? sender, EventArgs e)
    {
        if (sender is RadioButton { Checked: true })
        {
            SideModeChanged?.Invoke(this, EventArgs.Empty);
            ParametersChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    private void ParameterValueChanged(object? sender, EventArgs e)
    {
        if (!_applyingPreset &&
            (ReferenceEquals(sender, _tenonWidth) || ReferenceEquals(sender, _tenonHeight)) &&
            _preset.SelectedItem is string name && name != "Пользовательский")
            _preset.SelectedItem = "Пользовательский";
        ParametersChanged?.Invoke(this, EventArgs.Empty);
    }

    private void JointOffsetValueChanged(object? sender, EventArgs e)
    {
        ParameterValueChanged(sender, e);
        if (!_settingManipulatorValues)
            JointOffsetEdited?.Invoke(this, EventArgs.Empty);
    }

    private void HoleOffsetValueChanged(object? sender, EventArgs e)
    {
        ParameterValueChanged(sender, e);
        if (!_settingManipulatorValues)
            HoleOffsetsEdited?.Invoke(this, EventArgs.Empty);
    }

    private static Label Section(string text) => new()
    {
        Text = text,
        BackColor = SectionBack,
        ForeColor = TextColor,
        TextAlign = ContentAlignment.MiddleLeft,
        Padding = new Padding(3, 0, 0, 0),
        Margin = new Padding(0, 3, 0, 1)
    };

    private static Control ParameterRow(string label, Control editor, CheckBox automatic)
    {
        var host = new TableLayoutPanel
        {
            ColumnCount = 3,
            RowCount = 1,
            Margin = Padding.Empty,
            BackColor = PanelBack
        };
        host.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 42));
        host.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 38));
        host.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 20));
        editor.Dock = DockStyle.Fill;
        automatic.Dock = DockStyle.Fill;
        host.Controls.Add(RowLabel(label), 0, 0);
        host.Controls.Add(editor, 1, 0);
        host.Controls.Add(automatic, 2, 0);
        return host;
    }

    private static Control ReadOnlyRow(string label, string value)
    {
        var editor = new TextBox
        {
            Text = value,
            ReadOnly = true,
            BorderStyle = BorderStyle.FixedSingle,
            BackColor = EditorBack,
            ForeColor = TextColor,
            Dock = DockStyle.Fill
        };
        return Row(label, editor);
    }

    private static Control EditorRow(string label, Control editor, string suffix)
    {
        var host = new TableLayoutPanel
        {
            ColumnCount = 2,
            RowCount = 1,
            Margin = Padding.Empty,
            BackColor = EditorBack
        };
        host.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        host.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 27));
        editor.Dock = DockStyle.Fill;
        host.Controls.Add(editor, 0, 0);
        host.Controls.Add(new Label
        {
            Text = suffix,
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleCenter,
            ForeColor = MutedText
        }, 1, 0);
        return Row(label, host);
    }

    private static Control CompactHoleOffsets(FlatNumericBox x, FlatNumericBox y)
    {
        var row = new FlowLayoutPanel
        {
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false,
            BackColor = PanelBack,
            Margin = new Padding(5, 0, 2, 1),
            Padding = Padding.Empty
        };
        foreach (var item in new[] { ("X", x), ("Y", y) })
        {
            row.Controls.Add(new Label
            {
                Text = item.Item1,
                Width = 14,
                Height = 23,
                TextAlign = ContentAlignment.MiddleCenter,
                ForeColor = MutedText,
                Margin = new Padding(0, 1, 2, 0)
            });
            item.Item2.Width = 78;
            item.Item2.Height = 23;
            item.Item2.Margin = new Padding(0, 1, 10, 0);
            row.Controls.Add(item.Item2);
        }
        return row;
    }

    private static Control CompactCentered(Control control, int width)
    {
        var row = new FlowLayoutPanel
        {
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false,
            BackColor = PanelBack,
            Margin = new Padding(5, 0, 2, 1),
            Padding = Padding.Empty
        };
        control.Dock = DockStyle.None;
        control.Width = width;
        control.Height = 25;
        control.Margin = new Padding(0, 1, 0, 0);
        row.Controls.Add(control);
        return row;
    }

    private static Control Row(string label, Control editor)
    {
        var row = new TableLayoutPanel
        {
            ColumnCount = 2,
            RowCount = 1,
            Margin = new Padding(2, 1, 2, 1),
            BackColor = PanelBack
        };
        row.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 42));
        row.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 58));
        editor.Dock = DockStyle.Fill;
        row.Controls.Add(RowLabel(label), 0, 0);
        row.Controls.Add(editor, 1, 0);
        return row;
    }

    private static Label RowLabel(string text) => new()
    {
        Text = text,
        Dock = DockStyle.Fill,
        TextAlign = ContentAlignment.MiddleLeft,
        ForeColor = TextColor,
        AutoEllipsis = true
    };

    private static CheckBox AutoCheckBox() => new()
    {
        Text = "Авто",
        ForeColor = TextColor,
        FlatStyle = FlatStyle.Flat,
        TextAlign = ContentAlignment.MiddleCenter,
        CheckAlign = ContentAlignment.MiddleLeft,
        Margin = new Padding(4, 2, 0, 2)
    };

    private static FlatNumericBox Numeric(
        decimal value, decimal minimum, decimal maximum, decimal increment, int decimals)
    {
        var editor = new FlatNumericBox
        {
            Minimum = minimum,
            Maximum = maximum,
            Increment = increment,
            DecimalPlaces = decimals,
            BorderStyle = BorderStyle.FixedSingle,
            BackColor = EditorBack,
            ForeColor = TextColor,
            TextAlign = HorizontalAlignment.Right
        };
        editor.Value = value;
        return editor;
    }

    private static RadioButton SideButton(string text)
    {
        var button = new RadioButton
        {
            Text = text,
            Appearance = Appearance.Button,
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleCenter,
            FlatStyle = FlatStyle.Flat,
            BackColor = EditorBack,
            ForeColor = TextColor,
            Margin = new Padding(1),
            UseVisualStyleBackColor = false
        };
        button.FlatAppearance.BorderColor = Border;
        button.FlatAppearance.CheckedBackColor = Color.FromArgb(26, 126, 166);
        return button;
    }

    private static CheckBox ToggleButton(string text)
    {
        var button = new CheckBox
        {
            Text = text,
            Appearance = Appearance.Button,
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleCenter,
            FlatStyle = FlatStyle.Flat,
            BackColor = EditorBack,
            ForeColor = TextColor,
            Margin = new Padding(2, 1, 2, 1),
            UseVisualStyleBackColor = false
        };
        button.FlatAppearance.BorderColor = Border;
        button.FlatAppearance.CheckedBackColor = Color.FromArgb(26, 126, 166);
        return button;
    }

    private static Button SmallButton(string text)
    {
        var button = new Button
        {
            Text = text,
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleCenter,
            FlatStyle = FlatStyle.Flat,
            BackColor = EditorBack,
            ForeColor = TextColor,
            Margin = new Padding(1),
            UseVisualStyleBackColor = false
        };
        button.FlatAppearance.BorderColor = Border;
        return button;
    }

    private static Button ActionButton(string text, Color backColor)
    {
        var button = new Button
        {
            Text = text,
            Width = 82,
            Height = 27,
            FlatStyle = FlatStyle.Flat,
            BackColor = backColor,
            ForeColor = TextColor,
            Margin = new Padding(0, 0, 8, 0),
            UseVisualStyleBackColor = false
        };
        button.FlatAppearance.BorderColor = Border;
        return button;
    }
}

/// <summary>An Inventor-like parameter image with editable values placed on its leaders.</summary>
internal sealed class JointDiagramControl : UserControl
{
    private static readonly Color Background = Color.FromArgb(37, 47, 61);
    private readonly Image? _diagram;
    private readonly Control _relief;
    private readonly Control _ventScale;
    private readonly Control _tenonWidth;
    private readonly Control _tenonLength;
    private readonly Control _slotClearance;

    public JointDiagramControl(
        FlatNumericBox relief,
        FlatNumericBox ventScale,
        FlatNumericBox tenonWidth,
        FlatNumericBox tenonLength,
        FlatNumericBox slotClearance)
    {
        _diagram = LoadDiagram();
        _relief = Editor(relief, "%");
        _ventScale = Editor(ventScale, "%");
        _tenonWidth = Editor(tenonWidth, "мм");
        _tenonLength = Editor(tenonLength, "мм");
        _slotClearance = Editor(slotClearance, "мм");
        BackColor = Background;
        Margin = new Padding(2, 3, 2, 4);
        DoubleBuffered = true;

        Controls.AddRange(new[] { _relief, _ventScale, _tenonWidth, _tenonLength, _slotClearance });
        var tips = new ToolTip();
        tips.SetToolTip(_relief, "Прослабление: TJ_ReliefFactor");
        tips.SetToolTip(_ventScale, "Масштаб дополнительного выреза");
        tips.SetToolTip(_tenonWidth, "Ширина шипа: TJ_TenonAcrossWidth");
        tips.SetToolTip(_tenonLength, "Длина шипа: TJ_TenonLength");
        tips.SetToolTip(_slotClearance, "Припуск паза на одну сторону: TJ_Clearance");
        Resize += (_, _) => LayoutEditors();
        LayoutEditors();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        if (_diagram is null)
            return;

        e.Graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
        e.Graphics.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
        e.Graphics.DrawImage(_diagram, DiagramBounds());
    }

    private void LayoutEditors()
    {
        var image = DiagramBounds();
        Place(_relief, image, 0.49, 0.035);
        Place(_ventScale, image, 0.91, 0.035);
        Place(_tenonWidth, image, 0.30, 0.295);
        Place(_tenonLength, image, 0.31, 0.945);
        Place(_slotClearance, image, 0.83, 0.945);
    }

    private Rectangle DiagramBounds()
    {
        if (_diagram is null || Width <= 0 || Height <= 0)
            return ClientRectangle;

        var scale = Math.Min((double)Width / _diagram.Width, (double)Height / _diagram.Height);
        var width = Math.Max(1, (int)Math.Round(_diagram.Width * scale));
        var height = Math.Max(1, (int)Math.Round(_diagram.Height * scale));
        return new Rectangle((Width - width) / 2, (Height - height) / 2, width, height);
    }

    private static void Place(Control editor, Rectangle image, double x, double y)
    {
        var left = image.Left + (int)Math.Round(image.Width * x) - editor.Width / 2;
        var top = image.Top + (int)Math.Round(image.Height * y) - editor.Height / 2;
        editor.Location = new Point(
            Math.Clamp(left, 1, Math.Max(1, image.Right - editor.Width - 1)),
            Math.Clamp(top, 1, Math.Max(1, image.Bottom - editor.Height - 1)));
        editor.BringToFront();
    }

    private static Control Editor(FlatNumericBox value, string suffix)
    {
        var host = new TableLayoutPanel
        {
            ColumnCount = 2,
            RowCount = 1,
            Size = new Size(suffix == "%" ? 82 : 91, 23),
            BackColor = Background,
            CellBorderStyle = TableLayoutPanelCellBorderStyle.Single,
            Margin = Padding.Empty,
            Padding = Padding.Empty
        };
        host.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        host.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, suffix == "%" ? 19 : 25));
        value.BorderStyle = BorderStyle.None;
        value.Dock = DockStyle.Fill;
        value.Margin = Padding.Empty;
        host.Controls.Add(value, 0, 0);
        host.Controls.Add(new Label
        {
            Text = suffix,
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleCenter,
            ForeColor = Color.FromArgb(205, 213, 222),
            Margin = Padding.Empty
        }, 1, 0);
        return host;
    }

    private static Image? LoadDiagram()
    {
        using var stream = typeof(JointDiagramControl).Assembly.GetManifestResourceStream(
            "TubeJoint.AddIn.Resources.Icon.png");
        if (stream is null)
            return null;
        using var source = new Bitmap(stream);
        return new Bitmap(source);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
            _diagram?.Dispose();
        base.Dispose(disposing);
    }
}

/// <summary>Compact validated numeric field without a spinner, matching Inventor editors.</summary>
internal sealed class FlatNumericBox : TextBox
{
    private decimal _value;
    private bool _formatting;

    public decimal Minimum { get; set; } = decimal.MinValue;
    public decimal Maximum { get; set; } = decimal.MaxValue;
    public decimal Increment { get; set; } = 1m;
    public int DecimalPlaces { get; set; }

    public decimal Value
    {
        get => _value;
        set => SetValue(value, true);
    }

    public event EventHandler? ValueChanged;

    protected override void OnTextChanged(EventArgs e)
    {
        base.OnTextChanged(e);
        if (_formatting)
            return;
        if (!decimal.TryParse(Text, System.Globalization.NumberStyles.Number,
                System.Globalization.CultureInfo.CurrentCulture, out var parsed))
            return;
        if (parsed < Minimum || parsed > Maximum || parsed == _value)
            return;
        _value = parsed;
        ValueChanged?.Invoke(this, EventArgs.Empty);
    }

    protected override void OnLeave(EventArgs e)
    {
        CommitText();
        base.OnLeave(e);
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (e.KeyCode is Keys.Up or Keys.Down)
        {
            Value = _value + (e.KeyCode == Keys.Up ? Increment : -Increment);
            e.Handled = true;
            e.SuppressKeyPress = true;
            return;
        }
        if (e.KeyCode == Keys.Enter)
        {
            CommitText();
            e.Handled = true;
            e.SuppressKeyPress = true;
            return;
        }
        base.OnKeyDown(e);
    }

    private void CommitText()
    {
        if (decimal.TryParse(Text, System.Globalization.NumberStyles.Number,
                System.Globalization.CultureInfo.CurrentCulture, out var parsed))
            SetValue(parsed, true);
        else
            FormatValue();
    }

    private void SetValue(decimal value, bool format)
    {
        var clamped = Math.Clamp(value, Minimum, Maximum);
        var changed = clamped != _value;
        _value = clamped;
        if (format)
            FormatValue();
        if (changed)
            ValueChanged?.Invoke(this, EventArgs.Empty);
    }

    private void FormatValue()
    {
        _formatting = true;
        try
        {
            Text = _value.ToString($"F{DecimalPlaces}",
                System.Globalization.CultureInfo.CurrentCulture);
            SelectionStart = TextLength;
        }
        finally { _formatting = false; }
    }
}

internal static class GraphicsExtensions
{
    public static void DrawRoundedRectangle(this Graphics graphics, Pen pen, Rectangle bounds, int radius)
    {
        using var path = new System.Drawing.Drawing2D.GraphicsPath();
        var diameter = radius * 2;
        path.AddArc(bounds.Left, bounds.Top, diameter, diameter, 180, 90);
        path.AddArc(bounds.Right - diameter, bounds.Top, diameter, diameter, 270, 90);
        path.AddArc(bounds.Right - diameter, bounds.Bottom - diameter, diameter, diameter, 0, 90);
        path.AddArc(bounds.Left, bounds.Bottom - diameter, diameter, diameter, 90, 90);
        path.CloseFigure();
        graphics.DrawPath(pen, path);
    }
}
