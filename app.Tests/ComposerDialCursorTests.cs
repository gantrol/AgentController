using CodexController.Services;

namespace CodexController.Tests;

public sealed class ComposerDialCursorTests
{
    [Fact]
    public void FirstStepChoosesNearestEndForDirection()
    {
        var cursor = new ComposerDialCursor();
        string[] keys = ["project", "access", "model"];

        Assert.Equal(0, cursor.Move(keys, 1));

        cursor.Reset();

        Assert.Equal(2, cursor.Move(keys, -1));
    }

    [Fact]
    public void StepsWrapAndRemainAnchoredByStableKey()
    {
        var cursor = new ComposerDialCursor();
        string[] original = ["project", "access", "model"];

        Assert.Equal(0, cursor.Move(original, 1));
        Assert.Equal(1, cursor.Move(original, 1));

        string[] refreshed = ["project", "access", "model-renamed"];
        Assert.Equal(2, cursor.Move(refreshed, 1));
        Assert.Equal(0, cursor.Move(refreshed, 1));
    }

    [Fact]
    public void MissingSelectionRestartsWithoutUsingStaleIndex()
    {
        var cursor = new ComposerDialCursor();
        string[] original = ["project", "access", "model"];
        _ = cursor.Move(original, 1);
        _ = cursor.Move(original, 1);

        string[] replaced = ["workspace", "model"];

        Assert.Equal(0, cursor.Move(replaced, 1));
        Assert.Equal("workspace", cursor.SelectedKey);
    }
}
