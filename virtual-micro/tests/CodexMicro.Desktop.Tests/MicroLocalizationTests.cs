using System.Globalization;
using System.Text.RegularExpressions;
using System.Windows.Controls;
using CodexMicro.Desktop;
using CodexMicro.Desktop.Services;
using Xunit;

namespace CodexMicro.Desktop.Tests;

[Collection(WpfUiCollection.Name)]
public sealed class MicroLocalizationTests
{
    [Fact]
    public void AutoPrefersAgentControllerThenFallsBackToWindows()
    {
        var sharedLanguage = MicroLanguage.ZhCn;
        var localization = new MicroLocalization(
            MicroLanguage.Auto,
            () => sharedLanguage,
            () => CultureInfo.GetCultureInfo("en-US"));

        Assert.Equal(MicroLanguage.ZhCn, localization.EffectiveLanguage);

        sharedLanguage = MicroLanguage.EnUs;
        localization.RefreshAutoLanguage();
        Assert.Equal(MicroLanguage.EnUs, localization.EffectiveLanguage);

        var fallback = new MicroLocalization(
            MicroLanguage.Auto,
            () => null,
            () => CultureInfo.GetCultureInfo("zh-CN"));
        Assert.Equal(MicroLanguage.ZhCn, fallback.EffectiveLanguage);
    }

    [Fact]
    public void WindowLanguageSwitchUpdatesMenusAndHelpImmediately()
    {
        Exception? error = null;
        var thread = new Thread(() =>
        {
            try
            {
                var localization = new MicroLocalization(
                    MicroLanguage.ZhCn,
                    systemCulture: () => CultureInfo.GetCultureInfo("zh-CN"));
                var window = new MicroSurfaceWindow(localization);
                Assert.Equal("窗口置顶", window.TopmostMenuItem.Header);

                localization.SetLanguage(MicroLanguage.EnUs);

                Assert.Equal("Always on top", window.TopmostMenuItem.Header);
                Assert.Equal("Reconnect virtual HID", window.ReconnectMenuItem.Header);
                Assert.Equal("Hide panel", window.HidePanelMenuItem.Header);
                var tooltip = Assert.IsType<ToolTip>(window.ActivityLed.ToolTip);
                var panel = Assert.IsType<StackPanel>(tooltip.Content);
                Assert.Equal(
                    "Latest event",
                    Assert.IsType<TextBlock>(panel.Children[0]).Text);
                Assert.Equal(
                    "No event has been sent.",
                    Assert.IsType<TextBlock>(panel.Children[1]).Text);
                window.CloseForApplicationExit();
            }
            catch (Exception exception)
            {
                error = exception;
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();

        Assert.Null(error);
    }

    [Fact]
    public void EnglishCatalogCoversPresentationStringLiterals()
    {
        var localization = new MicroLocalization(MicroLanguage.EnUs);
        var unresolved = new List<string>();
        foreach (var relativePath in new[]
                 {
                     "virtual-micro/src/CodexMicro.Desktop/MainWindow.xaml.cs",
                     "virtual-micro/src/CodexMicro.Desktop/Services/AgentLightingAppearance.cs",
                     "virtual-micro/src/CodexMicro.Desktop/Services/CodexMenuSelectionObserver.cs",
                 })
        {
            var source = File.ReadAllText(Path.Combine(
                FindRepositoryRoot(),
                relativePath.Replace('/', Path.DirectorySeparatorChar)));
            foreach (Match match in Regex.Matches(
                         source,
                         "\\\"(?<text>(?:\\\\.|[^\\\"\\\\])*)\\\""))
            {
                var literal = match.Groups["text"].Value;
                if (!ContainsChinese(literal))
                {
                    continue;
                }

                var text = literal
                    .Replace("\\n", "\n", StringComparison.Ordinal)
                    .Replace("\\\"", "\"", StringComparison.Ordinal)
                    .Replace("\\\\", "\\", StringComparison.Ordinal);
                if (ContainsChinese(localization.Text(text)))
                {
                    unresolved.Add(text);
                }
            }
        }

        Assert.True(
            unresolved.Count == 0,
            "Missing English translations:\n" +
            string.Join("\n", unresolved.Distinct()));
    }

    private static bool ContainsChinese(string value) =>
        Regex.IsMatch(value, "[一-龥]");

    private static string FindRepositoryRoot()
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current is not null)
        {
            if (File.Exists(Path.Combine(current.FullName, "AgentController.sln")))
            {
                return current.FullName;
            }

            current = current.Parent;
        }

        throw new InvalidOperationException("Repository root not found.");
    }
}
