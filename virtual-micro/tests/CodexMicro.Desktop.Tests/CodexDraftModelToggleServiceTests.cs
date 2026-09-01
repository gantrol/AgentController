using CodexMicro.Desktop.Services;
using System.Text.Json;
using Xunit;

namespace CodexMicro.Desktop.Tests;

public sealed class CodexDraftModelToggleServiceTests
{
    [Theory]
    [InlineData(null, false)]
    [InlineData("", false)]
    [InlineData("   ", false)]
    [InlineData("client-new-thread:", false)]
    [InlineData("client-new-thread:abc", true)]
    [InlineData(" client-new-thread:abc ", false)]
    [InlineData("CLIENT-NEW-THREAD:abc", false)]
    [InlineData("019f-real-thread", false)]
    public void OnlyAnExactRendererDraftUsesTheNativeBootstrap(
        string? threadId,
        bool expected) =>
        Assert.Equal(
            expected,
            CodexDraftModelToggleService.ShouldUseDraftFallback(threadId));

    [Theory]
    [InlineData("client-new-thread:one", "client-new-thread:one", true)]
    [InlineData("client-new-thread:one", "client-new-thread:two", false)]
    [InlineData("client-new-thread:one", "019f-real-thread", false)]
    [InlineData("client-new-thread:one", null, false)]
    [InlineData(null, "client-new-thread:one", false)]
    [InlineData("019f-real-thread", "019f-real-thread", false)]
    public void DraftGuardRequiresTheSameExactRendererIdentity(
        string? expectedDraftThreadId,
        string? currentVisibleThreadId,
        bool expected) =>
        Assert.Equal(
            expected,
            CodexDraftModelToggleService.IsExpectedDraftCurrent(
                expectedDraftThreadId,
                currentVisibleThreadId));

    [Fact]
    public void DraftGuardReevaluatesTheCurrentIdentityAndFailsClosed()
    {
        const string expectedDraftThreadId = "client-new-thread:one";
        string? currentVisibleThreadId = expectedDraftThreadId;
        Func<string?> provider = () => currentVisibleThreadId;

        Assert.True(CodexDraftModelToggleService.IsExpectedDraftCurrent(
            expectedDraftThreadId,
            provider));

        currentVisibleThreadId = "019f-real-thread";

        Assert.False(CodexDraftModelToggleService.IsExpectedDraftCurrent(
            expectedDraftThreadId,
            provider));
        Assert.False(CodexDraftModelToggleService.IsExpectedDraftCurrent(
            expectedDraftThreadId,
            () => throw new InvalidOperationException("disconnected")));
    }

    [Fact]
    public void InternalForegroundLeaseTokenCanDriveOnlyAGuardedOperation()
    {
        var operationId =
            CodexModelToggleService.ForegroundDraftOperationPrefix +
            "00000000000000000000000000000001";

        Assert.False(
            CodexDraftModelToggleService.ShouldUseDraftFallback(operationId));
        Assert.True(
            CodexDraftModelToggleService.IsDraftOperationId(operationId));
        Assert.True(
            CodexDraftModelToggleService.IsExpectedDraftCurrent(
                operationId,
                operationId));
        Assert.False(
            CodexDraftModelToggleService.IsExpectedDraftCurrent(
                operationId,
                currentVisibleThreadId: null));
    }

    [Theory]
    [InlineData("newTask", true)]
    [InlineData("feedback", false)]
    [InlineData("NewTask", false)]
    [InlineData(null, false)]
    public void ComposerRebuildRequiresTheExactNativeNewTaskAction(
        string? resolvedAction,
        bool expected) =>
        Assert.Equal(
            expected,
            CodexDraftModelToggleService.CanDispatchComposerRebuild(
                resolvedAction));

    [Fact]
    public void ActiveWorkspaceRootsArePreferredAndDeduplicated()
    {
        const string state = """
            {
              "active-workspace-roots": [
                "D:\\active-one\\",
                "D:\\active-two",
                "D:\\ACTIVE-ONE"
              ],
              "selected-project": {
                "type": "local",
                "projectId": "stale-project"
              },
              "local-projects": {
                "stale-project": {
                  "rootPaths": ["D:\\stale"]
                }
              }
            }
            """;
        var existing = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            @"D:\active-one",
            @"D:\active-two",
            @"D:\stale",
        };

        var selection = Assert.IsType<
            CodexDraftModelToggleService.WorkspaceSelection>(
                CodexDraftModelToggleService.ResolveWorkspaceSelection(
                    state,
                    existing.Contains));

        Assert.Equal(@"D:\active-one", selection.Cwd);
        Assert.Equal(
            [@"D:\active-one", @"D:\active-two"],
            selection.Roots);
    }

    [Fact]
    public void SelectedLocalProjectIsUsedOnlyWhenActiveRootsAreInvalid()
    {
        const string state = """
            {
              "active-workspace-roots": ["relative", "D:\\missing"],
              "selected-project": {
                "type": "local",
                "projectId": "project-one"
              },
              "local-projects": {
                "project-one": {
                  "rootPaths": ["D:\\project-one", "D:\\project-two"]
                }
              }
            }
            """;

        var selection = Assert.IsType<
            CodexDraftModelToggleService.WorkspaceSelection>(
                CodexDraftModelToggleService.ResolveWorkspaceSelection(
                    state,
                    path => path.StartsWith(
                        @"D:\project-",
                        StringComparison.OrdinalIgnoreCase)));

        Assert.Equal(@"D:\project-one", selection.Cwd);
        Assert.Equal(2, selection.Roots.Count);
    }

    [Theory]
    [InlineData("")]
    [InlineData("null")]
    [InlineData("{not-json")]
    [InlineData("{\"active-workspace-roots\":[\"relative\"]}")]
    public void InvalidOrUnresolvableWorkspaceStateFailsClosed(string state) =>
        Assert.Null(CodexDraftModelToggleService.ResolveWorkspaceSelection(
            state,
            _ => false));

    [Fact]
    public void EffectiveConfigSuppliesThePreviousModelAndEffort()
    {
        using var document = JsonDocument.Parse("""
            {
              "config": {
                "model": "gpt-5.6-luna",
                "model_reasoning_effort": "MAX",
                "desktop": {
                  "codex-micro-layout": {
                    "encoderMode": "reasoning"
                  }
                }
              },
              "origins": {}
            }
            """);

        var config = CodexDraftModelToggleService.ParseCurrentConfig(
            document.RootElement);

        Assert.Equal(CodexQuickModel.Luna, config.Model);
        Assert.Equal("max", config.Effort);
        Assert.Equal("reasoning", config.EncoderMode);
    }

    [Fact]
    public void UnknownConfigValuesDoNotInventASelection()
    {
        using var document = JsonDocument.Parse("""
            {
              "config": {
                "model": "future-model",
                "model_reasoning_effort": "future-effort"
              },
              "origins": {}
            }
            """);

        var config = CodexDraftModelToggleService.ParseCurrentConfig(
            document.RootElement);

        Assert.Equal(CodexQuickModel.Unknown, config.Model);
        Assert.Null(config.Effort);
        Assert.Null(config.EncoderMode);
    }

    [Theory]
    [InlineData("{\"requirements\":null}", true, false)]
    [InlineData("{\"requirements\":{}}", true, false)]
    [InlineData(
        "{\"requirements\":{\"models\":{\"newThread\":null}}}",
        true,
        false)]
    [InlineData(
        "{\"requirements\":{\"models\":{\"newThread\":{}}}}",
        true,
        true)]
    [InlineData("{}", false, false)]
    [InlineData("{\"requirements\":\"managed\"}", false, false)]
    [InlineData(
        "{\"requirements\":{\"models\":\"managed\"}}",
        false,
        false)]
    public void ManagedNewThreadRequirementsAreParsedFailClosed(
        string json,
        bool expectedParsed,
        bool expectedManaged)
    {
        using var document = JsonDocument.Parse(json);

        var parsed = CodexDraftModelToggleService
            .TryReadManagedNewThreadRequirement(
                document.RootElement,
                out var managed);

        Assert.Equal(expectedParsed, parsed);
        Assert.Equal(expectedManaged, managed);
    }

    [Theory]
    [InlineData("chatgpt")]
    [InlineData("copilot")]
    [InlineData("apiKey")]
    public void AuthMethodRequiresAnExplicitString(string expected)
    {
        using var document = JsonDocument.Parse(
            $$"""{"authMethod":"{{expected}}"}""");

        Assert.True(CodexDraftModelToggleService.TryReadAuthMethod(
            document.RootElement,
            out var actual));
        Assert.Equal(expected, actual);
    }

    [Fact]
    public void MissingAuthMethodFailsClosed()
    {
        using var document = JsonDocument.Parse("{}");

        Assert.False(CodexDraftModelToggleService.TryReadAuthMethod(
            document.RootElement,
            out _));
    }

    [Fact]
    public void LunaMaxUsesSafeProbeThenNativeExactFinalStep()
    {
        var probe = CodexDraftModelToggleService.ResolveTargetEffortProbe(
            ["low", "medium", "high", "xhigh", "max"],
            "max");

        Assert.Equal("max", probe.SeedEffort);
        Assert.Equal(new(true, "xhigh"), probe.ProbeStep);
        Assert.Equal(new(false, "max"), probe.FinalStep);
    }

    [Fact]
    public void SolUltraUsesSafeDownwardProbeAndPassiveReplay()
    {
        var probe = CodexDraftModelToggleService.ResolveTargetEffortProbe(
            ["low", "medium", "high", "xhigh", "max", "ultra"],
            "ultra");

        Assert.Equal("ultra", probe.SeedEffort);
        Assert.Equal(new(true, "max"), probe.ProbeStep);
        Assert.Null(probe.FinalStep);
    }

    [Fact]
    public void TargetProbeNeverStepsUpIntoUltraWarning() =>
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            CodexDraftModelToggleService.ResolveTargetEffortProbe(
                ["low", "ultra"],
                "low"));

    [Fact]
    public void ModelCatalogPreservesPickerOrderAndEffortOrder()
    {
        using var document = JsonDocument.Parse("""
            {
              "data": [
                {
                  "model": "gpt-5.6-sol",
                  "hidden": false,
                  "defaultReasoningEffort": "medium",
                  "supportedReasoningEfforts": [
                    { "reasoningEffort": "low" },
                    { "reasoningEffort": "medium" },
                    { "reasoningEffort": "ultra" }
                  ]
                },
                {
                  "model": "gpt-5.6-luna",
                  "hidden": false,
                  "defaultReasoningEffort": "medium",
                  "supportedReasoningEfforts": [
                    { "reasoningEffort": "medium" },
                    { "reasoningEffort": "max" }
                  ]
                }
              ]
            }
            """);

        var catalog = CodexDraftModelToggleService.ParseModelCatalog(
            document.RootElement);

        Assert.Equal(["gpt-5.6-sol", "gpt-5.6-luna"],
            catalog.Select(model => model.Id));
        Assert.Equal(["low", "medium", "ultra"],
            catalog[0].SupportedEfforts);
        Assert.Equal(CodexQuickModel.Luna, catalog[1].Model);
    }

    [Theory]
    [InlineData("simple")]
    [InlineData("advanced")]
    public void PickerViewComesFromTheRendererOwnedAtom(string view)
    {
        var state = $$"""
            {
              "electron-persisted-atom-state": {
                "composer-model-picker-menu-view-v1": "{{view}}"
              }
            }
            """;

        Assert.Equal(
            view,
            CodexDraftModelToggleService.ParseModelPickerMenuView(state));
    }

    [Fact]
    public void PersistedTransitionProbeReadsOnlyTopLevelModelSettings()
    {
        var config = CodexDraftModelToggleService.ParseTopLevelUserConfig("""
            model = "gpt-5.6-sol"
            model_reasoning_effort = "ultra"

            [profiles.other]
            model = "gpt-5.6-luna"
            model_reasoning_effort = "max"
            """);

        Assert.Equal(CodexQuickModel.Sol, config.Model);
        Assert.Equal("ultra", config.Effort);
    }

    [Fact]
    public void MatchingPersistedConfigCannotBypassLostDraftContext()
    {
        var latest = new CodexDraftModelToggleService.DraftConfig(
            CodexQuickModel.Sol,
            "ultra");

        Assert.False(CodexDraftModelToggleService.CanAcceptPersistedConfig(
            latest,
            CodexQuickModel.Sol,
            "ultra",
            freshDraftCurrent: false));
        Assert.True(CodexDraftModelToggleService.CanAcceptPersistedConfig(
            latest,
            CodexQuickModel.Sol,
            "ultra",
            freshDraftCurrent: true));
    }

}
