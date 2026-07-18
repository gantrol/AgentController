using CodexController.Agents;
using CodexController.Localization;
using CodexController.Models;
using CodexController.Services;
using CodexController.Services.Micro;

namespace CodexController.Tests;

public sealed class AgentAutomationErrorTests
{
    private static readonly object EnvironmentLock = new();

    [Fact]
    public void ComposerAndSidebarSafetyGatesReturnStableCodes()
    {
        var settings = new AppSettings
        {
            BridgeEnabled = false,
        };

        var composer = new CodexComposerService()
            .SubmitComposer(settings);
        var stop = new CodexComposerService()
            .StopCurrentTurn(settings);
        var sidebar = new CodexSidebarService()
            .GoBack(settings);

        Assert.Equal(
            AgentAutomationErrorCodes.BridgeSafePreview,
            composer.Error);
        Assert.Null(composer.ErrorDetail);
        Assert.Equal(
            new AgentAutomationError(
                AgentAutomationErrorCodes.BridgeSafePreview),
            composer.Failure);
        Assert.Equal(
            AgentAutomationErrorCodes.BridgeSafePreview,
            stop.Error);

        Assert.Equal(
            AgentAutomationErrorCodes.BridgeSafePreview,
            sidebar.Error);
        Assert.Null(sidebar.ErrorDetail);
        Assert.Equal(
            new AgentAutomationError(
                AgentAutomationErrorCodes.BridgeSafePreview),
            sidebar.Failure);
    }

    [Fact]
    public void ComposerSubmitUsesNativeCtrlEnter()
    {
        string? sentShortcut = null;
        var service = new CodexComposerService(
            MicroInputService.Unavailable,
            shortcut =>
            {
                sentShortcut = shortcut;
                return true;
            });
        var settings = new AppSettings
        {
            BridgeEnabled = true,
            OnlyWhenCodexForeground = false,
        };

        var result = service.SubmitComposer(settings);

        Assert.True(result.Succeeded);
        Assert.Equal("Ctrl+Enter", sentShortcut);
        Assert.Equal(
            ComposerAutomationChannel.KeyboardInput,
            result.Channel);
        Assert.False(result.StateVerified);
    }

    [Fact]
    public void ComposerSubmitReportsNativeInjectionFailure()
    {
        var service = new CodexComposerService(
            MicroInputService.Unavailable,
            _ => false);
        var settings = new AppSettings
        {
            BridgeEnabled = true,
            OnlyWhenCodexForeground = false,
        };

        var result = service.SubmitComposer(settings);

        Assert.False(result.Succeeded);
        Assert.Equal(
            AgentAutomationErrorCodes.InputInjectionFailed,
            result.Error);
        Assert.Equal("Ctrl+Enter", result.ErrorDetail);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ComposerUiAutomationSuccessReportsItsEvidence(
        bool stateVerified)
    {
        var result =
            CodexComposerService.UiAutomationSucceeded(stateVerified);

        Assert.True(result.Succeeded);
        Assert.Equal(
            ComposerAutomationChannel.UiAutomation,
            result.Channel);
        Assert.Equal(stateVerified, result.StateVerified);
    }

    [Fact]
    public void InvalidKeybindingsReturnCodeAndMachineReadableDetail()
    {
        lock (EnvironmentLock)
        {
            var originalHome =
                Environment.GetEnvironmentVariable("CODEX_HOME");
            var temporaryHome = Path.Combine(
                Path.GetTempPath(),
                $"agent-controller-tests-{Guid.NewGuid():N}");

            try
            {
                Directory.CreateDirectory(temporaryHome);
                File.WriteAllText(
                    Path.Combine(temporaryHome, "keybindings.json"),
                    """{"command":"composer.submit","key":"F22"}""");
                Environment.SetEnvironmentVariable(
                    "CODEX_HOME",
                    temporaryHome);

                var result = new CodexKeybindingService()
                    .EnsureBridgeBindings(new AppSettings());

                Assert.False(result.Succeeded);
                Assert.Equal(
                    AgentAutomationErrorCodes.KeybindingsInvalid,
                    result.Error);
                Assert.Equal("root-not-array", result.ErrorDetail);
                Assert.Equal(
                    new AgentAutomationError(
                        AgentAutomationErrorCodes.KeybindingsInvalid,
                        "root-not-array"),
                    result.Failure);
            }
            finally
            {
                Environment.SetEnvironmentVariable(
                    "CODEX_HOME",
                    originalHome);
                if (Directory.Exists(temporaryHome))
                {
                    Directory.Delete(temporaryHome, recursive: true);
                }
            }
        }
    }

    [Fact]
    public void KeybindingConflictsContainNoLocalizedSentence()
    {
        lock (EnvironmentLock)
        {
            var originalHome =
                Environment.GetEnvironmentVariable("CODEX_HOME");
            var temporaryHome = Path.Combine(
                Path.GetTempPath(),
                $"agent-controller-tests-{Guid.NewGuid():N}");

            try
            {
                Directory.CreateDirectory(temporaryHome);
                File.WriteAllText(
                    Path.Combine(temporaryHome, "keybindings.json"),
                    """
                    [
                      {
                        "command": "another.command",
                        "key": "F17"
                      }
                    ]
                    """);
                Environment.SetEnvironmentVariable(
                    "CODEX_HOME",
                    temporaryHome);

                var result = new CodexKeybindingService()
                    .EnsureBridgeBindings(new AppSettings());

                Assert.True(result.Succeeded);
                Assert.Contains(
                    "key=F17;command=another.command",
                    result.Conflicts);
            }
            finally
            {
                Environment.SetEnvironmentVariable(
                    "CODEX_HOME",
                    originalHome);
                if (Directory.Exists(temporaryHome))
                {
                    Directory.Delete(temporaryHome, recursive: true);
                }
            }
        }
    }

    [Fact]
    public void ErrorLabelsFollowRuntimeLanguageAndPreserveDetail()
    {
        var localization = new LocalizationService(AppLanguage.EnUs);
        var strings = localization.Strings;

        Assert.Equal(
            "The Agent window could not be found",
            strings.ErrorLabel(
                AgentAutomationErrorCodes.AgentWindowNotFound));
        Assert.Equal(
            "Key input failed: Enter",
            strings.ErrorLabel(
                AgentAutomationErrorCodes.InputInjectionFailed,
                "Enter"));

        localization.SetLanguage(AppLanguage.ZhCn);

        Assert.Equal(
            "找不到 Agent 窗口",
            strings.ErrorLabel(
                AgentAutomationErrorCodes.AgentWindowNotFound));
        Assert.Equal(
            "按键输入失败：Enter",
            strings.ErrorLabel(
                AgentAutomationErrorCodes.InputInjectionFailed,
                "Enter"));
        Assert.Equal(
            "操作失败：future-error",
            strings.ErrorLabel("future-error"));
    }
}
