using System.Windows.Input;
using CodexController.Controllers;
using CodexController.Localization;
using CodexController.Models;
using CodexController.Presentation;
using CodexController.Presentation.Dispatch;

namespace CodexController.ViewModels;

public sealed record ControllerTutorialItem(
    string Glyph,
    string Title,
    string Description)
{
    public bool HasDescription =>
        !string.IsNullOrWhiteSpace(Description);

    public string AccessibleName =>
        HasDescription
            ? $"{Glyph}, {Title}, {Description}"
            : $"{Glyph}, {Title}";
}

/// <summary>
/// Interactive, read-only controller guide for the dashboard. It follows
/// physical layer inputs but never executes an action itself.
/// </summary>
public sealed class ControllerTutorialViewModel : ObservableObject
{
    private readonly Action? _refreshDispatchPresentation;
    private LocalizedStrings? _strings;
    private AppLanguage? _presentationLanguage;
    private ControllerProfile _profile =
        BuiltInControllerProfiles.Generic;
    private ControllerTutorialMode _mode =
        ControllerTutorialMode.Overview;
    private ControllerButtons _observedButtons;
    private DispatchDisplay? _dispatchDisplay;
    private string _heading = string.Empty;
    private string _subheading = string.Empty;
    private string _overviewTabLabel = string.Empty;
    private string _actionTabLabel = string.Empty;
    private string _agentTabLabel = string.Empty;
    private string _turnTabLabel = string.Empty;
    private string _commandTabLabel = string.Empty;
    private string _stickPressTabLabel = string.Empty;
    private string _gestureGlyph = string.Empty;
    private string _modeTitle = string.Empty;
    private string _modeDescription = string.Empty;
    private string _leftTriggerGlyph = string.Empty;
    private string _leftShoulderGlyph = string.Empty;
    private string _rightShoulderGlyph = string.Empty;
    private string _rightTriggerGlyph = string.Empty;
    private string _viewGlyph = string.Empty;
    private string _menuGlyph = string.Empty;
    private string _leftStickPressGlyph = string.Empty;
    private string _rightStickPressGlyph = string.Empty;
    private string _stickPressGuideTitle = string.Empty;
    private string _leftStickPressGuide = string.Empty;
    private string _rightStickPressGuide = string.Empty;
    private string _leftStickHint = string.Empty;
    private string _rightStickHint = string.Empty;
    private IReadOnlyList<ControllerTutorialItem> _items =
        Array.Empty<ControllerTutorialItem>();

    public ControllerTutorialViewModel(
        Action? refreshDispatchPresentation = null)
    {
        _refreshDispatchPresentation = refreshDispatchPresentation;
        SelectOverviewCommand = new RelayCommand(
            () => SelectMode(ControllerTutorialMode.Overview));
        SelectActionCommand = new RelayCommand(
            () => SelectMode(ControllerTutorialMode.Action));
        SelectAgentCommand = new RelayCommand(
            () => SelectMode(ControllerTutorialMode.Agent));
        SelectTurnCommand = new RelayCommand(
            () => SelectMode(ControllerTutorialMode.Turn));
        SelectCommandCommand = new RelayCommand(SelectCommandLesson);
        SelectStickPressCommand = new RelayCommand(
            () => SelectMode(ControllerTutorialMode.StickPress));
    }

    public ICommand SelectOverviewCommand { get; }

    public ICommand SelectActionCommand { get; }

    public ICommand SelectAgentCommand { get; }

    public ICommand SelectTurnCommand { get; }

    public ICommand SelectCommandCommand { get; }

    public ICommand SelectStickPressCommand { get; }

    public ControllerTutorialMode Mode
    {
        get => _mode;
        private set
        {
            if (!SetProperty(ref _mode, value))
            {
                return;
            }

            RaiseModeProperties();
            RefreshPresentation();
        }
    }

    public bool IsOverviewMode =>
        Mode == ControllerTutorialMode.Overview;

    public bool IsActionMode =>
        Mode == ControllerTutorialMode.Action;

    public bool IsAgentMode =>
        Mode == ControllerTutorialMode.Agent;

    public bool IsTurnMode =>
        Mode == ControllerTutorialMode.Turn;

    public bool IsCommandMode =>
        Mode == ControllerTutorialMode.Command;

    public bool IsStickPressMode =>
        Mode == ControllerTutorialMode.StickPress;

    public bool HighlightDPad =>
        IsActionMode || IsAgentMode;

    public bool HighlightFaceCluster =>
        IsActionMode || IsCommandMode || IsTurnMode;

    public bool HighlightViewMenu =>
        IsAgentMode || IsCommandMode;

    public string Heading
    {
        get => _heading;
        private set => SetProperty(ref _heading, value);
    }

    public string Subheading
    {
        get => _subheading;
        private set => SetProperty(ref _subheading, value);
    }

    public string OverviewTabLabel
    {
        get => _overviewTabLabel;
        private set => SetProperty(ref _overviewTabLabel, value);
    }

    public string ActionTabLabel
    {
        get => _actionTabLabel;
        private set => SetProperty(ref _actionTabLabel, value);
    }

    public string AgentTabLabel
    {
        get => _agentTabLabel;
        private set => SetProperty(ref _agentTabLabel, value);
    }

    public string TurnTabLabel
    {
        get => _turnTabLabel;
        private set => SetProperty(ref _turnTabLabel, value);
    }

    public string CommandTabLabel
    {
        get => _commandTabLabel;
        private set => SetProperty(ref _commandTabLabel, value);
    }

    public string StickPressTabLabel
    {
        get => _stickPressTabLabel;
        private set => SetProperty(ref _stickPressTabLabel, value);
    }

    public string GestureGlyph
    {
        get => _gestureGlyph;
        private set => SetProperty(ref _gestureGlyph, value);
    }

    public string ModeTitle
    {
        get => _modeTitle;
        private set => SetProperty(ref _modeTitle, value);
    }

    public string ModeDescription
    {
        get => _modeDescription;
        private set => SetProperty(ref _modeDescription, value);
    }

    public string LeftTriggerGlyph
    {
        get => _leftTriggerGlyph;
        private set => SetProperty(ref _leftTriggerGlyph, value);
    }

    public string LeftShoulderGlyph
    {
        get => _leftShoulderGlyph;
        private set => SetProperty(ref _leftShoulderGlyph, value);
    }

    public string RightShoulderGlyph
    {
        get => _rightShoulderGlyph;
        private set => SetProperty(ref _rightShoulderGlyph, value);
    }

    public string RightTriggerGlyph
    {
        get => _rightTriggerGlyph;
        private set => SetProperty(ref _rightTriggerGlyph, value);
    }

    public string ViewGlyph
    {
        get => _viewGlyph;
        private set => SetProperty(ref _viewGlyph, value);
    }

    public string MenuGlyph
    {
        get => _menuGlyph;
        private set => SetProperty(ref _menuGlyph, value);
    }

    public string LeftStickPressGlyph
    {
        get => _leftStickPressGlyph;
        private set => SetProperty(ref _leftStickPressGlyph, value);
    }

    public string RightStickPressGlyph
    {
        get => _rightStickPressGlyph;
        private set => SetProperty(ref _rightStickPressGlyph, value);
    }

    public string StickPressGuideTitle
    {
        get => _stickPressGuideTitle;
        private set => SetProperty(ref _stickPressGuideTitle, value);
    }

    public string LeftStickPressGuide
    {
        get => _leftStickPressGuide;
        private set => SetProperty(ref _leftStickPressGuide, value);
    }

    public string RightStickPressGuide
    {
        get => _rightStickPressGuide;
        private set => SetProperty(ref _rightStickPressGuide, value);
    }

    public IReadOnlyList<ControllerTutorialItem> Items
    {
        get => _items;
        private set => SetProperty(ref _items, value);
    }

    public void UpdateContext(
        LocalizedStrings strings,
        ControllerProfile profile,
        string leftStickHint,
        string rightStickHint)
    {
        ArgumentNullException.ThrowIfNull(strings);
        ArgumentNullException.ThrowIfNull(profile);
        ArgumentNullException.ThrowIfNull(leftStickHint);
        ArgumentNullException.ThrowIfNull(rightStickHint);

        var languageChanged =
            _presentationLanguage != strings.Language;
        _strings = strings;
        _presentationLanguage = strings.Language;
        _profile = profile;
        LeftTriggerGlyph = Glyph(LogicalInput.LeftTrigger);
        LeftShoulderGlyph = Glyph(LogicalInput.LeftShoulder);
        RightShoulderGlyph = Glyph(LogicalInput.RightShoulder);
        RightTriggerGlyph = Glyph(LogicalInput.RightTrigger);
        ViewGlyph = Glyph(LogicalInput.View);
        MenuGlyph = Glyph(LogicalInput.Menu);
        LeftStickPressGlyph = Glyph(LogicalInput.LeftStickPress);
        RightStickPressGlyph = Glyph(LogicalInput.RightStickPress);
        _leftStickHint = leftStickHint;
        _rightStickHint = rightStickHint;
        if (_dispatchDisplay is null || languageChanged)
        {
            _dispatchDisplay = new DispatchDisplayResolver(strings).Resolve(
                DispatchTurnState.Unknown,
                DispatchFollowUpBehavior.Unknown);
        }

        Heading = Text("手柄动态教程", "Interactive controller guide");
        Subheading = Text(
            "点击分层预览；实际按下对应按键也会自动切换",
            "Choose a layer, or press its button on the controller");
        OverviewTabLabel = Text("基础", "Basics");
        ActionTabLabel = Text(
            $"点按 {Glyph(LogicalInput.FaceNorth)}",
            $"Tap {Glyph(LogicalInput.FaceNorth)}");
        AgentTabLabel = Text(
            $"按住 {LeftShoulderGlyph}",
            $"Hold {LeftShoulderGlyph}");
        TurnTabLabel = Text(
            $"扣住 {RightTriggerGlyph}",
            $"Hold {RightTriggerGlyph}");
        CommandTabLabel = Text(
            $"按住 {RightShoulderGlyph}",
            $"Hold {RightShoulderGlyph}");
        StickPressTabLabel = Text("按下摇杆", "Press sticks");
        StickPressGuideTitle = Text(
            "L3 / R3 指的是把摇杆帽垂直按下",
            "L3 / R3 mean pressing the stick caps straight down");
        LeftStickPressGuide = Text(
            $"{LeftStickPressGlyph} / L3：按下左摇杆，切换任务根区域",
            $"{LeftStickPressGlyph} / L3: press the left stick to change task roots");
        RightStickPressGuide = Text(
            $"{RightStickPressGlyph} / R3：短按 Micro 旋钮；按住 500ms 打开设置",
            $"{RightStickPressGlyph} / R3: tap the Micro encoder; hold 500ms for settings");

        RefreshPresentation();
    }

    public void SelectMode(ControllerTutorialMode mode)
    {
        if (!Enum.IsDefined(mode))
        {
            throw new ArgumentOutOfRangeException(nameof(mode));
        }

        Mode = mode;
    }

    private void SelectCommandLesson()
    {
        // A manual visit should describe the button Codex exposes now, while
        // automatic layer entry receives the same value from the overlay
        // builder without adding another UIA probe to controller polling.
        _refreshDispatchPresentation?.Invoke();
        SelectMode(ControllerTutorialMode.Command);
    }

    /// <summary>
    /// Observes only the two stick-button edges. Shoulder, trigger, and action
    /// layers are synchronized separately from the authoritative runtime
    /// coordinator so a quick LB/RB tap is not presented as a hold gesture.
    /// </summary>
    public void ObserveStickPresses(ControllerState state)
    {
        if (!state.IsConnected)
        {
            _observedButtons = ControllerButtons.None;
            return;
        }

        var down = state.Buttons & ~_observedButtons;
        if (
            down.HasFlag(ControllerButtons.LeftThumb) ||
            down.HasFlag(ControllerButtons.RightThumb))
        {
            SelectMode(ControllerTutorialMode.StickPress);
        }

        _observedButtons = state.Buttons;
    }

    /// <summary>
    /// Follows the layer already accepted by <see cref="RadialLayerCoordinator"/>.
    /// Releasing a layer keeps its lesson visible so the user can read it.
    /// </summary>
    internal void UpdateActiveLayer(
        RadialMenuLayerKind? layer,
        bool isEngaged,
        bool isCancelled)
    {
        if (layer is null || !isEngaged || isCancelled)
        {
            return;
        }

        SelectMode(layer.Value switch
        {
            RadialMenuLayerKind.Agent => ControllerTutorialMode.Agent,
            RadialMenuLayerKind.Command => ControllerTutorialMode.Command,
            RadialMenuLayerKind.Turn => ControllerTutorialMode.Turn,
            RadialMenuLayerKind.Action => ControllerTutorialMode.Action,
            _ => throw new ArgumentOutOfRangeException(nameof(layer)),
        });
    }

    internal void UpdateDispatchPresentation(DispatchDisplay display)
    {
        ArgumentNullException.ThrowIfNull(display);
        if (_dispatchDisplay == display)
        {
            return;
        }

        _dispatchDisplay = display;
        if (IsCommandMode)
        {
            RefreshPresentation();
        }
    }

    private void RefreshPresentation()
    {
        if (_strings is null)
        {
            return;
        }

        var language = _strings.Language;
        switch (Mode)
        {
            case ControllerTutorialMode.Overview:
                GestureGlyph = Text("基础", "BASE");
                ModeTitle = Text("先认识基础操作", "Start with the basics");
                ModeDescription = Text(
                    "左右摇杆负责浏览和 Micro 控制；常用动作保持单键直达。",
                    "The sticks handle navigation and Micro control; common actions stay one button away.");
                Items = OverviewItems();
                break;
            case ControllerTutorialMode.Action:
                GestureGlyph = Glyph(LogicalInput.FaceNorth);
                ModeTitle = Text("点按 Y：动作面板", "Tap Y: action panel");
                ModeDescription = Text(
                    "Y 再按一次或 B 关闭。这里的箭头都表示十字键。",
                    "Tap Y again or B to close. Arrows here always mean the D-pad.");
                Items = LayerItems(
                    ControllerLayerPresentationFactory.Action(
                        language,
                        new ControllerActionPresentationOptions(
                            Glyph(LogicalInput.FaceSouth))));
                break;
            case ControllerTutorialMode.Agent:
                GestureGlyph = LeftShoulderGlyph;
                ModeTitle = Text(
                    "按住 LB：六个 Agent 任务",
                    "Hold LB: six Agent tasks");
                ModeDescription = Text(
                    "保持 LB 按下，再按对应键切换；短按 LB 是上一个任务。",
                    "Keep LB held and press a mapped key; a quick tap opens the previous task.");
                Items = AgentItems();
                break;
            case ControllerTutorialMode.Turn:
                GestureGlyph = RightTriggerGlyph;
                ModeTitle = Text(
                    "扣住 RT：运行中操作",
                    "Hold RT: active-turn actions");
                ModeDescription = Text(
                    "持续扣住扳机选择操作；松开 RT 即关闭这一层。",
                    "Keep the trigger held while choosing an action; release RT to close the layer.");
                Items = LayerItems(
                    ControllerLayerPresentationFactory.Turn(language));
                break;
            case ControllerTutorialMode.Command:
                GestureGlyph = RightShoulderGlyph;
                ModeTitle = Text(
                    "按住 RB：Codex 命令",
                    "Hold RB: Codex commands");
                ModeDescription = Text(
                    "保持 RB 按下；短按 RB 是下一个任务，L3 可取消。",
                    "Keep RB held; a quick tap opens the next task, and L3 cancels.");
                var dispatch = _dispatchDisplay ??
                    new DispatchDisplayResolver(_strings).Resolve(
                        DispatchTurnState.Unknown,
                        DispatchFollowUpBehavior.Unknown);
                Items = LayerItems(
                    ControllerLayerPresentationFactory.Command(
                        language,
                        new ControllerCommandPresentationOptions(
                            _strings.ConfigToggleFast,
                            _strings.ConfigDictation,
                            dispatch.Label,
                            dispatch.Description,
                            Glyph(LogicalInput.FaceSouth))));
                break;
            case ControllerTutorialMode.StickPress:
                GestureGlyph = "L3 / R3";
                ModeTitle = Text(
                    "垂直按下摇杆帽",
                    "Press each stick cap vertically");
                ModeDescription = Text(
                    "不是把摇杆往屏幕下方拨；请垂直按压摇杆帽，直到有咔哒手感。",
                    "Do not move the stick downward; press the cap vertically until it clicks.");
                Items = StickPressItems();
                break;
            default:
                throw new ArgumentOutOfRangeException();
        }
    }

    private IReadOnlyList<ControllerTutorialItem> OverviewItems() =>
    [
        new(
            Glyph(LogicalInput.LeftStick),
            Text("左摇杆：浏览任务", "Left stick: browse tasks"),
            string.IsNullOrWhiteSpace(_leftStickHint)
                ? Text(
                "上下移动，左右进入或退出项目",
                "Move vertically; enter or leave projects horizontally")
                : _leftStickHint),
        new(
            Glyph(LogicalInput.RightStick),
            Text("右摇杆：Micro 控制", "Right stick: Micro control"),
            string.IsNullOrWhiteSpace(_rightStickHint)
                ? Text(
                "上或左选上一项，下或右选下一项；按 R3 进入或确认",
                "Up/left selects previous; down/right selects next; press R3 to enter or confirm")
                : _rightStickHint),
        new(
            "↑↓",
            Text("十字键：遍历对话", "D-pad: browse turns"),
            Text(
                "长按上回到顶部，长按下回到底部",
                "Hold up for the top; hold down for the bottom")),
        new(
            LeftTriggerGlyph,
            Text("按住说话", "Hold to talk"),
            Text("松开结束录音", "Release to stop recording")),
        new(
            Glyph(LogicalInput.FaceWest),
            Text("发送当前输入", "Send current input"),
            string.Empty),
        new(
            ViewGlyph,
            Text("View：保留键", "View: reserved"),
            Text(
                "当前不执行操作；后续可能用于切换控制不同 Agent",
                "No action yet; it may switch the controlled Agent in a future version")),
        new(
            MenuGlyph,
            Text("Menu：唤醒 Codex", "Menu: wake Codex"),
            Text("需要时将 Codex 置于前台", "Bring Codex to the foreground when needed")),
    ];

    private IReadOnlyList<ControllerTutorialItem> AgentItems()
    {
        return AgentRadialSlotLayout.Bindings
            .Select((binding, index) =>
                new ControllerTutorialItem(
                    Glyph(binding.Input),
                    Text(
                        $"Agent {index + 1}",
                        $"Agent {index + 1}"),
                    index < 4
                        ? Text("十字键槽位", "D-pad slot")
                        : binding.Input == LogicalInput.View
                            ? Text("View / 双窗口键", "View button")
                            : Text("Menu / 三横线键", "Menu button")))
            .ToArray();
    }

    private IReadOnlyList<ControllerTutorialItem> StickPressItems() =>
    [
        new(
            $"{LeftStickPressGlyph} / L3",
            Text("按下左摇杆", "Press the left stick"),
            Text(
                "切换置顶任务、置顶项目、项目和未归项目任务",
                "Cycle pinned tasks, pinned projects, projects, and projectless tasks")),
        new(
            $"{RightStickPressGlyph} / R3",
            Text("按下右摇杆", "Press the right stick"),
            Text(
                "短按 Micro 旋钮；按住 500ms 打开 Agent Controller 设置",
                "Tap the Micro encoder; hold 500ms for Agent Controller settings")),
    ];

    private IReadOnlyList<ControllerTutorialItem> LayerItems(
        IEnumerable<ControllerLayerItemPresentation> items) =>
        items.Select(item =>
                new ControllerTutorialItem(
                    Glyph(item.Input),
                    item.Title,
                    item.Description ?? string.Empty))
            .ToArray();

    private string Glyph(LogicalInput input) =>
        _profile.GetGlyph(input);

    private string Text(string zhCn, string enUs) =>
        ControllerLayerPresentationFactory.Text(
            _strings?.Language ?? AppLanguage.EnUs,
            zhCn,
            enUs);

    private void RaiseModeProperties()
    {
        OnPropertyChanged(nameof(IsOverviewMode));
        OnPropertyChanged(nameof(IsActionMode));
        OnPropertyChanged(nameof(IsAgentMode));
        OnPropertyChanged(nameof(IsTurnMode));
        OnPropertyChanged(nameof(IsCommandMode));
        OnPropertyChanged(nameof(IsStickPressMode));
        OnPropertyChanged(nameof(HighlightDPad));
        OnPropertyChanged(nameof(HighlightFaceCluster));
        OnPropertyChanged(nameof(HighlightViewMenu));
    }
}
