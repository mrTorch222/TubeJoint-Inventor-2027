using System.Runtime.InteropServices;
using Inventor;
using TubeJoint.AddIn.Commands;

namespace TubeJoint.AddIn;

[Guid("7F44DC6F-3A5F-4AD4-A7F0-27D513769A58")]
[ComVisible(true)]
public sealed class StandardAddInServer : ApplicationAddInServer
{
    public const string ClientId = "{7F44DC6F-3A5F-4AD4-A7F0-27D513769A58}";
    private const string PanelId = "TubeJoint.DesignPanel.V2";
    private const string PartPanelId = "TubeJoint.PartPanel.V1";
    private Inventor.Application? _application;
    private ButtonDefinition? _createButton;
    private ButtonDefinition? _templateButton;
    private ButtonDefinition? _checkButton;
    private ButtonDefinition? _deleteButton;
    private ButtonDefinition? _recognizeTubeButton;
    private CreateTubeJointCommand? _createCommand;
    private OpenJointTemplateCommand? _templateCommand;
    private UpdateTubeJointsCommand? _checkCommand;
    private DeleteAllTubeJointsCommand? _deleteCommand;
    private RecognizeTubeCommand? _recognizeTubeCommand;

    public object? Automation => null;

    public void Activate(ApplicationAddInSite addInSiteObject, bool firstTime)
    {
        _application = addInSiteObject.Application;
        _createCommand = new CreateTubeJointCommand(_application);
        _templateCommand = new OpenJointTemplateCommand(_application);
        _checkCommand = new UpdateTubeJointsCommand(_application);
        _deleteCommand = new DeleteAllTubeJointsCommand(_application);
        _recognizeTubeCommand = new RecognizeTubeCommand(_application);

        _createButton = GetOrCreateButton(
            "Новое соединение", "TubeJoint.Create.V2",
            "Выбрать целую трубу с шипом и грань трубы с пазом",
            new[] { "AssemblyPlaceComponentCmd", "PartExtrudeCmd" },
            new[] { "place component", "разместить компонент", "extrude", "выдавливание" });
        _templateButton = GetOrCreateButton(
            "Открыть стандарт", "TubeJoint.OpenTemplate.V2",
            "Открыть параметрический стандарт v8: профили, центры и правила размеров шипа-паза",
            new[] { "PartSketch2DCmd", "PartCreate2DSketchCmd" },
            new[] { "sketch", "эскиз" });
        _checkButton = GetOrCreateButton(
            "Проверить соединения", "TubeJoint.Check.V2",
            "Проверить сохранённые соединения текущей сборки",
            new[] { "AppUpdateDesignCmd", "AssemblyRebuildAllCmd" },
            new[] { "update", "обновить", "rebuild", "перестроить" });
        _deleteButton = GetOrCreateButton(
            "Удалить все", "TubeJoint.DeleteAll.V1",
            "Удалить из текущей сборки все шипы и пазы, созданные TubeJoint",
            new[] { "AppDeleteCmd", "DeleteCmd" },
            new[] { "delete", "удалить" });
        _recognizeTubeButton = GetOrCreateButton(
            "Подготовить трубы", "TubeJoint.RecognizeTube.V1",
            "Распознать все трубы, заполнить iProperties, направить вдоль Z и при желании переименовать IPT",
            new[] { "PartMeasureCmd", "AppMeasureCmd" },
            new[] { "measure", "измерить", "properties", "свойства" });

        _createButton.OnExecute += CreateButtonOnExecute;
        _templateButton.OnExecute += TemplateButtonOnExecute;
        _checkButton.OnExecute += CheckButtonOnExecute;
        _deleteButton.OnExecute += DeleteButtonOnExecute;
        _recognizeTubeButton.OnExecute += RecognizeTubeButtonOnExecute;
        BuildRibbon();
    }

    public void Deactivate()
    {
        if (_createButton is not null) _createButton.OnExecute -= CreateButtonOnExecute;
        if (_templateButton is not null) _templateButton.OnExecute -= TemplateButtonOnExecute;
        if (_checkButton is not null) _checkButton.OnExecute -= CheckButtonOnExecute;
        if (_deleteButton is not null) _deleteButton.OnExecute -= DeleteButtonOnExecute;
        if (_recognizeTubeButton is not null) _recognizeTubeButton.OnExecute -= RecognizeTubeButtonOnExecute;
        _createCommand?.Dispose();
        _createButton = null;
        _templateButton = null;
        _checkButton = null;
        _deleteButton = null;
        _recognizeTubeButton = null;
        _createCommand = null;
        _templateCommand = null;
        _checkCommand = null;
        _deleteCommand = null;
        _recognizeTubeCommand = null;
        _application = null;
        GC.Collect();
        GC.WaitForPendingFinalizers();
    }

    public void ExecuteCommand(int commandId) { }

    private void BuildRibbon()
    {
        if (_application is null || _createButton is null || _templateButton is null ||
            _checkButton is null || _deleteButton is null || _recognizeTubeButton is null)
            return;
        var ribbon = _application.UserInterfaceManager.Ribbons["Assembly"];

        // Remove panel IDs used by the pre-template builds from Assemble.
        TryDeletePanel(ribbon, "id_TabAssemble", "TubeJoint.RibbonPanel");
        TryDeletePanel(ribbon, "id_TabAssemble", "TubeJoint.AssemblePanel");
        TryDeletePanel(ribbon, "id_TabAssemble", PanelId);

        var design = ribbon.RibbonTabs["id_TabDesign"];
        RibbonPanel panel;
        try { panel = design.RibbonPanels[PanelId]; }
        catch { panel = design.RibbonPanels.Add("Шип-паз труб", PanelId, ClientId, "", false); }

        EnsureButton(panel, _createButton, true);
        EnsureButton(panel, _templateButton, false);
        EnsureButton(panel, _checkButton, false);
        EnsureButton(panel, _recognizeTubeButton, false);
        EnsureButton(panel, _deleteButton, false);

        // The recognition command is also useful when an IPT is opened directly.
        // Keep joint-creation commands assembly-only, but expose normalization on Model.
        try
        {
            var partRibbon = _application.UserInterfaceManager.Ribbons["Part"];
            var model = partRibbon.RibbonTabs["id_TabModel"];
            RibbonPanel partPanel;
            try { partPanel = model.RibbonPanels[PartPanelId]; }
            catch { partPanel = model.RibbonPanels.Add("Подготовка труб", PartPanelId, ClientId, "", false); }
            EnsureButton(partPanel, _recognizeTubeButton, true);
        }
        catch
        {
            // Some Inventor editions do not expose the Part ribbon during add-in
            // activation. The assembly command remains available in that case.
        }
    }

    private ButtonDefinition GetOrCreateButton(
        string displayName,
        string internalName,
        string toolTip,
        IEnumerable<string> iconCandidates,
        IEnumerable<string> iconKeywords)
    {
        if (_application is null) throw new InvalidOperationException("Inventor не инициализирован.");
        var definitions = _application.CommandManager.ControlDefinitions;
        ButtonDefinition result;
        try { result = (ButtonDefinition)definitions[internalName]; }
        catch
        {
            result = definitions.AddButtonDefinition(
                displayName,
                internalName,
                CommandTypesEnum.kShapeEditCmdType,
                ClientId,
                toolTip,
                toolTip,
                Type.Missing,
                Type.Missing,
                ButtonDisplayEnum.kDisplayTextInLearningMode);
        }

        var icon = FindBuiltInButton(iconCandidates, iconKeywords);
        if (icon is not null)
        {
            // Inventor exposes these properties as stdole.IPictureDisp. Dynamic COM
            // binding keeps the add-in independent of a machine-specific stdole path.
            try
            {
                dynamic target = result;
                dynamic source = icon;
                target.StandardIcon = source.StandardIcon;
                target.LargeIcon = source.LargeIcon;
            }
            catch { }
        }
        return result;
    }

    private ButtonDefinition? FindBuiltInButton(
        IEnumerable<string> candidates,
        IEnumerable<string> keywords)
    {
        if (_application is null) return null;
        var definitions = _application.CommandManager.ControlDefinitions;
        foreach (var name in candidates)
        {
            try
            {
                if (definitions[name] is ButtonDefinition button && button.BuiltIn) return button;
            }
            catch { }
        }
        foreach (ControlDefinition definition in definitions)
        {
            if (!definition.BuiltIn || definition is not ButtonDefinition button) continue;
            var text = (definition.InternalName + " " + definition.DisplayName + " " + definition.ToolTipText)
                .ToLowerInvariant();
            if (keywords.Any(keyword => text.Contains(keyword.ToLowerInvariant()))) return button;
        }
        return null;
    }

    private static void EnsureButton(RibbonPanel panel, ButtonDefinition definition, bool large)
    {
        foreach (CommandControl control in panel.CommandControls)
            if (string.Equals(control.ControlDefinition.InternalName, definition.InternalName,
                    StringComparison.OrdinalIgnoreCase)) return;
        panel.CommandControls.AddButton(definition, large, true, "", false);
    }

    private static void TryDeletePanel(Ribbon ribbon, string tabName, string panelName)
    {
        try { ribbon.RibbonTabs[tabName].RibbonPanels[panelName].Delete(); } catch { }
    }

    private void CreateButtonOnExecute(NameValueMap context) => _createCommand?.Execute();
    private void TemplateButtonOnExecute(NameValueMap context) => _templateCommand?.Execute();
    private void CheckButtonOnExecute(NameValueMap context) => _checkCommand?.Execute();
    private void DeleteButtonOnExecute(NameValueMap context) => _deleteCommand?.Execute();
    private void RecognizeTubeButtonOnExecute(NameValueMap context) => _recognizeTubeCommand?.Execute();
}
