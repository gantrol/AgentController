using CodexController.Services;

namespace CodexController.Tests;

public sealed class ComposerShortcutHealthTests
{
    [Fact]
    public void AttemptsByDefaultWithoutProvenState()
    {
        var health = new ComposerShortcutHealth(() => 0);

        Assert.True(health.ShouldAttempt());
        Assert.False(health.IsProven);
    }

    [Fact]
    public void VerifiedSuccessProvesTheShortcut()
    {
        var health = new ComposerShortcutHealth(() => 0);

        health.MarkWorking();

        Assert.True(health.IsProven);
        Assert.True(health.ShouldAttempt());
    }

    [Fact]
    public void SuspectPausesAttemptsForTheCooldownOnly()
    {
        var now = 1_000L;
        var health = new ComposerShortcutHealth(() => now);

        health.MarkSuspect();

        Assert.False(health.ShouldAttempt());
        now +=
            (long)ComposerShortcutHealth.SuspectCooldown
                .TotalMilliseconds - 1;
        Assert.False(health.ShouldAttempt());
        now += 1;
        Assert.True(health.ShouldAttempt());
    }

    [Fact]
    public void SuspectClearsProvenState()
    {
        var health = new ComposerShortcutHealth(() => 0);

        health.MarkWorking();
        health.MarkSuspect();

        Assert.False(health.IsProven);
    }

    [Fact]
    public void WorkingClearsAnActiveCooldown()
    {
        var now = 1_000L;
        var health = new ComposerShortcutHealth(() => now);

        health.MarkSuspect();
        health.MarkWorking();

        Assert.True(health.ShouldAttempt());
        Assert.True(health.IsProven);
    }
}
