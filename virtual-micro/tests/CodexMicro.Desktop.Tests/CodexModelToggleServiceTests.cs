using System.Buffers.Binary;
using System.Text.Json;
using CodexMicro.Desktop.Services;
using Xunit;

namespace CodexMicro.Desktop.Tests;

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
        }
        finally
        {
            File.Delete(path);
        }
    }

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
