using CodexMicro.Desktop.Services;
using Xunit;

namespace CodexMicro.Desktop.Tests;

public sealed class CodexCompatibilityResultTests
{
    [Theory]
    [InlineData(0, false, false)]
    [InlineData(1, true, false)]
    [InlineData(2, true, true)]
    public void DispositionControlsConnectionAndReviewState(
        int dispositionValue,
        bool isCompatible,
        bool isReviewed)
    {
        var disposition =
            (CodexCompatibilityDisposition)dispositionValue;
        var result = new CodexCompatibilityResult(
            disposition,
            "test-build",
            "test-fingerprint",
            "test-detail",
            PackageRoot: null);

        Assert.Equal(isCompatible, result.IsCompatible);
        Assert.Equal(isReviewed, result.IsReviewed);
    }
}
