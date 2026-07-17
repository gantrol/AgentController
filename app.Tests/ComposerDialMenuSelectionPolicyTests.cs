using CodexController.Services;

namespace CodexController.Tests;

public sealed class ComposerDialMenuSelectionPolicyTests
{
    [Fact]
    public void OnePickerSurfaceFlattensProjectRowsAndFooterActions()
    {
        var options = new[]
        {
            Option("project-a", "picker", "ai-keyboard", 0),
            Option(
                "project-b",
                "picker",
                "taote",
                1,
                isSelected: true),
            Option("project-c", "picker", "gork-analysis", 2),
            Option("new-project", "picker", "New project", 3),
            Option("no-project", "picker", "Don't work in a project", 4),
        };

        var target =
            ComposerDialMenuSelectionPolicy.ResolveTarget(
                options,
                "picker",
                previousOptionKey: null,
                delta: 1);

        Assert.NotNull(target);
        Assert.Equal("project-c", target.Value.OptionKey);
        Assert.Equal("gork-analysis", target.Value.Name);
    }

    [Fact]
    public void LogicalCursorSurvivesFocusReturningToSearchField()
    {
        var options = new[]
        {
            Option("a", "picker", "A", 0),
            Option("b", "picker", "B", 1),
            Option("c", "picker", "C", 2),
        };

        var target =
            ComposerDialMenuSelectionPolicy.ResolveTarget(
                options,
                "picker",
                previousOptionKey: "b",
                delta: 1);

        Assert.NotNull(target);
        Assert.Equal("c", target.Value.OptionKey);
    }

    [Fact]
    public void UiActiveDescendantOverridesAStaleLogicalCursor()
    {
        var options = new[]
        {
            Option("a", "picker", "A", 0),
            Option(
                "b",
                "picker",
                "B",
                1,
                isActiveDescendant: true),
            Option("c", "picker", "C", 2),
        };

        var target =
            ComposerDialMenuSelectionPolicy.ResolveTarget(
                options,
                "picker",
                previousOptionKey: "a",
                delta: 1);

        Assert.NotNull(target);
        Assert.Equal("c", target.Value.OptionKey);
    }

    [Fact]
    public void DuplicateLabelsAreTrackedByStableOptionKey()
    {
        var options = new[]
        {
            Option("model-fast", "picker", "Fast", 0),
            Option("speed-fast", "picker", "Fast", 1),
        };

        var target =
            ComposerDialMenuSelectionPolicy.ResolveTarget(
                options,
                "picker",
                previousOptionKey: "model-fast",
                delta: 1);

        Assert.NotNull(target);
        Assert.Equal("speed-fast", target.Value.OptionKey);
    }

    [Fact]
    public void NestedMenuUsesOwnershipTreeNotRightmostCoordinates()
    {
        var surfaces = new[]
        {
            Surface("root", parent: null, depth: 0, mount: 1),
            Surface("submenu-left", parent: "root", depth: 1, mount: 2),
        };

        var active =
            ComposerDialMenuSelectionPolicy.ResolveActiveSurface(
                surfaces,
                previousSurfaceKey: "root");

        Assert.Equal("submenu-left", active);
    }

    [Fact]
    public void UnownedFocusedPopupCannotStealDialSession()
    {
        var surfaces = new[]
        {
            Surface("owned", parent: null, depth: 0, mount: 1),
            Surface(
                "unrelated",
                parent: null,
                depth: 5,
                mount: 2,
                owned: false,
                containsFocus: true),
        };

        var active =
            ComposerDialMenuSelectionPolicy.ResolveActiveSurface(
                surfaces,
                previousSurfaceKey: "owned");

        Assert.Equal("owned", active);
    }

    [Fact]
    public void ConfirmationRequiresSameOwnedSurfaceAndStableOptionKey()
    {
        var target = new ComposerDialMenuTarget(
            "submenu",
            "effort-high",
            "High",
            2);
        var ownedSurface = new[]
        {
            Surface("submenu", parent: "root", depth: 1, mount: 2),
        };
        var movedOption = new[]
        {
            Option("effort-high", "other-menu", "High", 2),
        };

        Assert.False(
            ComposerDialMenuSelectionPolicy.IsTargetStillConfirmable(
                target,
                ownedSurface,
                movedOption));

        Assert.True(
            ComposerDialMenuSelectionPolicy.IsTargetStillConfirmable(
                target,
                ownedSurface,
                [
                    Option(
                        "effort-high",
                        "submenu",
                        "High",
                        2),
                ]));
    }

    [Theory]
    [InlineData("Model 5.6 Sol", "model-sol")]
    [InlineData("Effort Extra High", "extra-high")]
    [InlineData("Speed Fast", "fast")]
    public void NestedMenuStartsAtLongestParentSuffix(
        string parentName,
        string expectedKey)
    {
        var options = new[]
        {
            Option("sol", "submenu", "Sol", 0),
            Option("model-sol", "submenu", "5.6 Sol", 1),
            Option("extra-high", "submenu", "Extra High", 2),
            Option("fast", "submenu", "Fast", 3),
        };

        var target =
            ComposerDialMenuSelectionPolicy.ResolveInitialTarget(
                options,
                "submenu",
                parentName);

        Assert.NotNull(target);
        Assert.Equal(expectedKey, target.Value.OptionKey);
    }

    [Fact]
    public void RootComposerMenuCanExplicitlyStartAtFirstItem()
    {
        var options = new[]
        {
            Option("model", "root", "Model", 0),
            Option(
                "effort",
                "root",
                "Effort",
                1,
                isSelected: true),
            Option("speed", "root", "Speed", 2),
        };

        var target =
            ComposerDialMenuSelectionPolicy.ResolveInitialTarget(
                options,
                "root",
                preferFirst: true);

        Assert.NotNull(target);
        Assert.Equal("model", target.Value.OptionKey);
    }

    [Fact]
    public void CurrentSelectedProjectIsPreferredInDirectPicker()
    {
        var options = new[]
        {
            Option("project-a", "picker", "A", 0),
            Option(
                "project-b",
                "picker",
                "B",
                1,
                isSelected: true),
        };

        var target =
            ComposerDialMenuSelectionPolicy.ResolveInitialTarget(
                options,
                "picker");

        Assert.NotNull(target);
        Assert.Equal("project-b", target.Value.OptionKey);
    }

    [Fact]
    public void AlignedProjectListAndFooterShareVisualSurface()
    {
        var list = new ComposerDialMenuContainerGeometry(
            100,
            100,
            360,
            420);
        var footer = new ComposerDialMenuContainerGeometry(
            100,
            528,
            360,
            92);
        var leftSubmenu = new ComposerDialMenuContainerGeometry(
            -270,
            160,
            360,
            260);

        Assert.True(
            ComposerDialMenuSelectionPolicy.SharesVisualSurface(
                list,
                footer));
        Assert.False(
            ComposerDialMenuSelectionPolicy.SharesVisualSurface(
                list,
                leftSubmenu));
    }

    [Fact]
    public void ComposerTriggerKeepsRootAndNestedMenuButRejectsProfilePopup()
    {
        var surfaces = new[]
        {
            SurfaceGeometry(
                "root",
                left: 1462,
                top: 1097,
                width: 379,
                height: 236),
            SurfaceGeometry(
                "submenu",
                left: 1853,
                top: 1072,
                width: 477,
                height: 351),
            SurfaceGeometry(
                "profile",
                left: 2960,
                top: 90,
                width: 420,
                height: 520),
        };
        var triggers = new[]
        {
            Geometry(
                left: 1622,
                top: 1348,
                width: 213,
                height: 49),
        };

        var associated =
            ComposerDialMenuSelectionPolicy
                .ResolveAssociatedSurfaceKeys(
                    surfaces,
                    triggers);

        Assert.Contains("root", associated);
        Assert.Contains("submenu", associated);
        Assert.DoesNotContain("profile", associated);
    }

    [Fact]
    public void UnrelatedPopupIsNotAdoptedWithoutNearbyComposerControl()
    {
        var associated =
            ComposerDialMenuSelectionPolicy
                .ResolveAssociatedSurfaceKeys(
                    [
                        SurfaceGeometry(
                            "usage-menu",
                            left: 2960,
                            top: 90,
                            width: 420,
                            height: 520),
                    ],
                    [
                        Geometry(
                            left: 1622,
                            top: 1348,
                            width: 213,
                            height: 49),
                    ]);

        Assert.Empty(associated);
    }

    [Fact]
    public void FullAccessCheckmarkIsASelectionMarker()
    {
        var item = Geometry(
            left: 1366,
            top: 1255,
            width: 783,
            height: 83);
        var check = Geometry(
            left: 2106,
            top: 1280,
            width: 29,
            height: 29);

        Assert.True(
            ComposerDialMenuSelectionPolicy
                .LooksLikeSelectionMarker(
                    item,
                    check,
                    "icon-xs text-token-editor-warning-foreground"));
    }

    [Fact]
    public void SubmenuChevronAndLeadingIconAreNotSelectionMarkers()
    {
        var item = Geometry(
            left: 1461,
            top: 1104,
            width: 379,
            height: 51);
        var chevron = Geometry(
            left: 1800,
            top: 1115,
            width: 29,
            height: 29);
        var leadingIcon = Geometry(
            left: 1378,
            top: 1095,
            width: 33,
            height: 33);

        Assert.False(
            ComposerDialMenuSelectionPolicy
                .LooksLikeSelectionMarker(
                    item,
                    chevron,
                    "icon-xs text-token-input-placeholder-foreground"));
        Assert.False(
            ComposerDialMenuSelectionPolicy
                .LooksLikeSelectionMarker(
                    item,
                    leadingIcon,
                    "icon-sm text-token-editor-warning-foreground"));
    }

    private static ComposerDialMenuSurfaceSnapshot Surface(
        string key,
        string? parent,
        int depth,
        long mount,
        bool owned = true,
        bool containsFocus = false,
        bool containsActive = false)
    {
        return new(
            key,
            parent,
            depth,
            mount,
            owned,
            containsFocus,
            containsActive);
    }

    private static ComposerDialMenuOptionSnapshot Option(
        string key,
        string surface,
        string name,
        int visualOrder,
        bool hasFocus = false,
        bool isActiveDescendant = false,
        bool isSelected = false)
    {
        return new(
            key,
            surface,
            name,
            visualOrder,
            hasFocus,
            isActiveDescendant,
            isSelected);
    }

    private static ComposerDialMenuSurfaceGeometrySnapshot
        SurfaceGeometry(
            string key,
            double left,
            double top,
            double width,
            double height)
    {
        return new(
            key,
            Geometry(left, top, width, height));
    }

    private static ComposerDialMenuContainerGeometry Geometry(
        double left,
        double top,
        double width,
        double height)
    {
        return new(left, top, width, height);
    }
}
