using CodexController.Agents;

namespace CodexController.Tests;

public sealed class SidebarNavigationSafetyTests
{
    [Fact]
    public void SidebarCapabilityCannotFocusOrActivateNativeEntries()
    {
        var methodNames = typeof(ISidebarAutomation)
            .GetMethods()
            .Select(method => method.Name)
            .ToArray();

        Assert.DoesNotContain("FocusEntry", methodNames);
        Assert.DoesNotContain("SetFocus", methodNames);
        Assert.DoesNotContain("OpenThread", methodNames);
    }
}
