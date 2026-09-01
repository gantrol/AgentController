using System.Buffers.Binary;
using System.Text.Json;
using CodexMicro.Desktop.Services;
using Xunit;

namespace CodexMicro.Desktop.Tests;

[Collection("Codex environment variables")]
public sealed class CodexModelToggleServiceTests
{
    [Theory]
    [InlineData("gpt-5.6-sol", 1)]
    [InlineData("GPT-5.6-SOL", 1)]
    [InlineData("gpt-5.6-terra", 2)]
    [InlineData("gpt-5.6-luna", 3)]
    [InlineData("gpt-5.5", 0)]
    [InlineData("", 0)]
    public void ParseModelIdRecognizesOnlyQuickToggleModels(
        string value,
        int expected)
    {
        Assert.Equal(
            (CodexQuickModel)expected,
            CodexModelToggleService.ParseModelId(value));
    }

    [Theory]
    [InlineData(1, 3)]
    [InlineData(3, 1)]
    [InlineData(2, 1)]
    [InlineData(0, 1)]
    public void ResolveToggleTargetTogglesConfiguredPairAndDefaultsToA(
        int current,
        int expected)
    {
        Assert.Equal(
            (CodexQuickModel)expected,
            CodexModelToggleService.ResolveToggleTarget(
                (CodexQuickModel)current,
                CodexQuickModel.Sol,
                CodexQuickModel.Luna));
    }

    [Fact]
    public void VisibleThreadMustStillMatchImmediatelyBeforeTheUpdate()
    {
        Assert.Null(CodexModelToggleService.ValidateVisibleThreadSelection(
            ["thread-a", "thread-a"],
            "thread-a"));
        Assert.Equal(
            "no-visible-thread",
            CodexModelToggleService.ValidateVisibleThreadSelection(
                [],
                "thread-a"));
        Assert.Equal(
            "multiple-visible-threads",
            CodexModelToggleService.ValidateVisibleThreadSelection(
                ["thread-a", "thread-b"],
                "thread-a"));
        Assert.Equal(
            "visible-thread-changed",
            CodexModelToggleService.ValidateVisibleThreadSelection(
                ["thread-b"],
                "thread-a"));
    }

    [Theory]
    [InlineData("client-new-thread:abc", false)]
    [InlineData("019f-real-thread", true)]
    public void RendererDraftsNeverEnterSemanticOwnerTracking(
        string threadId,
        bool expected) =>
        Assert.Equal(
            expected,
            CodexModelToggleService.CanTrackSemanticThread(threadId));

    [Fact]
    public void RendererDraftIdentitySurvivesRealDraftRealTransitions()
    {
        const string sourceClientId = "renderer-a";
        const string firstRealThreadId = "019f-real-thread-a";
        const string draftThreadId = "client-new-thread:draft-a";
        const string secondRealThreadId = "019f-real-thread-b";
        var visibleThreads = new Dictionary<string, string>(
            StringComparer.Ordinal);

        Assert.True(CodexModelToggleService.ApplyVisibleThreadFollowingChange(
            visibleThreads,
            sourceClientId,
            firstRealThreadId,
            following: true));
        Assert.Equal(
            new CodexModelToggleService.VisibleThreadSelection(
                firstRealThreadId,
                firstRealThreadId),
            CodexModelToggleService.ResolveVisibleThreadSelection(
                visibleThreads.Values));

        Assert.True(CodexModelToggleService.ApplyVisibleThreadFollowingChange(
            visibleThreads,
            sourceClientId,
            draftThreadId,
            following: true));
        Assert.Equal(draftThreadId, visibleThreads[sourceClientId]);
        Assert.Equal(
            new CodexModelToggleService.VisibleThreadSelection(
                draftThreadId,
                SemanticThreadId: null),
            CodexModelToggleService.ResolveVisibleThreadSelection(
                visibleThreads.Values));

        // A delayed release for the old real task must not erase the draft.
        Assert.False(CodexModelToggleService.ApplyVisibleThreadFollowingChange(
            visibleThreads,
            sourceClientId,
            firstRealThreadId,
            following: false));
        Assert.Equal(draftThreadId, visibleThreads[sourceClientId]);

        Assert.True(CodexModelToggleService.ApplyVisibleThreadFollowingChange(
            visibleThreads,
            sourceClientId,
            secondRealThreadId,
            following: true));
        Assert.Equal(
            new CodexModelToggleService.VisibleThreadSelection(
                secondRealThreadId,
                secondRealThreadId),
            CodexModelToggleService.ResolveVisibleThreadSelection(
                visibleThreads.Values));
    }

    [Fact]
    public void MissingOrAmbiguousVisibilityRemainsUnknownNotDraft()
    {
        Assert.Equal(
            default,
            CodexModelToggleService.ResolveVisibleThreadSelection([]));
        Assert.Equal(
            default,
            CodexModelToggleService.ResolveVisibleThreadSelection(
                ["thread-a", "client-new-thread:draft-a"]));
    }

    [Fact]
    public void DraftEvidenceSelectsOnlyTheRequestedForegroundWindow()
    {
        var now = DateTimeOffset.UtcNow;
        var firstWindow = new IntPtr(101);
        var secondWindow = new IntPtr(202);
        var visible = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["renderer-background"] = "019f-background-thread",
        };
        var evidence = new Dictionary<
            string,
            CodexModelToggleService.RendererDraftEvidence>(
                StringComparer.Ordinal)
        {
            ["renderer-first"] = new(4, now, firstWindow),
            ["renderer-second"] = new(5, now, secondWindow),
        };

        Assert.Equal(
            "renderer-first",
            CodexModelToggleService.SelectRendererDraftEvidenceClient(
                visible,
                evidence,
                now,
                TimeSpan.FromSeconds(30),
                firstWindow));
        Assert.Equal(
            "renderer-second",
            CodexModelToggleService.SelectRendererDraftEvidenceClient(
                visible,
                evidence,
                now,
                TimeSpan.FromSeconds(30),
                secondWindow));
        Assert.Null(
            CodexModelToggleService.SelectRendererDraftEvidenceClient(
                visible,
                evidence,
                now,
                TimeSpan.FromSeconds(30),
                new IntPtr(303)));
    }

    [Fact]
    public void RealFollowingOrExpiredEvidenceCannotTargetADraft()
    {
        var now = DateTimeOffset.UtcNow;
        var window = new IntPtr(101);
        var visible = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["renderer-real"] = "019f-real-thread",
        };
        var evidence = new Dictionary<
            string,
            CodexModelToggleService.RendererDraftEvidence>(
                StringComparer.Ordinal)
        {
            ["renderer-real"] = new(8, now, window),
            ["renderer-expired"] = new(
                9,
                now - TimeSpan.FromMinutes(1),
                window),
        };

        Assert.Null(
            CodexModelToggleService.SelectRendererDraftEvidenceClient(
                visible,
                evidence,
                now,
                TimeSpan.FromSeconds(30),
                window));
        Assert.Null(
            CodexModelToggleService.SelectRendererDraftEvidenceClient(
                new Dictionary<string, string>(StringComparer.Ordinal),
                evidence,
                now,
                TimeSpan.FromSeconds(30),
                IntPtr.Zero));
    }

    [Fact]
    public void DraftLeaseAdmissionReservesTimeForTheWholeMutation()
    {
        Assert.Equal(
            TimeSpan.FromSeconds(20),
            CodexModelToggleService.ForegroundDraftLeaseAdmissionLifetime);

        var now = DateTimeOffset.UtcNow;
        var window = new IntPtr(101);
        var visible = new Dictionary<string, string>(StringComparer.Ordinal);
        var evidence = new Dictionary<
            string,
            CodexModelToggleService.RendererDraftEvidence>(
                StringComparer.Ordinal)
        {
            ["renderer-with-budget"] = new(
                10,
                now - CodexModelToggleService
                    .ForegroundDraftLeaseAdmissionLifetime,
                window),
            ["renderer-too-late"] = new(
                11,
                now - CodexModelToggleService
                    .ForegroundDraftLeaseAdmissionLifetime -
                    TimeSpan.FromTicks(1),
                window),
        };

        Assert.Equal(
            "renderer-too-late",
            CodexModelToggleService.SelectRendererDraftEvidenceClient(
                visible,
                evidence,
                now,
                TimeSpan.FromSeconds(30),
                window));

        var withinBudget = new CodexModelToggleService.ForegroundDraftLease(
            "micro-client",
            1,
            CodexModelToggleService.ForegroundDraftOperationPrefix +
                "00000000000000000000000000000001",
            "renderer-with-budget",
            10,
            window,
            now - CodexModelToggleService
                .ForegroundDraftLeaseAdmissionLifetime);
        var tooLate = withinBudget with
        {
            RendererClientId = "renderer-too-late",
            RendererDraftGeneration = 11,
            RendererDraftObservedAt =
                now - CodexModelToggleService
                    .ForegroundDraftLeaseAdmissionLifetime -
                    TimeSpan.FromTicks(1),
        };

        Assert.True(
            CodexModelToggleService.HasForegroundDraftOperationBudget(
                withinBudget,
                now));
        Assert.False(
            CodexModelToggleService.HasForegroundDraftOperationBudget(
                tooLate,
                now));
    }

    [Fact]
    public void SyntheticOperationIdIsInternalProofNotRendererDraftIdentity()
    {
        var operationId =
            CodexModelToggleService.ForegroundDraftOperationPrefix +
            "00000000000000000000000000000001";

        Assert.False(CodexDraftModelToggleService.ShouldUseDraftFallback(null));
        Assert.False(CodexDraftModelToggleService.ShouldUseDraftFallback(
            operationId));
        Assert.True(CodexModelToggleService.IsForegroundDraftOperationId(
            operationId));
        Assert.False(CodexModelToggleService.IsForegroundDraftOperationId(
            CodexModelToggleService.ForegroundDraftOperationPrefix + "not-a-guid"));
        Assert.False(CodexModelToggleService.IsForegroundDraftOperationId(
            "client-new-thread:renderer-draft"));
    }

    [Fact]
    public void OnlyACompletedGuardedRebuildMayRenewDraftEvidence()
    {
        var operationId =
            CodexModelToggleService.ForegroundDraftOperationPrefix +
            "00000000000000000000000000000001";
        var lease = new CodexModelToggleService.ForegroundDraftLease(
            "micro-client",
            1,
            operationId,
            "renderer-a",
            4,
            new IntPtr(101),
            DateTimeOffset.UtcNow);
        var guardedRebuild = new CodexModelToggleResult(
            Succeeded: true,
            Previous: CodexQuickModel.Sol,
            Current: CodexQuickModel.Luna,
            ThreadId: operationId,
            Detail: CodexDraftModelToggleService
                .ComposerRebuildDispatchReceipt);

        Assert.True(CodexModelToggleService
            .CanRenewForegroundDraftEvidenceAfterGuardedRebuild(
                lease,
                guardedRebuild));
        Assert.False(CodexModelToggleService
            .CanRenewForegroundDraftEvidenceAfterGuardedRebuild(
                lease,
                guardedRebuild with { Detail = "config-only" }));
        Assert.False(CodexModelToggleService
            .CanRenewForegroundDraftEvidenceAfterGuardedRebuild(
                lease,
                guardedRebuild with { Succeeded = false }));
        Assert.False(CodexModelToggleService
            .CanRenewForegroundDraftEvidenceAfterGuardedRebuild(
                lease,
                guardedRebuild with { ThreadId = "another-operation" }));
    }

    [Fact]
    public void DraftEvidenceRenewalRequiresTheSameRendererWindowAndGeneration()
    {
        var now = DateTimeOffset.UtcNow;
        var window = new IntPtr(101);
        var operationId =
            CodexModelToggleService.ForegroundDraftOperationPrefix +
            "00000000000000000000000000000001";
        var lease = new CodexModelToggleService.ForegroundDraftLease(
            "micro-client",
            1,
            operationId,
            "renderer-a",
            4,
            window,
            now);
        var visible = new Dictionary<string, string>(StringComparer.Ordinal);
        var evidence = new Dictionary<
            string,
            CodexModelToggleService.RendererDraftEvidence>(
                StringComparer.Ordinal)
        {
            ["renderer-a"] = new(4, now, window),
        };

        Assert.True(CodexModelToggleService
            .HasRenewableForegroundDraftEvidence(
                lease,
                visible,
                evidence));
        Assert.False(CodexModelToggleService
            .TryCreateRenewedForegroundDraftEvidence(
                lease,
                visible,
                evidence,
                renewedGeneration: 5,
                observedAt: now + TimeSpan.FromSeconds(31),
                out _));

        visible["renderer-a"] = "client-new-thread:replacement";
        Assert.True(CodexModelToggleService
            .HasRenewableForegroundDraftEvidence(
                lease,
                visible,
                evidence));

        visible["renderer-a"] = "019f-real-thread";
        Assert.False(CodexModelToggleService
            .HasRenewableForegroundDraftEvidence(
                lease,
                visible,
                evidence));

        visible.Clear();
        evidence["renderer-a"] = new(5, now, window);
        Assert.False(CodexModelToggleService
            .HasRenewableForegroundDraftEvidence(
                lease,
                visible,
                evidence));

        evidence["renderer-a"] = new(4, now, new IntPtr(202));
        Assert.False(CodexModelToggleService
            .HasRenewableForegroundDraftEvidence(
                lease,
                visible,
                evidence));
    }

    [Fact]
    public void ConsecutiveRenewalsKeepTheThirdToggleAdmissible()
    {
        var now = DateTimeOffset.UtcNow;
        var window = new IntPtr(101);
        var initialObservedAt = now - TimeSpan.FromSeconds(10);
        var visible = new Dictionary<string, string>(StringComparer.Ordinal);
        var evidence = new Dictionary<
            string,
            CodexModelToggleService.RendererDraftEvidence>(
                StringComparer.Ordinal)
        {
            ["renderer-a"] = new(4, initialObservedAt, window),
        };
        var firstLease = new CodexModelToggleService.ForegroundDraftLease(
            "micro-client",
            1,
            CodexModelToggleService.ForegroundDraftOperationPrefix +
                "00000000000000000000000000000001",
            "renderer-a",
            4,
            window,
            initialObservedAt);

        Assert.True(CodexModelToggleService
            .TryCreateRenewedForegroundDraftEvidence(
                firstLease,
                visible,
                evidence,
                renewedGeneration: 5,
                observedAt: now,
                out var firstRenewal));
        evidence["renderer-a"] = firstRenewal;
        Assert.False(CodexModelToggleService
            .HasRenewableForegroundDraftEvidence(
                firstLease,
                visible,
                evidence));

        var secondLease = firstLease with
        {
            OperationId =
                CodexModelToggleService.ForegroundDraftOperationPrefix +
                "00000000000000000000000000000002",
            RendererDraftGeneration = 5,
            RendererDraftObservedAt = now,
        };
        var secondObservedAt = now + TimeSpan.FromSeconds(10);
        Assert.True(CodexModelToggleService
            .TryCreateRenewedForegroundDraftEvidence(
                secondLease,
                visible,
                evidence,
                renewedGeneration: 6,
                observedAt: secondObservedAt,
                out var secondRenewal));
        evidence["renderer-a"] = secondRenewal;

        var thirdLease = secondLease with
        {
            OperationId =
                CodexModelToggleService.ForegroundDraftOperationPrefix +
                "00000000000000000000000000000003",
            RendererDraftGeneration = 6,
            RendererDraftObservedAt = secondObservedAt,
        };

        Assert.True(
            CodexModelToggleService.HasForegroundDraftOperationBudget(
                secondLease,
                now + TimeSpan.FromSeconds(5)));
        Assert.True(
            CodexModelToggleService.HasForegroundDraftOperationBudget(
                thirdLease,
                now + TimeSpan.FromSeconds(15)));
    }

    [Fact]
    public async Task IpcFrameUsesUInt32LittleEndianUtf8Json()
    {
        var frame = CodexModelToggleService.EncodeFrame(new
        {
            type = "request",
            method = "initialize",
            @params = new { clientType = "codexmicro-test" },
        });

        var payloadLength = BinaryPrimitives.ReadUInt32LittleEndian(frame);
        Assert.Equal(
            checked((uint)(frame.Length - sizeof(uint))),
            payloadLength);
        await using var stream = new MemoryStream(frame);
        using var message = await CodexModelToggleService.ReadFrameAsync(
            stream,
            CancellationToken.None);
        Assert.Equal(
            "initialize",
            message.RootElement.GetProperty("method").GetString());
        Assert.Equal(
            "codexmicro-test",
            message.RootElement
                .GetProperty("params")
                .GetProperty("clientType")
                .GetString());
    }

    [Fact]
    public async Task IpcFrameRejectsInvalidLengthBeforeAllocatingPayload()
    {
        var prefix = new byte[sizeof(uint)];
        BinaryPrimitives.WriteUInt32LittleEndian(prefix, uint.MaxValue);
        await using var stream = new MemoryStream(prefix);

        await Assert.ThrowsAsync<InvalidDataException>(async () =>
        {
            using var _ = await CodexModelToggleService.ReadFrameAsync(
                stream,
                CancellationToken.None);
        });
    }

    [Fact]
    public void TargetEffortUsesRememberedValueOnlyWhenModelSupportsIt()
    {
        var path = Path.Combine(
            Path.GetTempPath(),
            $"codex-model-cache-{Guid.NewGuid():N}.json");
        try
        {
            File.WriteAllText(
                path,
                """
                {
                  "models": [
                    {
                      "slug": "gpt-5.6-sol",
                      "default_reasoning_level": "low",
                      "supported_reasoning_levels": [
                        { "effort": "low" },
                        { "effort": "ultra" }
                      ]
                    },
                    {
                      "slug": "gpt-5.6-luna",
                      "default_reasoning_level": "medium",
                      "supported_reasoning_levels": [
                        { "effort": "low" },
                        { "effort": "medium" },
                        { "effort": "max" }
                      ]
                    }
                  ]
                }
                """);

            Assert.Equal(
                "ultra",
                CodexModelToggleService.ResolveTargetEffort(
                    "gpt-5.6-sol",
                    "ultra",
                    path));
            Assert.Equal(
                "medium",
                CodexModelToggleService.ResolveTargetEffort(
                    "gpt-5.6-luna",
                    "ultra",
                    path));
            Assert.Equal(
                "medium",
                CodexModelToggleService.ResolveTargetEffort(
                    "gpt-5.6-luna",
                    rememberedEffort: null,
                    path));
            Assert.Equal(
                "max",
                CodexModelToggleService.ResolveTargetEffort(
                    "gpt-5.6-luna",
                    "max",
                    path));
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void TargetEffortPreservesKnownSelectionWhenModelCacheIsUnavailable()
    {
        var missingPath = Path.Combine(
            Path.GetTempPath(),
            $"missing-codex-model-cache-{Guid.NewGuid():N}.json");
        var malformedPath = Path.Combine(
            Path.GetTempPath(),
            $"malformed-codex-model-cache-{Guid.NewGuid():N}.json");
        try
        {
            File.WriteAllText(malformedPath, "{ not valid json");

            Assert.Equal(
                "ultra",
                CodexModelToggleService.ResolveTargetEffort(
                    "gpt-5.6-sol",
                    "Ultra",
                    missingPath));
            Assert.Equal(
                "max",
                CodexModelToggleService.ResolveTargetEffort(
                    "gpt-5.6-luna",
                    "max",
                    malformedPath));
            Assert.Equal(
                "low",
                CodexModelToggleService.ResolveTargetEffort(
                    "gpt-5.6-sol",
                    "not-a-real-effort",
                    missingPath));
        }
        finally
        {
            File.Delete(malformedPath);
        }
    }

    [Fact]
    public void TargetEffortFindsModelsCacheUnderConfiguredCodexHome()
    {
        var codexHome = Path.Combine(
            Path.GetTempPath(),
            $"codex-home-{Guid.NewGuid():N}");
        var previousCodexHome = Environment.GetEnvironmentVariable("CODEX_HOME");
        Directory.CreateDirectory(codexHome);
        try
        {
            File.WriteAllText(
                Path.Combine(codexHome, "models_cache.json"),
                """
                {
                  "models": [
                    {
                      "slug": "gpt-5.6-sol",
                      "default_reasoning_level": "ultra",
                      "supported_reasoning_levels": [
                        { "effort": "low" },
                        { "effort": "ultra" }
                      ]
                    }
                  ]
                }
                """);
            Environment.SetEnvironmentVariable("CODEX_HOME", codexHome);

            Assert.Equal(
                Path.Combine(codexHome, "models_cache.json"),
                CodexModelToggleService.ResolveModelsCachePath(codexHome));
            Assert.Equal(
                "ultra",
                CodexModelToggleService.ResolveTargetEffort(
                    "gpt-5.6-sol",
                    rememberedEffort: null));
        }
        finally
        {
            Environment.SetEnvironmentVariable("CODEX_HOME", previousCodexHome);
            Directory.Delete(codexHome, recursive: true);
        }
    }

    [Fact]
    public void SnapshotEffortPrefersLatestThreadSettings()
    {
        using var document = JsonDocument.Parse(
            """
            {
              "latestReasoningEffort": null,
              "latestThreadSettings": {
                "model": "gpt-5.6-sol",
                "effort": "ultra"
              }
            }
            """);

        Assert.Equal(
            "ultra",
            CodexModelToggleService.ReadSnapshotEffort(document.RootElement));
    }

    [Fact]
    public void SnapshotEffortFallsBackToLegacyField()
    {
        using var document = JsonDocument.Parse(
            """
            {
              "latestReasoningEffort": "high"
            }
            """);

        Assert.Equal(
            "high",
            CodexModelToggleService.ReadSnapshotEffort(document.RootElement));
    }

    [Fact]
    public void SnapshotWithoutModelCanBeCompletedByContinuousPatches()
    {
        var accumulator = new CodexThreadModelStateAccumulator(
            "thread-a",
            "owner-a");
        using var snapshot = JsonDocument.Parse(
            """
            {
              "type": "snapshot",
              "revision": 40,
              "conversationState": {
                "latestReasoningEffort": null,
                "latestThreadSettings": null
              }
            }
            """);
        var initial = accumulator.ApplyChange(snapshot.RootElement);

        Assert.True(initial.Applied);
        Assert.False(initial.RequiresSnapshot);
        Assert.Null(initial.State);

        using var patches = JsonDocument.Parse(
            """
            {
              "type": "patches",
              "baseRevision": 40,
              "revision": 41,
              "patches": [
                {
                  "op": "replace",
                  "path": ["turns", 0, "status"],
                  "value": "completed"
                },
                {
                  "op": "add",
                  "path": "/latestThreadSettings/model",
                  "value": "gpt-5.6-sol"
                },
                {
                  "op": "add",
                  "path": ["latestThreadSettings", "effort"],
                  "value": "ultra"
                }
              ]
            }
            """);
        var completed = accumulator.ApplyChange(patches.RootElement);

        Assert.True(completed.Applied);
        Assert.False(completed.RequiresSnapshot);
        Assert.Equal(
            new CodexThreadModelState(
                "thread-a",
                "gpt-5.6-sol",
                "ultra"),
            completed.State);
        Assert.Equal(41, accumulator.Revision);
    }

    [Fact]
    public void UnknownContinuousPatchAdvancesRevisionWithoutChangingState()
    {
        var accumulator = new CodexThreadModelStateAccumulator(
            "thread-a",
            "owner-a");
        using var snapshot = JsonDocument.Parse(
            """
            {
              "type": "snapshot",
              "revision": 7,
              "conversationState": {
                "latestModel": "gpt-5.6-luna",
                "latestThreadSettings": {
                  "effort": "max"
                }
              }
            }
            """);
        var initial = accumulator.ApplyChange(snapshot.RootElement);
        using var patches = JsonDocument.Parse(
            """
            {
              "type": "patches",
              "baseRevision": 7,
              "revision": 8,
              "patches": [
                {
                  "op": "future-operation",
                  "path": ["futureField"],
                  "value": true
                },
                {
                  "op": "replace",
                  "path": ["turns", 0, "status"],
                  "value": "completed"
                }
              ]
            }
            """);
        var updated = accumulator.ApplyChange(patches.RootElement);

        Assert.True(updated.Applied);
        Assert.False(updated.RequiresSnapshot);
        Assert.Equal(initial.State, updated.State);
        Assert.Equal(8, accumulator.Revision);
    }

    [Fact]
    public void RevisionGapInvalidatesStateUntilANewSnapshotArrives()
    {
        var accumulator = new CodexThreadModelStateAccumulator(
            "thread-a",
            "owner-a");
        using var snapshot = JsonDocument.Parse(
            """
            {
              "type": "snapshot",
              "revision": 3,
              "conversationState": {
                "latestModel": "gpt-5.6-luna",
                "latestReasoningEffort": "medium"
              }
            }
            """);
        Assert.NotNull(accumulator.ApplyChange(snapshot.RootElement).State);

        using var gap = JsonDocument.Parse(
            """
            {
              "type": "patches",
              "baseRevision": 4,
              "revision": 5,
              "patches": [
                {
                  "op": "replace",
                  "path": ["latestModel"],
                  "value": "gpt-5.6-sol"
                }
              ]
            }
            """);
        var invalidated = accumulator.ApplyChange(gap.RootElement);

        Assert.False(invalidated.Applied);
        Assert.True(invalidated.RequiresSnapshot);
        Assert.Null(invalidated.State);
        Assert.Null(accumulator.Revision);

        using var patchWithoutBaseline = JsonDocument.Parse(
            """
            {
              "type": "patches",
              "baseRevision": 5,
              "revision": 6,
              "patches": []
            }
            """);
        var ignored = accumulator.ApplyChange(patchWithoutBaseline.RootElement);
        Assert.False(ignored.Applied);
        Assert.False(ignored.RequiresSnapshot);
        Assert.Null(ignored.State);

        using var resnapshot = JsonDocument.Parse(
            """
            {
              "type": "snapshot",
              "revision": 10,
              "conversationState": {
                "latestThreadSettings": {
                  "model": "gpt-5.6-sol"
                }
              }
            }
            """);
        var restored = accumulator.ApplyChange(resnapshot.RootElement);
        Assert.Equal(
            new CodexThreadModelState(
                "thread-a",
                "gpt-5.6-sol",
                null),
            restored.State);
    }

    [Fact]
    public void MissingSnapshotEffortRemainsUnconfirmed()
    {
        var accumulator = new CodexThreadModelStateAccumulator(
            "thread-a",
            "owner-a");
        using var snapshot = JsonDocument.Parse(
            """
            {
              "type": "snapshot",
              "revision": 1,
              "conversationState": {
                "latestModel": "gpt-5.6-sol"
              }
            }
            """);

        var state = accumulator.ApplyChange(snapshot.RootElement).State;

        Assert.NotNull(state);
        Assert.Null(state.Effort);
    }

    [Fact]
    public void ExplicitNullSettingsEffortDoesNotReuseLegacyEffort()
    {
        var accumulator = new CodexThreadModelStateAccumulator(
            "thread-a",
            "owner-a");
        using var snapshot = JsonDocument.Parse(
            """
            {
              "type": "snapshot",
              "revision": 1,
              "conversationState": {
                "latestModel": "gpt-5.6-sol",
                "latestReasoningEffort": "ultra",
                "latestThreadSettings": {
                  "model": "gpt-5.6-luna",
                  "effort": null
                }
              }
            }
            """);

        var state = accumulator.ApplyChange(snapshot.RootElement).State;

        Assert.NotNull(state);
        Assert.Equal("gpt-5.6-luna", state.ModelId);
        Assert.Null(state.Effort);
    }

    [Fact]
    public void ConfirmedSettingsCannotBeRolledBackByAnOldSnapshot()
    {
        var accumulator = new CodexThreadModelStateAccumulator(
            "thread-a",
            "owner-a");
        using var initial = JsonDocument.Parse(
            """
            {
              "type": "snapshot",
              "revision": 10,
              "conversationState": {
                "latestModel": "gpt-5.6-sol",
                "latestReasoningEffort": "ultra"
              }
            }
            """);
        Assert.NotNull(accumulator.ApplyChange(initial.RootElement).State);
        accumulator.ConfirmSettings("gpt-5.6-luna", "max");

        using var delayed = JsonDocument.Parse(
            """
            {
              "type": "snapshot",
              "revision": 10,
              "conversationState": {
                "latestModel": "gpt-5.6-sol",
                "latestReasoningEffort": "ultra"
              }
            }
            """);
        Assert.False(accumulator.ApplyChange(delayed.RootElement).Applied);

        using var nextRevision = JsonDocument.Parse(
            """
            {
              "type": "patches",
              "baseRevision": 10,
              "revision": 11,
              "patches": []
            }
            """);
        Assert.Equal(
            new CodexThreadModelState(
                "thread-a",
                "gpt-5.6-luna",
                "max"),
            accumulator.ApplyChange(nextRevision.RootElement).State);
    }

    [Fact]
    public void ConfirmationWithoutABaselineAdoptsStaleRevisionWithoutRollback()
    {
        var accumulator = new CodexThreadModelStateAccumulator(
            "thread-a",
            "owner-new");
        accumulator.ConfirmSettings("gpt-5.6-luna", "max");

        using var stale = JsonDocument.Parse(
            """
            {
              "type": "snapshot",
              "revision": 20,
              "conversationState": {
                "latestModel": "gpt-5.6-sol",
                "latestReasoningEffort": "ultra"
              }
            }
            """);
        var baseline = accumulator.ApplyChange(stale.RootElement);
        Assert.True(baseline.Applied);
        Assert.Equal(20, accumulator.Revision);
        Assert.Equal(
            new CodexThreadModelState(
                "thread-a",
                "gpt-5.6-luna",
                "max"),
            baseline.State);

        using var nextPatch = JsonDocument.Parse(
            """
            {
              "type": "patches",
              "baseRevision": 20,
              "revision": 21,
              "patches": [
                {
                  "op": "replace",
                  "path": ["turns", 0, "status"],
                  "value": "completed"
                }
              ]
            }
            """);
        var advanced = accumulator.ApplyChange(nextPatch.RootElement);

        Assert.True(advanced.Applied);
        Assert.Equal(21, accumulator.Revision);
        Assert.Equal(
            new CodexThreadModelState(
                "thread-a",
                "gpt-5.6-luna",
                "max"),
            advanced.State);
    }

    [Theory]
    [InlineData("no-client-found", true)]
    [InlineData("client-disconnected", true)]
    [InlineData("request-timeout", true)]
    [InlineData("thread-follower-update-thread-settings-timeout", true)]
    [InlineData("request-version-mismatch", false)]
    [InlineData("no-handler-for-request", false)]
    [InlineData("method-mismatch", false)]
    [InlineData(null, false)]
    public void SettingsUpdateErrorsAreClassifiedForSafeRetry(
        string? error,
        bool expected) =>
        Assert.Equal(
            expected,
            CodexModelToggleService.IsTransientSettingsUpdateFailure(error));

    [Fact]
    public void TargetConfirmationRequiresBothModelAndEffort()
    {
        var state = new CodexThreadModelState(
            "thread-a",
            "GPT-5.6-LUNA",
            "MAX");

        Assert.True(CodexModelToggleService.ThreadStateMatchesTarget(
            state,
            "gpt-5.6-luna",
            "max"));
        Assert.False(CodexModelToggleService.ThreadStateMatchesTarget(
            state,
            "gpt-5.6-luna",
            "ultra"));
        Assert.False(CodexModelToggleService.ThreadStateMatchesTarget(
            state,
            "gpt-5.6-sol",
            "max"));
    }

    [Theory]
    [InlineData(
        0,
        7, 7, true, false, false, true)]
    [InlineData(
        0,
        6, 7, true, false, false, false)]
    [InlineData(
        0,
        7, 7, false, false, false, false)]
    [InlineData(
        1,
        0, 0, false, true, false, true)]
    [InlineData(
        1,
        0, 0, false, false, false, false)]
    [InlineData(
        2,
        1, 9, false, false, false, true)]
    [InlineData(
        2,
        1, 9, true, false, false, false)]
    [InlineData(
        2,
        1, 9, false, false, true, false)]
    public void FollowerSignalIsRevalidatedInsideTheSerializedWriter(
        int intent,
        int expectedGeneration,
        int currentGeneration,
        bool trackedKeyMatches,
        bool expectedWaiterIsCurrent,
        bool matchingWaiterExists,
        bool expected) =>
        Assert.Equal(
            expected,
            CodexModelToggleService.ShouldWriteFollowerSignal(
                (CodexModelToggleService.FollowerSignalIntent)intent,
                expectedGeneration,
                currentGeneration,
                trackedKeyMatches,
                expectedWaiterIsCurrent,
                matchingWaiterExists));

    [Fact]
    public void EffortStorePersistsPerThreadAndFullModelId()
    {
        var path = Path.Combine(
            Path.GetTempPath(),
            $"codex-model-efforts-{Guid.NewGuid():N}.json");
        try
        {
            var store = new CodexThreadModelEffortStore(path);
            store.Remember("thread-a", "gpt-5.6-sol", "ultra");
            store.Remember("thread-a", "gpt-5.6-luna", "medium");
            store.Remember("thread-b", "gpt-5.6-sol", "low");

            var reloaded = new CodexThreadModelEffortStore(path);
            Assert.Equal(
                "ultra",
                reloaded.Recall("thread-a", "gpt-5.6-sol"));
            Assert.Equal(
                "medium",
                reloaded.Recall("thread-a", "gpt-5.6-luna"));
            Assert.Equal(
                "low",
                reloaded.Recall("thread-b", "gpt-5.6-sol"));
            Assert.Null(reloaded.Recall("thread-b", "gpt-5.6-luna"));
        }
        finally
        {
            File.Delete(path);
            File.Delete(path + ".tmp");
        }
    }
}

[CollectionDefinition("Codex environment variables", DisableParallelization = true)]
public sealed class CodexEnvironmentVariableCollection
{
}
