using CodexMicro.Desktop.Services;
using Xunit;

namespace CodexMicro.Desktop.Tests;

public sealed class CodexModelToggleServiceTests
{
    [Theory]
    [InlineData("5.6 Sol", 1)]
    [InlineData("GPT-5.6-Sol Medium", 1)]
    [InlineData("Model 5.6 Luna", 3)]
    [InlineData("5.6 Luna Max", 3)]
    [InlineData("5.6 Terra High", 2)]
    [InlineData("", 0)]
    public void ParseModelNameRecognizesOnlyQuickToggleModels(
        string value,
        int expected)
    {
        Assert.Equal(
            (CodexQuickModel)expected,
            CodexModelToggleService.ParseModelName(value));
    }

    [Theory]
    [InlineData(1, 3)]
    [InlineData(3, 1)]
    [InlineData(0, 1)]
    public void ResolveToggleTargetTogglesConfiguredPair(
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
}
