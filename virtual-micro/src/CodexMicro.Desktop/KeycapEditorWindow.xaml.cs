using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using CodexMicro.Desktop.Services;

namespace CodexMicro.Desktop;

public partial class KeycapEditorWindow : Window
{
    private sealed record HarnessKeycap(
        string Id,
        string IconId,
        string DisplayText,
        string Label,
        string ActionId);

    private sealed record ActionChoice(
        string Kind,
        string DisplayName,
        string? Id = null,
        string? Path = null,
        string IconId = "EMPT1",
        bool IsAvailable = true)
    {
        public override string ToString() => DisplayName;
    }

    private readonly string _slotId;
    private readonly CodexMicroSlotBinding? _initialBinding;
    private readonly MicroLocalization _localization;
    private readonly CodexMicroConfigWriter? _configWriter;
    private readonly CodexMicroLayoutObserver? _layoutObserver;
    private readonly IReadOnlyList<CodexKeycapDefinition> _keycaps = [];
    private IReadOnlyList<CodexSkillDefinition> _skills = [];
    private readonly CancellationTokenSource _loadLifetime = new();
    private readonly Func<CancellationToken, Task<IReadOnlyList<CodexSkillDefinition>>> _readSkills;
    private readonly MicroHarnessRegistry? _harnessRegistry;
    private readonly string? _harnessId;
    private IReadOnlyList<HarnessKeycap> _harnessKeycaps = [];
    private bool _initialBindingApplied;
    private readonly MicroProfileSettings? _softwareProfile;

    internal KeycapEditorWindow(
        string slotId,
        CodexMicroSlotBinding binding,
        MicroLocalization localization,
        CodexMicroConfigWriter configWriter,
        CodexMicroLayoutObserver layoutObserver,
        Func<CancellationToken, Task<IReadOnlyList<CodexSkillDefinition>>>? readSkills = null,
        MicroProfileSettings? softwareProfile = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(slotId);
        _slotId = slotId;
        _initialBinding = binding ??
            throw new ArgumentNullException(nameof(binding));
        _softwareProfile = softwareProfile;
        _localization = localization ??
            throw new ArgumentNullException(nameof(localization));
        _configWriter = configWriter ??
            throw new ArgumentNullException(nameof(configWriter));
        _layoutObserver = layoutObserver ??
            throw new ArgumentNullException(nameof(layoutObserver));
        _keycaps = CodexKeycapCatalog.ForSlot(slotId);
        _readSkills = readSkills ?? CodexSkillCatalog.ReadInstalledAsync;

        InitializeComponent();
        Loaded += (_, _) => MicroWindowLayout.FitDialog(this);
        _localization.LanguageChanged += Localization_LanguageChanged;
        Closed += Window_Closed;
        ContentRendered += LoadSkills;
        KeycapList.ItemsSource = _keycaps;
        KeycapList.SelectedItem = _keycaps.FirstOrDefault(keycap =>
            keycap.Id == (_softwareProfile?.ResolveKeycapIcon(slotId, binding.KeycapId) ?? binding.KeycapId)) ?? _keycaps[0];
        RefreshLocalizedText();
    }

    internal KeycapEditorWindow(
        string controlId,
        string harnessId,
        MicroLocalization localization,
        MicroHarnessRegistry harnessRegistry)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(controlId);
        ArgumentException.ThrowIfNullOrWhiteSpace(harnessId);
        _slotId = controlId;
        _harnessId = harnessId;
        _localization = localization ??
            throw new ArgumentNullException(nameof(localization));
        _harnessRegistry = harnessRegistry ??
            throw new ArgumentNullException(nameof(harnessRegistry));
        _skills = [];
        _readSkills = CodexSkillCatalog.ReadInstalledAsync;
        _harnessKeycaps = CreateHarnessKeycaps(
            MicroHarnessControlIds.IsVoice(controlId));

        InitializeComponent();
        _localization.LanguageChanged += Localization_LanguageChanged;
        Closed += Window_Closed;
        KeycapList.ItemsSource = _harnessKeycaps;
        var current = _harnessRegistry.ResolveKeyMap(harnessId)
            .Resolve(controlId);
        KeycapList.SelectedItem = _harnessKeycaps.FirstOrDefault(item =>
            item.ActionId == current) ?? _harnessKeycaps[0];
        RefreshLocalizedText();
    }

    internal string SlotId => _slotId;

    private async void LoadSkills(object? sender, EventArgs e)
    {
        ContentRendered -= LoadSkills;
        var token = _loadLifetime.Token;
        try
        {
            _skills = await _readSkills(token);
            if (token.IsCancellationRequested || KeycapList.SelectedItem is not CodexKeycapDefinition keycap) return;
            var selected = ActionCombo.SelectedItem as ActionChoice;
            PopulateActionChoices(keycap, selected is { Kind: "skill" or "command", Id: { } id }
                ? new(selected.Kind, id, selected.Path) : null);
        }
        catch (OperationCanceledException) { }
        catch (Exception error) when (error is System.IO.IOException or UnauthorizedAccessException)
        {
            System.Diagnostics.Debug.WriteLine(error.Message);
        }
    }

    private bool IsHarnessEditor => _harnessRegistry is not null;

    private IReadOnlyList<HarnessKeycap> CreateHarnessKeycaps(bool voiceControl)
    {
        var english = _localization.IsEnglish;
        var items = new List<HarnessKeycap>
        {
            new("NEW", "NEW", english ? "NEW" : "新会话", english ? "New session" : "新建会话", MicroHarnessActionIds.NewSession),
            new("VIEW", "DIFF", english ? "CHAT ↔ TRACE" : "对话 ↔ 轨迹", english ? "Conversation / trajectory" : "对话 / 轨迹", MicroHarnessActionIds.ToggleConversationView),
            new("STOP", "REJ", english ? "STOP" : "停止", english ? "Stop generation" : "停止生成", MicroHarnessActionIds.CancelTurn),
            new("FORK", "SPLIT", english ? "FORK" : "分叉", english ? "Fork session" : "分叉会话", MicroHarnessActionIds.ForkSession),
            new("SIDEBAR", "NAV", english ? "SIDEBAR" : "侧边栏", english ? "Toggle sidebar" : "切换侧边栏", MicroHarnessActionIds.ToggleSidebar),
            new("DETAILS", "DIFF", english ? "DETAILS" : "详情", english ? "Open details" : "打开详情", MicroHarnessActionIds.OpenDetails),
            new("HISTORY", "TIME", english ? "HISTORY" : "更早历史", english ? "Load older history" : "加载更早历史", MicroHarnessActionIds.LoadOlderHistory),
            new("ARCHIVE", "DEL", english ? "ARCHIVE" : "归档", english ? "Archive session" : "归档会话", MicroHarnessActionIds.ArchiveSession),
            new("PREVIOUS", "NAV", english ? "PREVIOUS" : "上一会话", english ? "Previous session" : "上一个会话", MicroHarnessActionIds.PreviousSession),
            new("NEXT", "NAV", english ? "NEXT" : "下一会话", english ? "Next session" : "下一个会话", MicroHarnessActionIds.NextSession),
            new("HARNESS", "DEEPSEEK", english ? "HARNESS" : "打开 DSH", english ? "Open / focus Harness" : "打开 / 聚焦 Harness", MicroHarnessActionIds.ActivateSurface),
            new("GOAL", "GOAL", "GOAL", english ? "Set or view Goal" : "设置或查看 Goal", MicroHarnessActionIds.OpenGoal),
            new("NONE", "EMPT1", english ? "NONE" : "不分配", english ? "Unassigned" : "未分配", MicroHarnessActionIds.None),
        };
        if (voiceControl)
        {
            items.Insert(0, new(
                "MIC1",
                "MIC1",
                english ? "VOICE" : "语音",
                english ? "Push to talk" : "按住说话",
                MicroHarnessActionIds.VoiceDictation));
        }

        return items;
    }

    private void PopulateHarnessActionChoices(HarnessKeycap selected)
    {
        var choices = _harnessKeycaps.Select(item => new ActionChoice(
            "harness",
            item.Label,
            item.ActionId)).ToArray();
        ActionCombo.ItemsSource = choices;
        ActionCombo.SelectedItem = choices.First(choice =>
            choice.Id == selected.ActionId);
        AssignedDetailText.Text = _localization.IsEnglish
            ? $"Harness action: {selected.Label}"
            : $"Harness 动作：{selected.Label}";
    }

    private void PopulateActionChoices(
        CodexKeycapDefinition keycap,
        CodexMicroActionBinding? selectedAction = null,
        string? legacyCommandId = null)
    {
        var english = _localization.IsEnglish;
        var originalKeycap = CodexKeycapCatalog.Get(_initialBinding?.KeycapId ?? keycap.Id);
        var choices = new List<ActionChoice>();
        if (_softwareProfile is not null || !CodexActionCatalog.All.Any(c => c.Id == originalKeycap.DefaultAction))
            choices.Add(new("default", originalKeycap.Label, IconId: originalKeycap.Id,
                IsAvailable: _softwareProfile is null || CodexActionCatalog.SoftwareRoute(originalKeycap.DefaultAction) is not null));

        var commands = CodexActionCatalog.All
            .Where(item => _softwareProfile is null || item.Id != "codexMicroSettings")
            .OrderByDescending(item => item.SoftwareSupported)
            .ThenBy(item => item.Label, StringComparer.OrdinalIgnoreCase);
        choices.AddRange(commands.Select(command => new ActionChoice(
            "command",
            english ? command.Label : command.LabelZh ?? command.Label,
            command.Id,
            IconId: command.IconId,
            IsAvailable: _softwareProfile is null || command.SoftwareSupported)));

        var existingCommand = selectedAction is { Type: "command" }
            ? selectedAction.Id
            : legacyCommandId;
        if (!string.IsNullOrWhiteSpace(existingCommand) &&
            choices.All(choice => choice.Id != existingCommand))
        {
            choices.Add(new(
                "command",
                $"{(english ? "Command" : "命令")} · {existingCommand}",
                existingCommand,
                IsAvailable: _softwareProfile is null || CodexActionCatalog.SoftwareRoute(existingCommand) is not null));
        }

        foreach (var skill in _skills)
        {
            choices.Add(new(
                "skill",
                $"Skill · {skill.Name}",
                skill.Name,
                skill.SkillPath));
        }

        // Keep the saved skill intact even before the background catalog has arrived.
        if (selectedAction is { Type: "skill" } && !choices.Any(choice =>
            choice.Kind == "skill" && choice.Id == selectedAction.Id && choice.Path == selectedAction.SkillPath))
            choices.Add(new("skill", $"Skill · {selectedAction.Id}", selectedAction.Id, selectedAction.SkillPath));

        ActionCombo.ItemsSource = choices;
        ActionCombo.SelectedItem = selectedAction switch
        {
            { Type: "command" } => choices.FirstOrDefault(choice =>
                choice.Kind == "command" && choice.Id == selectedAction.Id),
            { Type: "skill" } => choices.FirstOrDefault(choice =>
                choice.Kind == "skill" &&
                choice.Id == selectedAction.Id &&
                choice.Path == selectedAction.SkillPath),
            _ when !string.IsNullOrWhiteSpace(legacyCommandId) =>
                choices.FirstOrDefault(choice =>
                    choice.Kind == "command" && choice.Id == legacyCommandId),
            _ => choices.FirstOrDefault(choice => choice.Kind == "default") ??
                choices.First(choice => choice.Id == originalKeycap.DefaultAction),
        } ?? choices[0];
        AssignedDetailText.Text = english
            ? $"Keycap default: {keycap.Label}"
            : $"键帽默认：{keycap.Label}";
    }

    private void KeycapList_SelectionChanged(
        object sender,
        SelectionChangedEventArgs e)
    {
        if (IsHarnessEditor)
        {
            if (KeycapList.SelectedItem is HarnessKeycap harnessKeycap)
            {
                PopulateHarnessActionChoices(harnessKeycap);
            }
            return;
        }

        if (KeycapList.SelectedItem is not CodexKeycapDefinition keycap)
        {
            return;
        }

        var initialBinding = _initialBinding;
        if (initialBinding is null)
        {
            return;
        }

        if (!_initialBindingApplied)
        {
            _initialBindingApplied = true;
            PopulateActionChoices(
                keycap,
                initialBinding.Action,
                initialBinding.CommandId);
        }
        else
        {
            // Appearance never changes the selected action, including a default
            // microphone/empty key whose behavior is tied to its original keycap.
            return;
        }
    }

    private void SearchBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        SearchPlaceholderText.Visibility = string.IsNullOrEmpty(SearchBox.Text)
            ? Visibility.Visible
            : Visibility.Collapsed;
        var query = SearchBox.Text.Trim();
        var view = CollectionViewSource.GetDefaultView(KeycapList.ItemsSource);
        view.Filter = item => item switch
        {
            CodexKeycapDefinition keycap => query.Length == 0 ||
                keycap.Id.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                keycap.Label.Contains(query, StringComparison.OrdinalIgnoreCase),
            HarnessKeycap keycap => query.Length == 0 ||
                keycap.Id.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                keycap.Label.Contains(query, StringComparison.OrdinalIgnoreCase),
            _ => false,
        };
        view.Refresh();
    }

    private void SaveButton_Click(object sender, RoutedEventArgs e)
    {
        if (IsHarnessEditor)
        {
            if (_harnessRegistry is null ||
                _harnessId is null ||
                ActionCombo.SelectedItem is not ActionChoice
                {
                    Id: { Length: > 0 } actionId,
                })
            {
                return;
            }

            if (!_harnessRegistry.UpdateKeyMapping(
                _harnessId,
                _slotId,
                actionId))
            {
                EditorStatusText.Text = _localization.IsEnglish
                    ? "Could not save the Harness key mapping."
                    : "无法保存 Harness 键位映射。";
                return;
            }

            DialogResult = true;
            return;
        }

        if (KeycapList.SelectedItem is not CodexKeycapDefinition keycap ||
            ActionCombo.SelectedItem is not ActionChoice actionChoice)
        {
            return;
        }

        CodexMicroActionBinding? action = actionChoice.Kind switch
        {
            "default" => null,
            "command" when actionChoice.Id is { Length: > 0 } commandId =>
                new("command", commandId),
            "skill" when actionChoice.Id is { Length: > 0 } skillName &&
                actionChoice.Path is { Length: > 0 } skillPath =>
                new("skill", skillName, skillPath),
            _ => null,
        };
        if (_configWriter is null ||
            !_configWriter.SetSlot(_slotId,
                _softwareProfile is not null ? _initialBinding!.KeycapId : keycap.Id, action))
        {
            EditorStatusText.Text = _localization.IsEnglish
                ? "Could not save the Codex configuration."
                : "无法写入 Codex 配置。";
            EditorStatusText.Foreground = new SolidColorBrush(
                Color.FromRgb(0xB0, 0x6B, 0x4F));
            return;
        }

        if (_softwareProfile is not null)
        {
            _softwareProfile.SetKeycapIcon(_slotId, keycap.Id);
            if (!_softwareProfile.LastSaveSucceeded)
            {
                EditorStatusText.Text = _localization.IsEnglish ? "Could not save icon." : "图标保存失败。";
                return;
            }
        }
        _layoutObserver?.ReloadNow();
        DialogResult = true;
    }

    private void RefreshLocalizedText()
    {
        var english = _localization.IsEnglish;
        if (IsHarnessEditor)
        {
            var selectedAction = (KeycapList.SelectedItem as HarnessKeycap)
                ?.ActionId ?? _harnessRegistry?.ResolveKeyMap(_harnessId!)
                    .Resolve(_slotId);
            _harnessKeycaps = CreateHarnessKeycaps(
                MicroHarnessControlIds.IsVoice(_slotId));
            KeycapList.ItemsSource = _harnessKeycaps;
            KeycapList.SelectedItem = _harnessKeycaps.FirstOrDefault(item =>
                item.ActionId == selectedAction) ?? _harnessKeycaps[0];
        }

        Title = english ? "Edit keycap" : "编辑键帽";
        EditorTitleText.Text = Title;
        EditorSubtitleText.Text = _slotId;
        SearchPlaceholderText.Text = english ? "Search keycaps" : "搜索键帽";
        AssignedTitleText.Text = english ? "Action" : "动作";
        CancelButton.Content = english ? "Cancel" : "取消";
        SaveButton.Content = english ? "Save" : "保存";
        if (KeycapList.SelectedItem is HarnessKeycap harnessKeycap)
        {
            PopulateHarnessActionChoices(harnessKeycap);
        }
        else if (KeycapList.SelectedItem is CodexKeycapDefinition keycap &&
            _initialBinding is not null)
        {
            var selected = ActionCombo.SelectedItem as ActionChoice;
            PopulateActionChoices(
                keycap,
                selected is { Kind: "command" or "skill", Id: { } id }
                    ? new(selected.Kind, id, selected.Path) : selected is null ? _initialBinding.Action : null,
                selected is null ? _initialBinding.CommandId : null);
        }
    }

    private void TitleBar_MouseLeftButtonDown(
        object sender,
        MouseButtonEventArgs e)
    {
        MicroWindowLayout.DragTitle(this, e);
    }

    private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            e.Handled = true;
            Close();
        }
    }

    private void CancelButton_Click(object sender, RoutedEventArgs e) => Close();

    private void Localization_LanguageChanged(object? sender, EventArgs e) =>
        Dispatcher.Invoke(RefreshLocalizedText);

    private void Window_Closed(object? sender, EventArgs e)
    {
        _loadLifetime.Cancel();
        _loadLifetime.Dispose();
        ContentRendered -= LoadSkills;
        _localization.LanguageChanged -= Localization_LanguageChanged;
        Closed -= Window_Closed;
    }
}
