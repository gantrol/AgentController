using CodexController.Services;

namespace CodexController.Tests;

public sealed class ComposerPickerVisualOrderPolicyTests
{
    [Theory]
    [InlineData(2, 5, 1, 1)]
    [InlineData(2, 5, -1, 3)]
    [InlineData(0, 5, 1, 0)]
    [InlineData(4, 5, -1, 4)]
    [InlineData(2, 5, 0, 2)]
    public void FollowsTopToBottomScreenOrder(
        int currentIndex,
        int optionCount,
        int verticalDirection,
        int expected)
    {
        Assert.Equal(
            expected,
            ComposerPickerVisualOrderPolicy.ResolveNextIndex(
                currentIndex,
                optionCount,
                verticalDirection));
    }

    [Fact]
    public void EmptyListHasNoIndex()
    {
        Assert.Equal(
            -1,
            ComposerPickerVisualOrderPolicy.ResolveNextIndex(
                currentIndex: 0,
                optionCount: 0,
                verticalDirection: 1));
    }
}
