using AgentController.Application.Actions;
using AgentController.Domain.Actions;
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
    public void SelectionCyclesRegisteredTargetsInStableOrder()
    {
        var codex = new TestAgentTarget("codex", "Codex");
        var deepSeek = new TestAgentTarget(
            "deepseek-harness",
            "DeepSeek Harness");
        var selection = new AgentTargetSelection(
            new AgentTargetRegistry([codex, deepSeek], codex.Id),
            "codex");
        AgentTargetChangedEventArgs? changed = null;
        selection.Changed += (_, value) => changed = value;

        Assert.True(selection.SelectNext());
        Assert.Same(deepSeek, selection.Active);
        Assert.Same(codex, changed?.Previous);
        Assert.Same(deepSeek, changed?.Current);

        Assert.True(selection.SelectNext());
        Assert.Same(codex, selection.Active);
    }

    [Fact]
    public async Task ScopedExecutorCannotLeakIntoUnselectedAgent()
    {
        var codex = new TestAgentTarget("codex", "Codex");
        var deepSeek = new TestAgentTarget(
            "deepseek-harness",
            "DeepSeek Harness");
        var selection = new AgentTargetSelection(
            new AgentTargetRegistry([codex, deepSeek], codex.Id),
            "deepseek-harness");
        var inner = new TestActionExecutor();
        var scoped = new AgentScopedActionExecutor(
            inner,
            selection,
            codex.Id);
        var request = new ActionRequest(
            Guid.NewGuid(),
            ComposerActionContract.SubmitId,
            new ActionSource(
                "controller",
                AgentController.Domain.Inputs.ControlId.Parse("face.west")),
            AgentController.Domain.Inputs.InputContext.Parse("composer.input"),
            "test:submit",
            ActionSafetyLevel.Routine,
            DateTimeOffset.UtcNow);

        var capability = await scoped.ProbeAsync(request);
        var result = await scoped.ExecuteAsync(request);

        Assert.Equal(
            ExecutorCapabilityStatus.Unsupported,
            capability.Status);
        Assert.Equal(ActionOutcome.Unsupported, result.Outcome);
        Assert.Equal(0, inner.ExecutionCount);
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

    private sealed class TestActionExecutor : IActionExecutor
    {
        public string Id => "test.executor";
        public int ExecutionCount { get; private set; }

        public ValueTask<ExecutorCapability> ProbeAsync(
            ActionRequest request,
            CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(new ExecutorCapability(
                Id,
                request.ActionId,
                ExecutorCapabilityStatus.Available,
                Priority: 100));

        public ValueTask<ActionResult> ExecuteAsync(
            ActionRequest request,
            CancellationToken cancellationToken = default)
        {
            ExecutionCount++;
            return ValueTask.FromResult(new ActionResult(
                request.RequestId,
                request.ActionId,
                ActionOutcome.Succeeded,
                Id,
                DateTimeOffset.UtcNow));
        }
    }
}
