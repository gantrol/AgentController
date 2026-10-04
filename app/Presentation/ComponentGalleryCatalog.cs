using System.Windows;
using System.Windows.Automation;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using CodexController.Localization;
using CodexController.Controllers;
using CodexController.Models;
using CodexController.Views;
using CodexController.ViewModels;
using Brush = System.Windows.Media.Brush;
using HorizontalAlignment = System.Windows.HorizontalAlignment;
using Controls = System.Windows.Controls;

namespace CodexController.Presentation;

internal sealed class ComponentGallerySample(string category, string key, Func<FrameworkElement> createPreview)
{
    private FrameworkElement? _preview;
    public string Category { get; } = category;
    public string Key { get; } = key;
    public FrameworkElement Preview => _preview ??= createPreview();
}

internal static class ComponentGalleryCatalog
{
    public static string Text(string zh, string en) =>
        LocalizationHost.Current.EffectiveLanguage == AppLanguage.ZhCn ? zh : en;

    public static IReadOnlyList<ComponentGallerySample> Build()
    {
        var samples = new List<ComponentGallerySample>();
        var resources = ThemeResources();
        var foundations = Text("基础样式", "Foundations");
        var actions = Text("操作控件", "Actions");
        var inputs = Text("输入控件", "Inputs");
        var navigation = Text("导航", "Navigation");
        var lists = Text("列表与滚动", "Lists & scrolling");
        var feedback = Text("状态与反馈", "Status & feedback");
        var surfaces = Text("容器与材质", "Surfaces");
        var icons = Text("图标与按键", "Icons & keys");

        void Add(string category, string key, Func<FrameworkElement> preview) =>
            samples.Add(new(category, key, preview));

        foreach (var family in resources.Keys.Where(key => key.StartsWith("Brush.", StringComparison.Ordinal))
                     .GroupBy(key => string.Join('.', key.Split('.').Take(2))))
        {
            var keys = family.ToArray();
            Add(foundations, family.Key + ".*", () => Swatches(keys));
        }
        Add(foundations, "Font.Size.*", () => Column(
            new[] { "Caption", "Label", "Body", "BodyLg", "Title", "TitleLg", "Display", "Hero" }
                .Select(name => (FrameworkElement)Label($"{name} · 中文 Aa 012", "Font.Size." + name)).ToArray()));
        Add(foundations, "Space.*", () => Row(
            resources.Keys.Where(key => key.StartsWith("Space.", StringComparison.Ordinal))
                .Select(key => (FrameworkElement)Column(
                    Label(key), new Controls.Border { Width = Resource<double>(key), Height = 24,
                        Background = Resource<Brush>("Brush.Accent.Soft"), HorizontalAlignment = HorizontalAlignment.Left }))
                .ToArray()));
        Add(foundations, "Radius.*", () => Row(
            resources.Keys.Where(key => key.StartsWith("Radius.", StringComparison.Ordinal))
                .Select(key => (FrameworkElement)Column(Label(key), new Controls.Border
                {
                    Width = 56, Height = 42, CornerRadius = Resource<CornerRadius>(key),
                    BorderBrush = Resource<Brush>("Brush.Border.Strong"), BorderThickness = new Thickness(1),
                    Background = Resource<Brush>("Brush.Bg.Panel"),
                })).ToArray()));
        Add(foundations, "Control.H.*", () => Column(
            new[] { "SM", "MD", "LG" }.Select(name => (FrameworkElement)new Controls.Border
            {
                Height = Resource<double>("Control.H." + name), Background = Resource<Brush>("Brush.Bg.Sunken"),
                Child = Label("Control.H." + name),
            }).ToArray()));

        foreach (var pair in resources.Where(pair => pair.Value is Style))
        {
            var key = pair.Key;
            var style = (Style)pair.Value;
            if (key.EndsWith(".Base", StringComparison.Ordinal) || key.EndsWith("Style", StringComparison.Ordinal)) continue;
            if (style.TargetType == typeof(Controls.Button))
                Add(actions, key, () => Buttons(key));
            else if (style.TargetType == typeof(Controls.RadioButton))
                Add(navigation, key, () => Tabs(key, key == "Tab.Connected"));
            else if (style.TargetType == typeof(Controls.Border))
            {
                var isKey = key.StartsWith("Overlay.PhysicalKey", StringComparison.Ordinal) || key == "Overlay.ModifierBadge";
                Add(isKey ? icons : surfaces, key, () => new Controls.Border
                {
                    Style = Resource<Style>(key), Child = isKey
                        ? new ControllerGlyphView { Glyph = "A", Width = 24, Height = 24 }
                        : Label("AgentController", "Font.Size.Body"),
                });
            }
            else if (style.TargetType == typeof(Controls.TextBox))
                Add(inputs, key, () => Column(
                    Input(key, key == "TextBox.Shortcut" ? "Ctrl+Shift+M" : "AgentController", true), Input(key, Text("禁用", "Disabled"), false),
                    Input(key, Text("只读", "Read only"), true, readOnly: true)));
            else if (style.TargetType == typeof(Controls.CheckBox))
                Add(inputs, key, () => Row(Check(key, false), Check(key, true), Check(key, true, enabled: false)));
            else if (style.TargetType == typeof(Controls.Slider))
                Add(inputs, key, () => Column(Slider(key, true), Slider(key, false)));
            else if (style.TargetType == typeof(Controls.ProgressBar))
                Add(feedback, key, () => Column(new[] { 0d, 40d, 100d }.Select(value =>
                    (FrameworkElement)new Controls.ProgressBar { Style = Resource<Style>(key),
                        Minimum = 0, Maximum = 100, Value = value }).ToArray()));
        }

        Add(inputs, "ComboBox", () => Column(Combo(true), Combo(false)));
        Add(lists, "ListBox.Sidebar · Sidebar.Entry", () => Sidebar(8));
        Add(lists, "ListBox.Sidebar · " + Text("空", "Empty"), () => Sidebar(0));
        Add(lists, "ScrollViewer.SectionTabs", () => Tabs("Tab.Connected", connected: true, overflow: true));
        Add(lists, "ScrollBar.Token · Vertical / Horizontal", () => new Controls.ScrollViewer
        {
            Height = 156, HorizontalScrollBarVisibility = Controls.ScrollBarVisibility.Auto,
            VerticalScrollBarVisibility = Controls.ScrollBarVisibility.Auto,
            Content = new Controls.Border { Width = 720, Height = 320, Padding = new Thickness(16),
                Background = Resource<Brush>("Brush.Bg.Panel"), Child = Column(
                    Enumerable.Range(1, 12).Select(index => (FrameworkElement)Label($"{index:00} · AgentController")).ToArray()) },
        });
        Add(feedback, "Event.Row · Text.EventLine", () => new Controls.ItemsControl
        {
            ItemTemplate = Resource<DataTemplate>("Event.Row"),
            ItemsSource = new[]
            {
                new { Time = "08:01:09", Text = Text("AgentController 已就绪", "AgentController ready") },
                new { Time = "18:41:40", Text = Text("切换到 DeepSeek Harness", "Switched to DeepSeek Harness") },
                new { Time = "23:59:59", Text = Text("已切换到公开创作项目 · AgentController · DeepSeek Harness · 组件与导航", "Switched to Public creations · AgentController · DeepSeek Harness · Components and navigation") },
            },
        });
        Add(feedback, "StatusDot · Brush.Status.*", () => Dots(resources, "Brush.Status."));
        Add(feedback, "StatusDot · Brush.Led.*", () => Dots(resources, "Brush.Led."));
        Add(feedback, "Badge.Mono", () => new Controls.TextBlock
        {
            Style = Resource<Style>("Badge.Mono"), Text = "18:41:40  0123456789  100%",
        });
        Add(feedback, "FullResetStatusView", () => new FullResetStatusView
        {
            Text = "100% · 02:30:00", ToolTipText = "FullResetStatusView",
        });
        Add(feedback, "ToolTip", () => new Controls.Button
        {
            Style = Resource<Style>("Button.Secondary"), Content = Text("提示", "Tooltip"),
            HorizontalAlignment = HorizontalAlignment.Left,
            ToolTip = Text("AgentController · 公开创作 · 组件库", "AgentController · Public creations · Components"),
        });
        foreach (var key in resources.Keys.Where(key => key.StartsWith("Duration.", StringComparison.Ordinal)))
            Add(feedback, key, () => Motion(key));
        foreach (var key in resources.Keys.Where(key => key.StartsWith("Shadow.", StringComparison.Ordinal)))
            Add(surfaces, key, () => new Controls.Border
            {
                Margin = new Thickness(20), Height = 64, Background = Resource<Brush>("Brush.Bg.Surface"),
                CornerRadius = Resource<CornerRadius>("Radius.MD"), Effect = Resource<Effect>(key).Clone(),
            });
        foreach (var key in new[] { "Logo.Codex", "Logo.DeepSeekHarness" })
            Add(icons, key, () => Row(new[] { 16d, 24d, 32d }.Select(size =>
                (FrameworkElement)new Controls.Image { Width = size, Height = size,
                    Source = Resource<ImageSource>(key), Stretch = Stretch.Uniform }).ToArray()));
        Add(icons, "ControllerGlyphView", () => Row(new[] { "A", "B", "X", "Y", "LB", "RB", "LT", "RT", "↑", "→" }
            .Select(glyph => (FrameworkElement)new ControllerGlyphView { Glyph = glyph, Width = 32, Height = 32,
                Foreground = Resource<Brush>("Brush.Text.Accent") }).ToArray()));
        Add(icons, "Sidebar.ScopeGlyph", () => Row(new[] { "PinnedTasks", "PinnedProjects", "Projects", "ProjectlessTasks" }
            .Select(tag => (FrameworkElement)new Controls.ContentControl { Style = Resource<Style>("Sidebar.ScopeGlyph"),
                Tag = tag, Foreground = Resource<Brush>("Brush.Text.Accent"), ToolTip = tag }).ToArray()));
        Add(navigation, "ActionMenuView", () => Fit(new ActionMenuView { DataContext = MenuModel(RadialMenuLayerKind.Action) }));
        Add(navigation, "RadialMenuView", () => Fit(new RadialMenuView { DataContext = MenuModel(RadialMenuLayerKind.Command) }));
        Add(navigation, "SidebarNavigationMenuView", SidebarMenu);
        Add(icons, "AgentKeypadView", () => Fit(new AgentKeypadView { DataContext = MenuModel(RadialMenuLayerKind.Agent) }));
        foreach (var key in resources.Keys.Where(key => key.StartsWith("Icon.", StringComparison.Ordinal)))
            Add(icons, key, () => Row(new[] { 20d, 24d }.Select(size => (FrameworkElement)new Controls.ContentControl
            {
                Style = Resource<Style>("Icon"), Content = Resource<Geometry>(key), Width = size, Height = size,
                Foreground = Resource<Brush>("Brush.Text.Secondary"),
            }).ToArray()));
        Add(icons, "ControllerIconView", () => new ControllerIconView { Width = 32, Height = 32 });
        Add(icons, "ControllerTutorialView", () =>
        {
            var model = new ControllerTutorialViewModel();
            model.UpdateContext(LocalizationHost.Current.Strings, BuiltInControllerProfiles.Generic, "LS", "RS");
            var view = new ControllerTutorialView { Width = 680, Height = 500, DataContext = model };
            ContextHint.SetIsScope(view, true);
            var hint = new System.Windows.Data.MultiBinding { StringFormat = "{0}\n{1}" };
            hint.Bindings.Add(new System.Windows.Data.Binding(nameof(model.ModeTitle)));
            hint.Bindings.Add(new System.Windows.Data.Binding(nameof(model.ModeDescription)));
            view.SetBinding(ContextHint.DefaultTextProperty, hint);
            return Fit(view);
        });

        var order = new[] { foundations, actions, inputs, navigation, lists, feedback, surfaces, icons };
        return samples.OrderBy(sample => Array.IndexOf(order, sample.Category)).ToArray();
    }

    private static Dictionary<string, object> ThemeResources()
    {
        var result = new Dictionary<string, object>(StringComparer.Ordinal);
        void Read(ResourceDictionary dictionary)
        {
            foreach (var child in dictionary.MergedDictionaries) Read(child);
            foreach (var key in dictionary.Keys.OfType<string>().ToArray()) result[key] = dictionary[key];
        }
        Read(System.Windows.Application.Current.Resources);
        return result.OrderBy(pair => pair.Key, StringComparer.Ordinal).ToDictionary(pair => pair.Key, pair => pair.Value);
    }

    private static T Resource<T>(string key) => (T)System.Windows.Application.Current.FindResource(key);

    private static Controls.TextBlock Label(string text, string size = "Font.Size.Caption")
    {
        var label = new Controls.TextBlock { Text = text, TextWrapping = TextWrapping.Wrap,
            VerticalAlignment = VerticalAlignment.Center };
        label.SetResourceReference(Controls.TextBlock.FontSizeProperty, size);
        return label;
    }

    private static Controls.WrapPanel Row(params FrameworkElement[] children)
    {
        var row = new Controls.WrapPanel();
        foreach (var child in children)
        {
            child.Margin = new Thickness(0, 0, 12, 8);
            child.VerticalAlignment = VerticalAlignment.Center;
            row.Children.Add(child);
        }
        return row;
    }

    private static Controls.StackPanel Column(params FrameworkElement[] children)
    {
        var column = new Controls.StackPanel();
        foreach (var child in children)
        {
            child.Margin = new Thickness(0, 0, 0, 8);
            column.Children.Add(child);
        }
        return column;
    }

    private static FrameworkElement Swatches(IEnumerable<string> keys) => Row(keys.Select(key =>
    {
        var swatch = Column(new Controls.Border { Height = 40, Background = Resource<Brush>(key),
            BorderBrush = Resource<Brush>("Brush.Border.Subtle"), BorderThickness = new Thickness(1),
            CornerRadius = Resource<CornerRadius>("Radius.XS") }, Label(string.Join('.', key.Split('.').Skip(2))));
        swatch.Width = 100;
        swatch.ToolTip = key;
        return (FrameworkElement)swatch;
    }).ToArray());

    private static FrameworkElement Buttons(string key)
    {
        var compact = key.StartsWith("Shell.", StringComparison.Ordinal) || key.EndsWith(".Icon", StringComparison.Ordinal);
        return Row(new[] { true, false }.Select(enabled =>
        {
            var button = new Controls.Button { Style = Resource<Style>(key), IsEnabled = enabled, ToolTip = key };
            if (key == "Button.Icon") button.Content = new Controls.ContentControl
            {
                Style = Resource<Style>("Icon"), Content = Resource<Geometry>("Icon.External"),
            };
            else if (key != "Shell.HelpButton") button.Content = compact ? "↗" : enabled ? Text("按钮", "Button") : Text("禁用", "Disabled");
            AutomationProperties.SetName(button, key + (enabled ? "" : " · Disabled"));
            return (FrameworkElement)button;
        }).ToArray());
    }

    private static FrameworkElement Tabs(string key, bool connected, bool overflow = false)
    {
        var group = Guid.NewGuid().ToString("N");
        Controls.Panel tabs = key == "Tab.SidebarScope"
            ? new Controls.WrapPanel()
            : new Controls.StackPanel { Orientation = Controls.Orientation.Horizontal };
        var labels = overflow
            ? new[] { Text("置顶", "Pinned"), Text("待办", "Todo"), Text("公开纯AI创作", "Public AI creations"),
                Text("项目", "Projects"), Text("游戏", "Games"), Text("归档", "Archive") }
            : new[] { Text("默认", "Default"), Text("选中", "Selected"), Text("禁用", "Disabled") };
        for (var index = 0; index < labels.Length; index++)
        {
            var tab = new Controls.RadioButton { Style = Resource<Style>(key), GroupName = group,
                IsChecked = index == 1, IsEnabled = index != labels.Length - 1, ToolTip = labels[index] };
            tab.Content = key == "Shell.NavIcon" ? new Controls.ContentControl
            {
                Style = Resource<Style>("Sidebar.ScopeGlyph"), Tag = "Projects",
            } : labels[index];
            AutomationProperties.SetName(tab, labels[index]);
            tabs.Children.Add(tab);
        }
        if (key == "Tab.SidebarScope") return new Controls.ScrollViewer { Content = tabs, MaxHeight = 128,
            HorizontalScrollBarVisibility = Controls.ScrollBarVisibility.Disabled,
            VerticalScrollBarVisibility = Controls.ScrollBarVisibility.Auto };
        if (!connected) return new Controls.ScrollViewer { Content = tabs,
            HorizontalScrollBarVisibility = Controls.ScrollBarVisibility.Auto,
            VerticalScrollBarVisibility = Controls.ScrollBarVisibility.Disabled };
        var panel = new Controls.StackPanel();
        var tabStrip = new Controls.ScrollViewer { Style = Resource<Style>("ScrollViewer.SectionTabs"),
            Content = tabs, Margin = new Thickness(16, 0, 16, 0) };
        Controls.Panel.SetZIndex(tabStrip, 2);
        panel.Children.Add(tabStrip);
        panel.Children.Add(new Controls.Border { Style = Resource<Style>("Panel.ConnectedTab"), Height = 72 });
        return panel;
    }

    private static Controls.TextBox Input(string key, string text, bool enabled, bool readOnly = false) =>
        new() { Style = Resource<Style>(key), Text = text, IsEnabled = enabled, IsReadOnly = readOnly };

    private static Controls.CheckBox Check(string key, bool value, bool enabled = true) => new()
    {
        Style = Resource<Style>(key), IsChecked = value, IsEnabled = enabled,
        Content = key == "Shell.Toggle" ? null : Text(value ? "选中" : "默认", value ? "Checked" : "Default"),
        ToolTip = key,
    };

    private static Controls.Slider Slider(string key, bool enabled) => new()
    {
        Style = Resource<Style>(key), Minimum = 0, Maximum = 100, Value = 40, TickFrequency = 10, IsEnabled = enabled,
    };

    private static Controls.ComboBox Combo(bool enabled) => new()
    {
        ItemsSource = new[] { "Codex", "DeepSeek Harness" }, SelectedIndex = 0, IsEnabled = enabled, MinHeight = 32,
    };

    private static Controls.ListBox Sidebar(int count) => new()
    {
        Style = Resource<Style>("ListBox.Sidebar"), ItemTemplate = Resource<DataTemplate>("Sidebar.Entry"),
        Height = count == 0 ? 96 : 220, SelectedIndex = count > 1 ? 1 : -1,
        ItemsSource = Enumerable.Range(0, count).Select(index => new SidebarEntry(
            $"sample-{index}", index == 0 ? "AgentController" : index == 2
                ? Text("一个需要截断显示的较长任务标题 · AgentController", "A long task title that needs truncation · AgentController")
                : Text($"任务 {index:00}", $"Task {index:00}"),
            index == 0 ? Text("8 个任务", "8 tasks") : Text("1 分钟前", "1 minute ago"),
            index == 0 ? SidebarLayer.Projects : SidebarLayer.Tasks,
            IsPinned: index == 1, PinBadge: Text("置顶", "Pinned"), ActionHint: Text("打开", "Open"))).ToArray(),
    };

    private static FrameworkElement Dots(Dictionary<string, object> resources, string prefix) => Row(
        resources.Keys.Where(key => key.StartsWith(prefix, StringComparison.Ordinal) && !key.EndsWith(".Halo", StringComparison.Ordinal))
            .Select(key => (FrameworkElement)Column(new System.Windows.Shapes.Ellipse
            {
                Style = Resource<Style>("StatusDot"), Fill = Resource<Brush>(key), HorizontalAlignment = HorizontalAlignment.Left,
            }, Label(key[prefix.Length..]))).ToArray());

    private static FrameworkElement Motion(string key)
    {
        var button = new Controls.Button { Style = Resource<Style>("Button.Secondary"), Content = Text("播放", "Play") };
        var target = new Controls.Border { Width = 32, Height = 32, CornerRadius = Resource<CornerRadius>("Radius.SM"),
            Background = Resource<Brush>("Brush.Accent"), RenderTransform = new TranslateTransform() };
        button.Click += (_, _) =>
        {
            if (!SystemParameters.ClientAreaAnimation) return;
            target.RenderTransform.BeginAnimation(TranslateTransform.XProperty, new DoubleAnimation(0, 60, Resource<Duration>(key))
            {
                AutoReverse = true, EasingFunction = (CubicEase)Resource<CubicEase>("Ease.Out").Clone(),
            });
        };
        return Row(button, target);
    }

    private static Controls.Viewbox Fit(FrameworkElement content) =>
        new() { Stretch = Stretch.Uniform, StretchDirection = Controls.StretchDirection.DownOnly, Child = content };

    private static RadialMenuViewModel MenuModel(RadialMenuLayerKind layer)
    {
        var model = new RadialMenuViewModel();
        var positions = Enum.GetValues<RadialMenuSlotPosition>();
        var titles = new[] { Text("新建任务", "New task"), Text("打开项目", "Open project"),
            Text("继续", "Continue"), Text("取消", "Cancel"), Text("上一项", "Previous"), Text("下一项", "Next") };
        var glyphs = new[] { "Y", "B", "A", "X", "LB", "RB" };
        var states = new[] { ThreadStatus.Idle, ThreadStatus.Thinking, ThreadStatus.CompleteUnread,
            ThreadStatus.RequiresInput, ThreadStatus.Error, ThreadStatus.Unassigned };
        model.Update(new RadialMenuState(layer, layer.ToString(), "Y",
            positions.Select((position, index) => new RadialMenuItemState($"sample-{index}", position,
                glyphs[index], titles[index], isEnabled: index < 5, isHighlighted: index == 2,
                confirmationProgress: index == 2 ? 0.6 : 0, status: states[index])),
            displayMode: RadialMenuDisplayMode.Always,
            agentKeypad: new AgentKeypadPresentation(string.Empty, "Y", Text("返回", "Back"), string.Empty,
                Text("空闲", "Idle"), Text("运行中", "Working"), Text("完成", "Complete"),
                Text("待输入", "Input"), Text("错误", "Error"), Text("未分配", "Unassigned"))));
        return model;
    }

    private static FrameworkElement SidebarMenu()
    {
        var model = new SidebarNavigationMenuViewModel();
        var project = new SidebarNavigationMenuItem("project", "AgentController", "8", SidebarScope.Projects,
            Text("项目", "Projects"), SidebarNavigationMenuItemKind.Project, true);
        var task = new SidebarNavigationMenuItem("task", Text("组件库", "Components"), "1m", SidebarScope.ProjectTasks,
            "AgentController", SidebarNavigationMenuItemKind.Task, true, true);
        model.Update(new SidebarNavigationMenuState(
            new SidebarNavigationMenuPanel(Text("项目", "Projects"),
                [new(SidebarScope.Projects, Text("项目", "Projects"), [project])], false),
            new SidebarNavigationMenuPanel("AgentController",
                [new(SidebarScope.ProjectTasks, "AgentController", [task])], true))
        {
            Title = Text("侧边栏", "Sidebar"), NavigateHint = Text("移动", "Move"),
            CycleScopeHint = Text("分区", "Section"), OpenHint = Text("打开", "Open"),
        });
        var view = new SidebarNavigationMenuView { DataContext = model };
        view.RequestBringIntoView += (_, e) => e.Handled = true;
        return Fit(view);
    }
}
