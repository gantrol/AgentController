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
        string? Path = null)
    {
        public override string ToString() => DisplayName;
    }

    private readonly string _slotId;
    private readonly CodexMicroSlotBinding? _initialBinding;
    private readonly MicroLocalization _localization;
    private readonly CodexMicroConfigWriter? _configWriter;
    private readonly CodexMicroLayoutObserver? _layoutObserver;
    private readonly IReadOnlyList<CodexKeycapDefinition> _keycaps = [];
    private readonly IReadOnlyList<CodexSkillDefinition> _skills;
    private readonly MicroHarnessRegistry? _harnessRegistry;
    private readonly string? _harnessId;
    private IReadOnlyList<HarnessKeycap> _harnessKeycaps = [];
    private bool _initialBindingApplied;

    internal KeycapEditorWindow(
        string slotId,
        CodexMicroSlotBinding binding,
        MicroLocalization localization,
        CodexMicroConfigWriter configWriter,
        CodexMicroLayoutObserver layoutObserver)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(slotId);
        _slotId = slotId;
        _initialBinding = binding ??
            throw new ArgumentNullException(nameof(binding));
        _localization = localization ??
            throw new ArgumentNullException(nameof(localization));
        _configWriter = configWriter ??
            throw new ArgumentNullException(nameof(configWriter));
        _layoutObserver = layoutObserver ??
            throw new ArgumentNullException(nameof(layoutObserver));
        _keycaps = CodexKeycapCatalog.ForSlot(slotId);
        _skills = CodexSkillCatalog.ReadInstalled();

        InitializeComponent();
        _localization.LanguageChanged += Localization_LanguageChanged;
        Closed += Window_Closed;
        KeycapList.ItemsSource = _keycaps;
        KeycapList.SelectedItem = _keycaps.FirstOrDefault(keycap =>
            keycap.Id == binding.KeycapId) ?? _keycaps[0];
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
        var choices = new List<ActionChoice>
        {
            new(
                "default",
                english ? "Keycap default" : "使用键帽默认动作"),
        };

        var commands = CodexKeycapCatalog.All
            .GroupBy(item => item.DefaultAction, StringComparer.Ordinal)
            .Select(group => group.First())
            .OrderBy(item => item.Label, StringComparer.OrdinalIgnoreCase);
        choices.AddRange(commands.Select(command => new ActionChoice(
            "command",
            $"{(english ? "Command" : "命令")} · {command.Label}",
            command.DefaultAction)));

        var existingCommand = selectedAction is { Type: "command" }
            ? selectedAction.Id
            : legacyCommandId;
        if (!string.IsNullOrWhiteSpace(existingCommand) &&
            choices.All(choice => choice.Id != existingCommand))
        {
            choices.Add(new(
                "command",
                $"{(english ? "Command" : "命令")} · {existingCommand}",
                existingCommand));
        }

        foreach (var skill in _skills)
        {
            choices.Add(new(
                "skill",
                $"Skill · {skill.Name}",
                skill.Name,
                skill.SkillPath));
        }

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
            _ => choices[0],
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

        if (!_initialBindingApplied && keycap.Id == initialBinding.KeycapId)
        {
            _initialBindingApplied = true;
            PopulateActionChoices(
                keycap,
                initialBinding.Action,
                initialBinding.CommandId);
        }
        else
        {
            _initialBindingApplied = true;
            PopulateActionChoices(keycap);
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
            !_configWriter.SetSlot(_slotId, keycap.Id, action))
        {
            EditorStatusText.Text = _localization.IsEnglish
                ? "Could not save the Codex configuration."
                : "无法写入 Codex 配置。";
            EditorStatusText.Foreground = new SolidColorBrush(
                Color.FromRgb(0xB0, 0x6B, 0x4F));
            return;
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
        EditorSubtitleText.Text = IsHarnessEditor
            ? english
                ? $"Choose the native Harness action on {_slotId}"
                : $"选择 {_slotId} 的 Harness 原生动作"
            : english
                ? $"Choose what appears on {_slotId}"
                : $"选择 {_slotId} 上显示的内容";
        SearchPlaceholderText.Text = english ? "Search keycaps" : "搜索键帽";
        AssignedTitleText.Text = IsHarnessEditor
            ? english ? "Assigned Harness action" : "已分配的 Harness 动作"
            : english ? "Assigned shortcut or skill" : "已分配的快捷操作或 Skill";
        CancelButton.Content = english ? "Cancel" : "取消";
        SaveButton.Content = english ? "Save" : "保存";
        if (KeycapList.SelectedItem is HarnessKeycap harnessKeycap)
        {
            PopulateHarnessActionChoices(harnessKeycap);
        }
        else if (KeycapList.SelectedItem is CodexKeycapDefinition keycap &&
            _initialBinding is not null)
        {
            PopulateActionChoices(
                keycap,
                _initialBinding.Action,
                _initialBinding.CommandId);
        }
    }

    private void TitleBar_MouseLeftButtonDown(
        object sender,
        MouseButtonEventArgs e)
    {
        if (e.LeftButton == MouseButtonState.Pressed)
        {
            DragMove();
        }
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
        _localization.LanguageChanged -= Localization_LanguageChanged;
        Closed -= Window_Closed;
    }
}
