using CodexMicro.Desktop.Services;
using Xunit;

namespace CodexMicro.Desktop.Tests;

public sealed class CodexQuotaServiceTests
{
    [Fact]
    public void ParsesQuotaWindowsAndChoosesTheTighterRemainingLimit()
    {
        const string response =
            """
            {
              "id": 2,
              "result": {
                "rateLimits": {
                  "limitId": "codex",
                  "primary": {
                    "usedPercent": 35.4,
                    "windowDurationMins": 300,
                    "resetsAt": 1786572000
                  },
                  "secondary": {
                    "usedPercent": 82,
                    "windowDurationMins": 10080,
                    "resetsAt": 1787176800
                  },
                  "planType": "pro"
                }
              }
            }
            """;
        var readAt = new DateTimeOffset(
            2026,
            8,
            12,
            20,
            0,
            0,
            TimeSpan.Zero);

        var snapshot = Assert.IsType<CodexQuotaSnapshot>(
            CodexQuotaService.Parse(response, readAt));

        Assert.Equal("pro", snapshot.PlanType);
        Assert.Equal(readAt, snapshot.ReadAt);
        Assert.Equal(2, snapshot.Windows.Count);
        Assert.Equal(64.6, snapshot.Primary.RemainingPercent, 3);
        Assert.NotNull(snapshot.Secondary);
        Assert.Equal(18, snapshot.Secondary.RemainingPercent, 3);
        Assert.Same(snapshot.Secondary, snapshot.DisplayWindow);
    }

    [Fact]
    public void FallsBackToTheCodexMultiBucketView()
    {
        const string response =
            """
            {
              "id": 2,
              "result": {
                "rateLimitsByLimitId": {
                  "codex": {
                    "primary": {
                      "usedPercent": 1,
                      "windowDurationMins": 10080,
                      "resetsAt": 1787196677
                    },
                    "secondary": null
                  },
                  "codex_other": {
                    "primary": {
                      "usedPercent": 99,
                      "windowDurationMins": 60,
                      "resetsAt": 1787190000
                    }
                  }
                }
              }
            }
            """;

        var snapshot = Assert.IsType<CodexQuotaSnapshot>(
            CodexQuotaService.Parse(response));

        Assert.Equal(99, snapshot.DisplayWindow.RemainingPercent, 3);
        Assert.Equal(10080, snapshot.DisplayWindow.WindowDurationMinutes);
        Assert.Null(snapshot.Secondary);
    }

    [Fact]
    public void ClampsOutOfRangeUsageWithoutInventingMoreThanFullQuota()
    {
        const string response =
            """
            {
              "result": {
                "rateLimits": {
                  "primary": {
                    "usedPercent": 125,
                    "windowDurationMins": 300,
                    "resetsAt": 1786572000
                  },
                  "secondary": {
                    "usedPercent": -4,
                    "windowDurationMins": 10080,
                    "resetsAt": 1787176800
                  }
                }
              }
            }
            """;

        var snapshot = Assert.IsType<CodexQuotaSnapshot>(
            CodexQuotaService.Parse(response));

        Assert.Equal(0, snapshot.Primary.RemainingPercent, 3);
        Assert.Equal(100, snapshot.Secondary!.RemainingPercent, 3);
        Assert.Same(snapshot.Primary, snapshot.DisplayWindow);
    }

    [Theory]
    [InlineData("""{"id":2,"result":{}}""")]
    [InlineData(
        """{"id":2,"result":{"rateLimits":{"primary":null}}}""")]
    [InlineData(
        """{"id":2,"result":{"rateLimits":{"primary":{"usedPercent":20,"windowDurationMins":0,"resetsAt":1786572000}}}}""")]
    public void MissingOrInvalidPrimaryWindowReturnsNoSnapshot(string response)
    {
        Assert.Null(CodexQuotaService.Parse(response));
    }

    [Fact]
    public void ResolvesTheInstalledCodexExecutableWhenAvailable()
    {
        var executable = CodexQuotaService.ResolveCodexExecutable();

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
