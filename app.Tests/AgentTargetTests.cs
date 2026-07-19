using CodexController.Agents;
using CodexController.Agents.Codex;
using CodexController.Models;
using CodexController.Services;

namespace CodexController.Tests;

public sealed class AgentTargetTests
{
    [Theory]
    [InlineData("codex")]
    [InlineData("claude-code")]
    [InlineData("agent2")]
    public void AgentIdAcceptsStableLowercaseSlugs(string value)
    {
        var id = new AgentId(value);

        Assert.Equal(value, id.Value);
        Assert.Equal(value, id.ToString());
    }

    [Theory]
    [InlineData("")]
    [InlineData("Codex")]
    [InlineData("-codex")]
    [InlineData("codex-")]
    [InlineData("claude--code")]
    [InlineData("codex.desktop")]
    public void AgentIdRejectsUnstableValues(string value)
    {
        Assert.Throws<ArgumentException>(() => new AgentId(value));
    }

    [Fact]
    public void CodexTargetAlwaysProvidesRequiredCapabilities()
    {
        var target = new CodexAgentTarget(new CodexCommandService());

        Assert.Equal(new AgentId("codex"), target.Id);
        Assert.Equal("Codex", target.DisplayName);
        Assert.NotNull(target.Presence);
        Assert.NotNull(target.Shortcuts);
        Assert.NotNull(target.DeepLinks);
        Assert.Equal(
            AgentCapabilities.Presence |
            AgentCapabilities.Shortcuts |
            AgentCapabilities.DeepLinks,
            target.Capabilities);
    }

    [Fact]
    public void CodexTargetLeavesUnavailableCapabilitiesNull()
    {
        var target = new CodexAgentTarget(new CodexCommandService());

        Assert.Null(target.Workspace);
        Assert.Null(target.Sidebar);
        Assert.Null(target.Composer);
        Assert.Null(target.Keybindings);
        Assert.False(
            target.Capabilities.HasFlag(AgentCapabilities.Workspace));
        Assert.False(
            target.Capabilities.HasFlag(AgentCapabilities.Sidebar));
        Assert.False(
            target.Capabilities.HasFlag(AgentCapabilities.Composer));
        Assert.False(
            target.Capabilities.HasFlag(AgentCapabilities.Keybindings));
    }

    [Fact]
    public void DefaultCodexTargetAdvertisesItsCompleteSurface()
    {
        var target = CodexAgentTarget.CreateDefault();
        var expected =
            AgentCapabilities.Presence |
            AgentCapabilities.Shortcuts |
            AgentCapabilities.Workspace |
            AgentCapabilities.Sidebar |
            AgentCapabilities.Composer |
            AgentCapabilities.DeepLinks |
            AgentCapabilities.Keybindings;

        Assert.Equal(expected, target.Capabilities);
        Assert.NotNull(target.Workspace);
        Assert.NotNull(target.Sidebar);
        Assert.NotNull(target.Composer);
        Assert.NotNull(target.Keybindings);
    }

    [Fact]
    public void ShortcutAdapterPreservesBridgeSafetyGate()
    {
        var target = new CodexAgentTarget(new CodexCommandService());

        Assert.False(target.Shortcuts.CanExecute(new()
        {
            BridgeEnabled = false,
        }));
    }

    [Fact]
    public void RegistryResolvesPersistedTargetAndFallsBackSafely()
    {
        var codex = new TestAgentTarget("codex", "Codex");
        var studio = new TestAgentTarget(
            "studio-agent",
            "Studio Agent");
        var registry = new AgentTargetRegistry(
            [codex, studio],
            codex.Id);

        Assert.Same(studio, registry.Resolve("studio-agent"));
        Assert.Same(codex, registry.Resolve("missing-agent"));
        Assert.Same(codex, registry.Resolve("Invalid ID"));
    }

    [Fact]
    public async Task MissingOptionalCapabilitiesDegradeWithoutThrowing()
    {
        var target = new TestAgentTarget("shortcut-agent", "Shortcut");
        var snapshot = target.WorkspaceOrEmpty().LoadSnapshot();
        var sidebar = target.SidebarOrUnavailable().RestoreDisclosure(
            new ProjectDisclosureLease("Project", projectIsPinned: false));
        var composer = await target
            .ComposerOrUnavailable()
            .SelectAsync(
                ComposerSettingKind.Model,
                "model",
                new(),
                CancellationToken.None);
        var planToggle = await target
            .ComposerOrUnavailable()
            .TogglePlanModeAsync(
                "F19",
                new(),
                CancellationToken.None);
        var picker = await target
            .ComposerOrUnavailable()
            .OpenPickerAsync(
                ComposerPickerView.Simple,
                new(),
                CancellationToken.None);
        var power = await target
            .ComposerOrUnavailable()
            .StepSimplePowerAsync(
                1,
                allowShortcutFastPath: false,
                new(),
                CancellationToken.None);
        var speed = await target
            .ComposerOrUnavailable()
            .SetSimpleSpeedAsync(
                true,
                allowShortcutFastPath: false,
                new(),
                CancellationToken.None);
        var advanced = await target
            .ComposerOrUnavailable()
            .StepAdvancedAsync(
                ComposerSettingKind.Effort,
                1,
                new(),
                CancellationToken.None);
        var dial = target
            .ComposerOrUnavailable()
            .DialStep(1, new());
        var dialProbe = target
            .ComposerOrUnavailable()
            .ProbeDialState();

        Assert.Empty(snapshot.Threads);
        Assert.False(sidebar.Succeeded);
        Assert.Equal(
            AgentCapabilityFallbacks.CapabilityUnavailable,
            sidebar.Error);
        Assert.False(composer.Succeeded);
        Assert.Equal(
            AgentCapabilityFallbacks.CapabilityUnavailable,
            composer.Error);
        Assert.False(planToggle.Succeeded);
        Assert.Equal(
            AgentCapabilityFallbacks.CapabilityUnavailable,
            planToggle.Error);
        Assert.All(
            new[] { picker, power, speed, advanced },
            result =>
            {
                Assert.False(result.Succeeded);
                Assert.Equal(
                    AgentCapabilityFallbacks.CapabilityUnavailable,
                    result.Error);
            });
        Assert.False(dial.Succeeded);
        Assert.Equal(
            AgentCapabilityFallbacks.CapabilityUnavailable,
            dial.Error);
        Assert.False(dialProbe.Succeeded);
        Assert.Equal(
            AgentCapabilityFallbacks.CapabilityUnavailable,
            dialProbe.Error);
    }

    private sealed class TestAgentTarget : IAgentTarget
    {
        public TestAgentTarget(string id, string displayName)
        {
            Id = new AgentId(id);
            DisplayName = displayName;
        }

        public AgentId Id { get; }
        public string DisplayName { get; }
        public AgentCapabilities Capabilities =>
            AgentCapabilities.Presence |
            AgentCapabilities.Shortcuts;
        public IAgentPresence Presence { get; } =
            new TestPresence();
        public IAgentShortcuts Shortcuts { get; } =
            new TestShortcuts();
        public IWorkspaceReader? Workspace => null;
        public ISidebarAutomation? Sidebar => null;
        public IComposerAutomation? Composer => null;
        public IDeepLinks? DeepLinks => null;
        public IKeybindingProvisioner? Keybindings => null;
    }

    private sealed class TestPresence : IAgentPresence
    {
        public bool IsForeground => false;
        public bool Wake() => false;
    }

    private sealed class TestShortcuts : IAgentShortcuts
    {
        public bool CanExecute(AppSettings settings) => false;

        public bool Execute(
            string shortcut,
            AppSettings settings) =>
            false;

        public Task<bool> StepModelAsync(
            int steps,
            AppSettings settings,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(false);
    }
}
