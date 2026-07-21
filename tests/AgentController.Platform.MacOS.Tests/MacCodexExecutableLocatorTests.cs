using AgentController.Platform.MacOS.Codex;

namespace AgentController.Platform.MacOS.Tests;

public sealed class MacCodexExecutableLocatorTests
{
    [Fact]
    public void FindsCodexFromPathWithoutStartingIt()
    {
        var bin = Path.Combine(Path.DirectorySeparatorChar.ToString(), "tools");
        var expected = Path.Combine(bin, "codex");

        var result = MacCodexExecutableLocator.Locate(
            bin,
            homeDirectory: null,
            path => string.Equals(path, expected, StringComparison.Ordinal));

        Assert.True(result.IsFound);
        Assert.Equal(expected, result.ExecutablePath);
    }

    [Fact]
    public void IncludesHomebrewAndUserInstallLocations()
    {
        var home = Path.Combine(
            Path.DirectorySeparatorChar.ToString(),
            "Users",
            "preview");
        var expected = Path.Combine(home, ".local", "bin", "codex");

        var result = MacCodexExecutableLocator.Locate(
            pathEnvironment: null,
            home,
            path => string.Equals(path, expected, StringComparison.Ordinal));

        Assert.True(result.IsFound);
        Assert.Equal(expected, result.ExecutablePath);
    }
}
