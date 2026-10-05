using System.Collections.ObjectModel;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Markup;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Xml.Linq;
using CodexController;
using CodexController.Controllers;
using CodexController.Core.Bridge;
using CodexController.Localization;
using CodexController.Models;
using CodexController.Presentation.Feedback;
using CodexController.ViewModels;
using CodexController.Views;

// Offline listing export: product XAML and explicit sample data, no application
// composition, account reads, controller polling, visible windows or input injection.
internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        if (args.Length != 2 || args.Any(p => p.Contains("trash", StringComparison.OrdinalIgnoreCase))) return 2;
        var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        var exit = 0;
        app.Dispatcher.BeginInvoke(new Action(async () =>
        {
            try
            {
                var root = Path.GetFullPath(args[0]);
                var output = Path.GetFullPath(args[1]);
                var appSource = XDocument.Parse(await File.ReadAllTextAsync(Path.Combine(root, "app/App.xaml")));
                var dictionary = appSource.Descendants().First(e => e.Name.LocalName == "ResourceDictionary");
                foreach (var source in dictionary.Descendants().Attributes("Source"))
                    app.Resources.MergedDictionaries.Add(new ResourceDictionary
                    {
                        Source = new Uri("pack://application:,,,/AgentController;component/" + source.Value)
                    });
                var xaml = await File.ReadAllTextAsync(Path.Combine(root, "app/MainWindow.xaml"));
                foreach (var language in new[] { AppLanguage.EnUs, AppLanguage.ZhCn })
                {
                    var localization = new LocalizationService(language);
                    LocalizationHost.Use(localization);
                    var locale = localization.Culture.Name;
                    var directory = Path.Combine(output, locale);
                    await Task.Run(() => Directory.CreateDirectory(directory));
                    foreach (var page in new[] { "device", "actions", "settings" })
                    {
                        var window = LoadShell(xaml);
                        var device = (DevicePageView)window.FindName("DevicePage");
                        var settings = (SettingsPageView)window.FindName("SettingsPage");
                        ((ConfigPageView)window.FindName("ConfigPage")).AgentLogo =
                            (ImageSource)app.FindResource("Logo.Codex");
                        ((TextBlock)window.FindName("FooterVersionText")).Text = typeof(App).Assembly.GetName().Version?.ToString(3);
                        var strings = localization.Strings;
                        (string Title, int TaskCount)[] projects =
                        [
                            ("Desktop app", 8),
                            ("Website", 5),
                            ("Documentation", 3),
                            ("CLI tools", 4),
                            ("Design system", 6)
                        ];
                        var entries = new ObservableCollection<SidebarEntry>(
                            projects.Select((project, index) => new SidebarEntry(
                                $"example-project-{index}", project.Title,
                                strings.SidebarProjectTaskCount(project.TaskCount),
                                SidebarLayer.Projects, SectionId: "threads")));
                        var formatter = new LocalizedBridgeFeedbackFormatter(strings);
                        var timestamp = new DateTimeOffset(2026, 10, 4, 9, 30, 0, TimeSpan.FromHours(-7));
                        BridgeEvent[] events =
                        [
                            new(BridgeEventKeys.SidebarEntryOpened, timestamp,
                                BridgeEventSeverity.Success, new Dictionary<string, string> { ["label"] = projects[0].Title }),
                            new(BridgeEventKeys.SidebarScopeChanged, timestamp.AddSeconds(-12),
                                BridgeEventSeverity.Info, new Dictionary<string, string> { ["scope"] = nameof(SidebarScope.Projects) }),
                            new(BridgeEventKeys.ControllerArmed, timestamp.AddSeconds(-30),
                                BridgeEventSeverity.Success)
                        ];
                        var recentEvents = new ObservableCollection<BridgeFeedbackLogRow>(
                            events.Select(item => new BridgeFeedbackLogRow(item, formatter.Format(item).LogText)));
                        var model = new DevicePageViewModel(entries,
                            new ReadOnlyObservableCollection<BridgeFeedbackLogRow>(recentEvents), () => { }, _ => { });
                        model.UpdateContext(strings, "Codex", BuiltInControllerProfiles.Xbox);
                        model.UpdateAgentStatus(strings.Get(StringKeys.StatusControlActive), isActive: true);
                        model.UpdateSidebarSections(
                            [
                                new("example-pinned", "Desktop app", "", SidebarLayer.Pinned,
                                    NavigationScope: SidebarScope.PinnedTasks, SectionId: "pinned"),
                                entries[0],
                                new("example-chat", "Scratchpad", "", SidebarLayer.Tasks,
                                    NavigationScope: SidebarScope.ProjectlessTasks, SectionId: "chats")
                            ], null, entries[0].Id);
                        var state = new ControllerState(true, 0, 1, "XInput", ControllerButtons.None, 0, 0, 0, 0, 0, 0);
                        model.UpdateControllerState(state);
                        if (page == "actions") model.Tutorial.SelectActionCommand.Execute(null);
                        device.DataContext = model;
                        device.RenderControllerState(state, 0.58);
                        device.SelectSidebarIndex(0);
                        var preferences = new SettingsPageViewModel(() => { }, () => { }, () => { }, _ => { });
                        preferences.UpdateContext(strings, "Codex", "Menu", null);
                        preferences.Load(new AppSettings());
                        settings.Strings = strings;
                        settings.Localization = localization;
                        settings.DataContext = preferences;
                        if (page == "settings")
                        {
                            device.Visibility = Visibility.Collapsed;
                            settings.Visibility = Visibility.Visible;
                            ((RadioButton)window.FindName("SettingsNavButton")).IsChecked = true;
                        }
                        var surface = (FrameworkElement)window.Content;
                        var names = NameScope.GetNameScope(window);
                        window.Content = null;
                        // Keep ElementName bindings intact when exporting the window content.
                        NameScope.SetNameScope(surface, names);
                        surface.Width = 1220;
                        surface.Height = 800;
                        var canvas = new Grid { Width = 1920, Height = 1080, Background = new SolidColorBrush(Color.FromRgb(238, 241, 239)) };
                        canvas.Children.Add(new Viewbox { Width = 1560, Height = 1023, Child = surface, Stretch = Stretch.Uniform });
                        canvas.Measure(new Size(1920, 1080));
                        canvas.Arrange(new Rect(0, 0, 1920, 1080));
                        canvas.UpdateLayout();
                        await app.Dispatcher.InvokeAsync(() => { }, System.Windows.Threading.DispatcherPriority.Render);
                        var bitmap = new RenderTargetBitmap(1920, 1080, 96, 96, PixelFormats.Pbgra32);
                        bitmap.Render(canvas);
                        bitmap.Freeze();
                        await Task.Run(async () =>
                        {
                            var encoder = new PngBitmapEncoder();
                            encoder.Frames.Add(BitmapFrame.Create(bitmap));
                            using var buffer = new MemoryStream();
                            encoder.Save(buffer);
                            await File.WriteAllBytesAsync(Path.Combine(directory, page + ".png"), buffer.ToArray());
                        });
                        Console.WriteLine($"{locale}/{page}.png");
                    }
                }
            }
            catch (Exception error) { Console.Error.WriteLine(error); exit = 1; }
            finally { app.Shutdown(exit); }
        }));
        app.Run();
        return exit;
    }

    private static Window LoadShell(string source)
    {
        var root = XDocument.Parse(source).Root!;
        XNamespace x = "http://schemas.microsoft.com/winfx/2006/xaml";
        root.Attribute(x + "Class")?.Remove();
        string[] events = ["Loaded", "Closing", "StateChanged", "MouseLeftButtonDown", "Click", "Checked", "Unchecked", "SidebarSelectionChanged", "SidebarMouseDoubleClick", "SidebarPreviewKeyDown", "AgentTargetRequested"];
        foreach (var attribute in root.DescendantsAndSelf().Attributes().ToArray())
        {
            if (events.Contains(attribute.Name.LocalName)) attribute.Remove();
            else if (attribute.IsNamespaceDeclaration && attribute.Value.StartsWith("clr-namespace:") && !attribute.Value.Contains(";assembly="))
                attribute.Value += ";assembly=AgentController";
        }
        foreach (var element in root.DescendantsAndSelf())
            if (element.Name.NamespaceName.StartsWith("clr-namespace:") && !element.Name.NamespaceName.Contains(";assembly="))
                element.Name = XName.Get(element.Name.LocalName, element.Name.NamespaceName + ";assembly=AgentController");
        root.Attribute("Icon")?.Remove();
        return (Window)XamlReader.Parse(root.ToString());
    }
}
