using CodexController.Services;

namespace CodexController.Tests;

public sealed class CodexRateLimitResetServiceTests
{
    [Fact]
    public void ParsesOnlyAvailableFullResetsInExpirationOrder()
    {
        const string response =
            """
            {
              "id": 2,
              "result": {
                "rateLimitResetCredits": {
                  "availableCount": 2,
                  "credits": [
                    {
                      "status": "available",
                      "grantedAt": 1783963725,
                      "expiresAt": 1786555725,
                      "title": "Full reset"
                    },
                    {
                      "status": "used",
                      "grantedAt": 1782932592,
                      "expiresAt": 1785524592,
                      "title": "Full reset"
                    },
                    {
                      "status": "available",
                      "grantedAt": 1782932592,
                      "expiresAt": 1785524592,
                      "title": "Full reset"
                    },
                    {
                      "status": "available",
                      "grantedAt": 1782932592,
                      "expiresAt": 1785524592,
                      "title": "Other credit"
                    }
                  ]
                }
              }
            }
            """;

        var credits = CodexRateLimitResetService
            .ParseAvailableFullResetCredits(response);

        Assert.Collection(
            credits,
            first => Assert.Equal(
                1785524592,
                first.ExpiresAt.ToUnixTimeSeconds()),
            second => Assert.Equal(
                1786555725,
                second.ExpiresAt.ToUnixTimeSeconds()));
    }

    [Theory]
    [InlineData("""{"id":2,"result":{}}""")]
    [InlineData(
        """{"id":2,"result":{"rateLimitResetCredits":{"credits":[]}}}""")]
    public void MissingOrEmptyCreditsReturnNoResults(string response)
    {
        Assert.Empty(CodexRateLimitResetService
            .ParseAvailableFullResetCredits(response));
    }

    [Fact]
    public void ResolvesTheInstalledCodexExecutableWhenAvailable()
    {
        var executable =
            CodexRateLimitResetService.ResolveCodexExecutable();

        Assert.EndsWith(
            "codex.exe",
            executable,
            StringComparison.OrdinalIgnoreCase);
        if (Path.IsPathRooted(executable))
        {
            Assert.True(File.Exists(executable));
        }
    }
}
