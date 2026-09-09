using Inventor;
using TubeJoint.AddIn.Models;
using TubeJoint.AddIn.Services;

namespace TubeJoint.AddIn.UI;

/// <summary>Hosts the command UI in a real Inventor DockableWindow.</summary>
internal sealed class NativeJointInput : IDisposable
{
    private enum ManipulatorMode
    {
        None,
        Joint,
        Hole
    }

    private const string WindowInternalName = "TubeJoint.Properties.V7";
    private readonly Inventor.Application _application;
    private JointPairSelection _selection;
    private readonly JointPropertiesControl _control;
    private readonly DockableWindow _window;
    private readonly DockableWindowsEvents _windowEvents;
    private readonly JointPreviewService _preview;
    private readonly JointManipulatorService _manipulator;
    private readonly JointTemplateService _templates;
    private readonly JointPresetStore _presetStore;
    private JointPresetCatalog _presetCatalog;
    private IReadOnlyList<JointTemplateDescriptor> _templateDescriptors;
    private string _templatePath;
    private readonly System.Windows.Forms.Timer _previewTimer;
    private ManipulatorMode _manipulatorMode;
    private bool _changingHoleToggle;
    private bool _cancelQueued;
    private bool _disposed;

    public NativeJointInput(
        Inventor.Application application,
        AssemblyDocument assembly,
        JointPairSelection selection,
        JointStandardSettings settings,
        JointTemplateService templates,
        JointPresetStore presetStore,
        JointPresetCatalog presetCatalog,
        IReadOnlyList<JointTemplateDescriptor> templateDescriptors,
        JointPreset? initialPreset,
        JointTemplateDescriptor initialTemplate)
    {
        _application = application;
        _selection = selection;
        _templates = templates;
        _presetStore = presetStore;
        _presetCatalog = presetCatalog;
        _templateDescriptors = templateDescriptors;
        _templatePath = initialTemplate.FullPath;
        _preview = new JointPreviewService(application, assembly, templates, _templatePath);
        _manipulator = new JointManipulatorService(application);
        _previewTimer = new System.Windows.Forms.Timer { Interval = 80 };
        _previewTimer.Tick += PreviewTimerOnTick;
        var windows = application.UserInterfaceManager.DockableWindows;
        foreach (var obsoleteName in new[]
                 {
                     "TubeJoint.Properties.V1",
                     "TubeJoint.Properties.V2",
                     "TubeJoint.Properties.V3",
                     "TubeJoint.Properties.V4",
                     "TubeJoint.Properties.V5",
                     "TubeJoint.Properties.V6"
                 })
        {
            try
            {
                var obsolete = windows[obsoleteName];
                obsolete.Visible = false;
                obsolete.Clear();
            }
            catch { }
        }
        try
        {
            _window = windows[WindowInternalName];
            _window.Clear();
        }
        catch
        {
            _window = windows.Add(StandardAddInServer.ClientId, WindowInternalName, "Properties");
        }

        InventorThemePalette.Refresh(application);
        _control = new JointPropertiesControl(
            selection, settings, presetCatalog, templateDescriptors,
            initialPreset, initialTemplate.FileName);
        _control.CreateControl();
        _control.Accepted += OnAccepted;
        _control.Cancelled += OnCancelled;
        _control.SideModeChanged += OnSideModeChanged;
        _control.RotationRequested += OnRotationRequested;
        _control.ParametersChanged += OnParametersChanged;
        _control.JointOffsetEdited += OnJointOffsetEdited;
        _control.HoleOffsetsEdited += OnHoleOffsetsEdited;
        _control.HoleManipulatorRequested += OnHoleManipulatorRequested;
        _control.SavePresetAsRequested += OnSavePresetAsRequested;
        _control.SavePresetRequested += OnSavePresetRequested;
        _control.RenamePresetRequested += OnRenamePresetRequested;
        _control.DeletePresetRequested += OnDeletePresetRequested;
        _control.CreateTemplateRequested += OnCreateTemplateRequested;
        _control.EditTemplateRequested += OnEditTemplateRequested;
        _control.SelectedTemplateChanged += OnSelectedTemplateChanged;
        _control.SelectedPresetChanged += OnSelectedPresetChanged;
        _control.SortOrderRequested += OnSortOrderRequested;
        _control.StartupModeRequested += OnStartupModeRequested;
        _window.AddChild(_control.Handle);
        _window.ShowVisibilityCheckBox = false;
        _window.DisabledDockingStates =
            DockingStateEnum.kDockTop | DockingStateEnum.kDockBottom;
        _window.SetMinimumSize(285, 560);
        // Match Inventor's narrow vertical property panels on first use. Inventor
        // restores the user's own docking and sizing after the pane is customized.
        if (!_window.IsCustomized)
        {
            _window.SetDockingState(DockingStateEnum.kFloat);
            _window.Width = 320;
            _window.Height = 650;
        }
        _windowEvents = windows.Events;
        _windowEvents.OnHide += OnWindowHidden;
    }

    public event Action<TubeJointParameters, JointPairSelection, string>? Accepted;
    public event Action? Cancelled;

    public void Show()
    {
        _window.Visible = true;
        _control.Visible = true;
        ShowSelectedSides();
        RefreshPreview();
        StartMainManipulator();
    }

    public void BringToFront()
    {
        if (_disposed) return;
        _window.Visible = true;
        _control.Focus();
        RefreshPreview();
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _control.Accepted -= OnAccepted;
        _control.Cancelled -= OnCancelled;
        _control.SideModeChanged -= OnSideModeChanged;
        _control.RotationRequested -= OnRotationRequested;
        _control.ParametersChanged -= OnParametersChanged;
        _control.JointOffsetEdited -= OnJointOffsetEdited;
        _control.HoleOffsetsEdited -= OnHoleOffsetsEdited;
        _control.HoleManipulatorRequested -= OnHoleManipulatorRequested;
        _control.SavePresetAsRequested -= OnSavePresetAsRequested;
        _control.SavePresetRequested -= OnSavePresetRequested;
        _control.RenamePresetRequested -= OnRenamePresetRequested;
        _control.DeletePresetRequested -= OnDeletePresetRequested;
        _control.CreateTemplateRequested -= OnCreateTemplateRequested;
        _control.EditTemplateRequested -= OnEditTemplateRequested;
        _control.SelectedTemplateChanged -= OnSelectedTemplateChanged;
        _control.SelectedPresetChanged -= OnSelectedPresetChanged;
        _control.SortOrderRequested -= OnSortOrderRequested;
        _control.StartupModeRequested -= OnStartupModeRequested;
        try { _windowEvents.OnHide -= OnWindowHidden; } catch { }
        _previewTimer.Stop();
        _previewTimer.Tick -= PreviewTimerOnTick;
        _previewTimer.Dispose();
        _preview.Dispose();
        _manipulatorMode = ManipulatorMode.None;
        _manipulator.Dispose();
        try { ClearAssemblySelection(); } catch { }
        try { _window.Visible = false; } catch { }
        try { _window.Clear(); } catch { }
        _control.Dispose();
    }

    private void OnAccepted(object? sender, EventArgs e)
    {
        if (_disposed) return;
        var parameters = _control.GetParameters(_selection);
        var selectedPair = _selection;
        _control.BeginInvoke(new Action(() =>
        {
            if (!_disposed) Accepted?.Invoke(parameters, selectedPair, _templatePath);
        }));
    }

    private void OnCancelled(object? sender, EventArgs e)
    {
        if (_disposed || _cancelQueued) return;
        _cancelQueued = true;
        _control.BeginInvoke(new Action(() =>
        {
            if (!_disposed) Cancelled?.Invoke();
        }));
    }

    private void OnSavePresetAsRequested(object? sender, EventArgs e)
    {
        var name = TextPromptDialog.Show(
            "Новый пресет", "Имя пресета:");
        if (name is null) return;
        if (_presetCatalog.Presets.Any(item =>
                string.Equals(item.Name, name, StringComparison.CurrentCultureIgnoreCase)))
        {
            ShowPresetError($"Пресет '{name}' уже существует.");
            return;
        }

        var preset = _control.CapturePreset(name, _selection);
        _presetCatalog.Presets.Add(preset);
        _presetCatalog.LastUsedPresetId = preset.Id;
        SavePresetCatalog();
        RefreshPresetData(preset.Id);
    }

    private void OnSavePresetRequested(object? sender, EventArgs e)
    {
        if (_control.SelectedPreset is not JointPreset selected) return;
        var replacement = _control.CapturePreset(selected.Name, _selection, selected.Id);
        ReplacePreset(replacement);
        SavePresetCatalog();
        RefreshPresetData(replacement.Id);
    }

    private void OnRenamePresetRequested(object? sender, EventArgs e)
    {
        if (_control.SelectedPreset is not JointPreset selected) return;
        var name = TextPromptDialog.Show(
            "Переименовать пресет", "Новое имя:", selected.Name);
        if (name is null || string.Equals(name, selected.Name, StringComparison.Ordinal)) return;
        if (_presetCatalog.Presets.Any(item => item.Id != selected.Id &&
                string.Equals(item.Name, name, StringComparison.CurrentCultureIgnoreCase)))
        {
            ShowPresetError($"Пресет '{name}' уже существует.");
            return;
        }

        var renamed = selected with { Name = name, ModifiedUtc = DateTimeOffset.UtcNow };
        ReplacePreset(renamed);
        SavePresetCatalog();
        RefreshPresetData(renamed.Id);
    }

    private void OnDeletePresetRequested(object? sender, EventArgs e)
    {
        if (_control.SelectedPreset is not JointPreset selected) return;
        var answer = System.Windows.Forms.MessageBox.Show(
            $"Удалить пресет '{selected.Name}'?\nЭскиз IPT удалён не будет.",
            "Пресеты шип-паза",
            System.Windows.Forms.MessageBoxButtons.YesNo,
            System.Windows.Forms.MessageBoxIcon.Question,
            System.Windows.Forms.MessageBoxDefaultButton.Button2);
        if (answer != System.Windows.Forms.DialogResult.Yes) return;

        _presetCatalog.Presets.RemoveAll(item => item.Id == selected.Id);
        if (_presetCatalog.DefaultPresetId == selected.Id)
        {
            _presetCatalog.DefaultPresetId = null;
            _presetCatalog.StartupMode = JointPresetStartupMode.NoPreset;
        }
        if (_presetCatalog.LastUsedPresetId == selected.Id)
            _presetCatalog.LastUsedPresetId = null;
        SavePresetCatalog();
        RefreshPresetData(null);
    }

    private void OnSortOrderRequested(JointPresetSortOrder order)
    {
        _presetCatalog.SortOrder = order;
        var selectedId = _control.SelectedPreset?.Id;
        SavePresetCatalog();
        RefreshPresetData(selectedId);
    }

    private void OnStartupModeRequested(JointPresetStartupMode mode)
    {
        if (mode == JointPresetStartupMode.SpecificPreset &&
            _control.SelectedPreset is not JointPreset selected)
        {
            ShowPresetError("Сначала выберите пресет.");
            return;
        }
        _presetCatalog.StartupMode = mode;
        _presetCatalog.DefaultPresetId = mode == JointPresetStartupMode.SpecificPreset
            ? _control.SelectedPreset?.Id
            : null;
        SavePresetCatalog();
        RefreshPresetData(_control.SelectedPreset?.Id);
    }

    private void OnSelectedPresetChanged(object? sender, EventArgs e)
    {
        if (_control.SelectedPreset is not JointPreset selected) return;
        var used = selected with { LastUsedUtc = DateTimeOffset.UtcNow };
        ReplacePreset(used);
        _presetCatalog.LastUsedPresetId = used.Id;
        SavePresetCatalog();
    }

    private void OnSelectedTemplateChanged(object? sender, EventArgs e)
    {
        if (_control.SelectedTemplate is not JointTemplateDescriptor selected) return;
        try
        {
            _templatePath = _templates.EnsureTemplate(selected.FullPath);
            _preview.SelectTemplate(_templatePath);
        }
        catch (Exception exception)
        {
            ShowPresetError(exception.Message);
            RefreshPresetData(
                _control.SelectedPreset?.Id, System.IO.Path.GetFileName(_templatePath));
        }
    }

    private void OnCreateTemplateRequested(object? sender, EventArgs e)
    {
        if (_control.SelectedTemplate is not JointTemplateDescriptor source) return;
        var name = TextPromptDialog.Show(
            "Новый вариант эскиза", "Имя варианта:", source.DisplayName + " копия");
        if (name is null) return;
        try
        {
            var created = _templates.CreateTemplateVariant(name, source.FullPath);
            _templateDescriptors = _templates.ListTemplates();
            _templatePath = created.FullPath;
            _preview.SelectTemplate(_templatePath);
            _control.BindPresetData(
                _presetCatalog, _templateDescriptors, null, created.FileName);
            _templates.OpenTemplateForEditing(created.FullPath);
        }
        catch (Exception exception)
        {
            ShowPresetError(exception.Message);
        }
    }

    private void OnEditTemplateRequested(object? sender, EventArgs e)
    {
        if (_control.SelectedTemplate is not JointTemplateDescriptor selected) return;
        try { _templates.OpenTemplateForEditing(selected.FullPath); }
        catch (Exception exception) { ShowPresetError(exception.Message); }
    }

    private void ReplacePreset(JointPreset replacement)
    {
        var index = _presetCatalog.Presets.FindIndex(item => item.Id == replacement.Id);
        if (index >= 0) _presetCatalog.Presets[index] = replacement;
        else _presetCatalog.Presets.Add(replacement);
    }

    private void RefreshPresetData(
        string? selectedPresetId,
        string? templateFileNameOverride = null)
    {
        var templateFileName = templateFileNameOverride ?? _control.SelectedTemplate?.FileName ??
                               System.IO.Path.GetFileName(_templatePath);
        _control.BindPresetData(
            _presetCatalog, _templateDescriptors, selectedPresetId, templateFileName);
    }

    private void SavePresetCatalog()
    {
        try { _presetStore.Save(_presetCatalog); }
        catch (Exception exception) { ShowPresetError(exception.Message); }
    }

    private static void ShowPresetError(string message) =>
        System.Windows.Forms.MessageBox.Show(
            message, "Пресеты шип-паза",
            System.Windows.Forms.MessageBoxButtons.OK,
            System.Windows.Forms.MessageBoxIcon.Error);

    private void OnWindowHidden(
        DockableWindow window,
        EventTimingEnum timing,
        NameValueMap context,
        out HandlingCodeEnum handlingCode)
    {
        handlingCode = HandlingCodeEnum.kEventNotHandled;
        if (_disposed || _cancelQueued || timing != EventTimingEnum.kAfter ||
            !string.Equals(window.InternalName, WindowInternalName, StringComparison.Ordinal))
            return;

        // The title-bar X hides a DockableWindow without disposing its child control.
        // Tear down all live command state immediately, then let the command owner dispose it.
        _cancelQueued = true;
        _previewTimer.Stop();
        _manipulatorMode = ManipulatorMode.None;
        _manipulator.Stop();
        _preview.Clear();
        try { ClearAssemblySelection(); } catch { }
        _control.BeginInvoke(new Action(() =>
        {
            if (!_disposed) Cancelled?.Invoke();
        }));
    }

    private void OnSideModeChanged(object? sender, EventArgs e) => ShowSelectedSides();

    private void OnRotationRequested(object? sender, EventArgs e)
    {
        if (_disposed || _selection.RotatedPair is not JointPairSelection rotated) return;
        _manipulator.Stop();
        _manipulatorMode = ManipulatorMode.None;
        _selection = rotated;
        SetHoleToggle(false);
        _control.SetWallPair(rotated.WallPair);
        _control.SetJointOffset(0.0);
        ShowSelectedSides();
        RefreshPreview();
        StartMainManipulator();
    }

    private void OnParametersChanged(object? sender, EventArgs e)
    {
        _previewTimer.Stop();
        _previewTimer.Start();
    }

    private void OnHoleManipulatorRequested(bool active)
    {
        if (_disposed || _changingHoleToggle) return;
        if (!active)
        {
            StartMainManipulator();
            return;
        }

        StartHoleManipulator();
    }

    private void StartHoleManipulator()
    {
        if (_disposed) return;
        try
        {
            var parameters = _control.GetParameters(_selection);
            if (!parameters.CreateCenterVent)
            {
                SetHoleToggle(false);
                StartMainManipulator();
                return;
            }
            _manipulator.Stop();
            _manipulatorMode = ManipulatorMode.None;
            _manipulator.StartHole(
                _selection,
                parameters.JointOffsetYMm,
                parameters.HoleOffsetXMm,
                parameters.HoleOffsetYMm,
                OnHoleManipulatorMoved,
                OnHoleManipulatorEnded);
            _manipulatorMode = ManipulatorMode.Hole;
        }
        catch (Exception exception)
        {
            _manipulator.Stop();
            _manipulatorMode = ManipulatorMode.None;
            SetHoleToggle(false);
            StartMainManipulator(showError: false);
            System.Windows.Forms.MessageBox.Show(
                exception.Message, "Шип-паз труб — манипулятор",
                System.Windows.Forms.MessageBoxButtons.OK,
                System.Windows.Forms.MessageBoxIcon.Error);
        }
    }

    private void OnJointOffsetEdited(object? sender, EventArgs e)
    {
        if (_disposed) return;
        if (_manipulatorMode == ManipulatorMode.Hole)
            StartHoleManipulator();
        else
            StartMainManipulator();
    }

    private void OnHoleOffsetsEdited(object? sender, EventArgs e)
    {
        if (_disposed || _manipulatorMode != ManipulatorMode.Hole) return;
        StartHoleManipulator();
    }

    private void StartMainManipulator(bool showError = true)
    {
        if (_disposed) return;
        try
        {
            var parameters = _control.GetParameters(_selection);
            _manipulator.Stop();
            _manipulatorMode = ManipulatorMode.None;
            _manipulator.StartJoint(
                _selection,
                parameters.JointOffsetYMm,
                OnJointManipulatorMoved);
            _manipulatorMode = ManipulatorMode.Joint;
        }
        catch (Exception exception)
        {
            _manipulator.Stop();
            _manipulatorMode = ManipulatorMode.None;
            if (showError)
                System.Windows.Forms.MessageBox.Show(
                    exception.Message, "Шип-паз труб — манипулятор",
                    System.Windows.Forms.MessageBoxButtons.OK,
                    System.Windows.Forms.MessageBoxIcon.Error);
        }
    }

    private void OnHoleManipulatorEnded()
    {
        if (_disposed) return;
        _control.BeginInvoke(new Action(() =>
        {
            if (_disposed || _manipulatorMode != ManipulatorMode.Hole) return;
            SetHoleToggle(false);
            StartMainManipulator();
        }));
    }

    private void SetHoleToggle(bool active)
    {
        _changingHoleToggle = true;
        try { _control.SetHoleManipulatorActive(active); }
        finally { _changingHoleToggle = false; }
    }

    private void OnJointManipulatorMoved(double offsetMm)
    {
        if (_disposed || _manipulatorMode != ManipulatorMode.Joint) return;
        if (_control.InvokeRequired)
        {
            _control.BeginInvoke(new Action<double>(OnJointManipulatorMoved), offsetMm);
            return;
        }
        _control.SetJointOffset(offsetMm);
    }

    private void OnHoleManipulatorMoved(double xMm, double yMm)
    {
        if (_disposed || _manipulatorMode != ManipulatorMode.Hole) return;
        if (_control.InvokeRequired)
        {
            _control.BeginInvoke(new Action<double, double>(OnHoleManipulatorMoved), xMm, yMm);
            return;
        }
        _control.SetHoleOffsets(xMm, yMm);
    }

    private void PreviewTimerOnTick(object? sender, EventArgs e)
    {
        _previewTimer.Stop();
        RefreshPreview();
    }

    private void RefreshPreview()
    {
        if (_disposed) return;
        _control.SetPreviewWarning(
            _preview.Rebuild(_selection, _control.GetParameters(_selection)));
    }

    private void ShowSelectedSides()
    {
        if (_application.ActiveDocument is not AssemblyDocument assembly) return;
        assembly.SelectSet.Clear();
        if (_control.SelectedSideMode is JointSideMode.SideA or JointSideMode.Both)
            assembly.SelectSet.Select(_selection.Male.ProxyFace);
        if (_control.SelectedSideMode is JointSideMode.SideB or JointSideMode.Both)
            assembly.SelectSet.Select(_selection.MaleOpposite.ProxyFace);
        _application.ActiveView.Update();
    }

    private void ClearAssemblySelection()
    {
        if (_application.ActiveDocument is AssemblyDocument assembly)
            assembly.SelectSet.Clear();
    }
}
