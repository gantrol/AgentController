using CodexController.Services;

namespace CodexController.Tests;

public sealed class ComposerDialPolicyTests
{
    [Theory]
    [InlineData("h-token-button-composer")]
    [InlineData("rounded h-token-button-composer min-w-0")]
    [InlineData("h-token-button-composer-sm px-2")]
    public void ComposerClassRequiresAnExactCssToken(string className)
    {
        Assert.True(
            ComposerDialPolicy.HasComposerButtonClassToken(className));
    }

    [Theory]
    [InlineData("min-h-token-button-composer")]
    [InlineData("h-token-button-composerish")]
    [InlineData("prefix-h-token-button-composer-sm")]
    [InlineData("H-TOKEN-BUTTON-COMPOSER")]
    public void ComposerClassRejectsSubstringAndCaseLookalikes(
        string className)
    {
        Assert.False(
            ComposerDialPolicy.HasComposerButtonClassToken(className));
    }

    [Fact]
    public void InvokeFallbackAllowsLocalizedProjectPillWithoutNameMatching()
    {
        const string localizedProjectClass =
            "rounded-full h-token-button-composer-sm min-w-0";

        Assert.True(
            ComposerDialPolicy.IsConservativeInvokeTrigger(
                localizedProjectClass,
                isKeyboardFocusable: true));
    }

    [Theory]
    [InlineData(
        "h-token-button-composer aspect-square",
        true)]
    [InlineData(
        "h-token-button-composer px-2",
        true)]
    [InlineData(
        "h-token-button-composer-sm min-w-0",
        false)]
    [InlineData(
        "min-h-token-button-composer min-w-0",
        true)]
    public void InvokeFallbackRemainsConservative(
        string className,
        bool isKeyboardFocusable)
    {
        Assert.False(
            ComposerDialPolicy.IsConservativeInvokeTrigger(
                className,
                isKeyboardFocusable));
    }

    [Theory]
    [InlineData("Search projects", "", "", "")]
    [InlineData("搜索项目", "", "", "")]
    [InlineData("", "project-search-input", "", "")]
    [InlineData("", "", "", "筛选工作区")]
    public void SearchEditRecognitionSupportsLocalizedAndSemanticMetadata(
        string name,
        string automationId,
        string className,
        string helpText)
    {
        Assert.True(
            ComposerDialPolicy.LooksLikeSearchEdit(
                name,
                automationId,
                className,
                helpText));
    }

    [Fact]
    public void VisibleMenuKindsNearComposerArePopupEvidence()
    {
        foreach (
            var kind in new[]
            {
                ComposerDialPopupElementKind.Menu,
                ComposerDialPopupElementKind.MenuItem,
            })
        {
            Assert.True(
                ComposerDialPolicy.IsPopupEvidence(
                    Evidence(
                        kind,
                        isNearComposer: true)));
        }

        Assert.True(
            ComposerDialPolicy.IsPopupEvidence(
                Evidence(
                    ComposerDialPopupElementKind.ListBox,
                    isNearComposer: true,
                    supportsSelection: true)));
        Assert.True(
            ComposerDialPolicy.IsPopupEvidence(
                Evidence(
                    ComposerDialPopupElementKind.OptionItem,
                    isNearComposer: true,
                    hasPopupAncestor: true)));
    }

    [Fact]
    public void MarkdownListNearComposerIsNotAListBoxPopup()
    {
        Assert.False(
            ComposerDialPolicy.IsPopupEvidence(
                Evidence(
                    ComposerDialPopupElementKind.ListBox,
                    isNearComposer: true)));
    }

    [Fact]
    public void SearchEditNeedsPopupRelationshipOrFocus()
    {
        Assert.False(
            ComposerDialPolicy.IsPopupEvidence(
                Evidence(
                    ComposerDialPopupElementKind.Edit,
                    isNearComposer: true)));

        Assert.True(
            ComposerDialPolicy.IsPopupEvidence(
                Evidence(
                    ComposerDialPopupElementKind.Edit,
                    isNearComposer: true,
                    hasPopupAncestor: true)));

        Assert.True(
            ComposerDialPolicy.IsPopupEvidence(
                Evidence(
                    ComposerDialPopupElementKind.Edit,
                    hasKeyboardFocus: true)));
    }

    [Fact]
    public void PopupStateIncludesMountedContentsNotOnlyOpenFlag()
    {
        Assert.False(
            ComposerDialPolicy.IsSamePopupState(
                true,
                "project-list",
                true,
                "new-project-menu"));
        Assert.True(
            ComposerDialPolicy.IsSamePopupState(
                false,
                string.Empty,
                false,
                null));
    }

    [Theory]
    [InlineData("Search projects", "ai-keyboard", true)]
    [InlineData(null, "ai-keyboard", true)]
    [InlineData("ai-keyboard", "ai-keyboard", false)]
    [InlineData("ai-keyboard", null, false)]
    public void FocusedSelectionChangeMustBeObservable(
        string? before,
        string? after,
        bool expected)
    {
        Assert.Equal(
            expected,
            ComposerDialPolicy.HasFocusedSelectionChanged(before, after));
    }

    [Theory]
    [InlineData(4, -1, 1, 0)]
    [InlineData(4, -1, -1, 3)]
    [InlineData(4, 0, -1, 3)]
    [InlineData(4, 3, 1, 0)]
    [InlineData(0, -1, 1, -1)]
    public void VisualOptionNavigationIsDetentedAndWraps(
        int count,
        int current,
        int delta,
        int expected)
    {
        Assert.Equal(
            expected,
            ComposerDialPolicy.ResolveVisualOptionIndex(
                count,
                current,
                delta));
    }

    [Fact]
    public void ClosedProbeIsSuccessfulAndDoesNotLeakStaleSelection()
    {
        var closed = ComposerDialPolicy.CreateProbeResult(
            isOpen: false,
            focusedName: "stale project");
        var open = ComposerDialPolicy.CreateProbeResult(
            isOpen: true,
            focusedName: "新项目");

        Assert.True(closed.Succeeded);
        Assert.False(closed.IsMenuOpen);
        Assert.Null(closed.ControlName);
        Assert.Null(closed.Error);

        Assert.True(open.Succeeded);
        Assert.True(open.IsMenuOpen);
        Assert.Equal("新项目", open.ControlName);
    }

    private static ComposerDialPopupEvidence Evidence(
        ComposerDialPopupElementKind kind,
        bool isNearComposer = false,
        bool hasKeyboardFocus = false,
        bool isKeyboardFocusable = false,
        bool isInPopupWindow = false,
        bool hasPopupAncestor = false,
        bool isSemanticSearchEdit = false,
        bool supportsSelection = false)
    {
        return new(
            kind,
            IsEnabled: true,
            IsOffscreen: false,
            IsBoundsEmpty: false,
            IsNearComposer: isNearComposer,
            HasKeyboardFocus: hasKeyboardFocus,
            IsKeyboardFocusable: isKeyboardFocusable,
            IsInPopupWindow: isInPopupWindow,
            HasPopupAncestor: hasPopupAncestor,
            IsSemanticSearchEdit: isSemanticSearchEdit,
            SupportsSelection: supportsSelection);
    }
}
