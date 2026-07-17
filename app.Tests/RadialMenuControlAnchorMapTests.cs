using CodexController.Controllers;
using CodexController.Models;
using CodexController.Presentation;

namespace CodexController.Tests;

public sealed class RadialMenuControlAnchorMapTests
{
    [Fact]
    public void AgentLayoutSwapsSideDirectionsWithViewAndMenu()
    {
        Assert.Collection(
            AgentRadialSlotLayout.Bindings,
            binding => AssertBinding(
                binding,
                LogicalInput.DPadUp,
                RadialMenuSlotPosition.Top),
            binding => AssertBinding(
                binding,
                LogicalInput.DPadRight,
                RadialMenuSlotPosition.CenterRight),
            binding => AssertBinding(
                binding,
                LogicalInput.DPadDown,
                RadialMenuSlotPosition.Bottom),
            binding => AssertBinding(
                binding,
                LogicalInput.DPadLeft,
                RadialMenuSlotPosition.CenterLeft),
            binding => AssertBinding(
                binding,
                LogicalInput.View,
                RadialMenuSlotPosition.Left),
            binding => AssertBinding(
                binding,
                LogicalInput.Menu,
                RadialMenuSlotPosition.Right));
    }

    [Theory]
    [InlineData(LogicalInput.DPadUp, ControllerControlAnchor.DPadUp)]
    [InlineData(LogicalInput.DPadRight, ControllerControlAnchor.DPadRight)]
    [InlineData(LogicalInput.DPadDown, ControllerControlAnchor.DPadDown)]
    [InlineData(LogicalInput.DPadLeft, ControllerControlAnchor.DPadLeft)]
    [InlineData(LogicalInput.View, ControllerControlAnchor.View)]
    [InlineData(LogicalInput.Menu, ControllerControlAnchor.Menu)]
    [InlineData(LogicalInput.FaceNorth, ControllerControlAnchor.FaceNorth)]
    [InlineData(LogicalInput.FaceEast, ControllerControlAnchor.FaceEast)]
    [InlineData(LogicalInput.FaceSouth, ControllerControlAnchor.FaceSouth)]
    [InlineData(LogicalInput.FaceWest, ControllerControlAnchor.FaceWest)]
    public void PhysicalInputsResolveToExactNamedAnchors(
        LogicalInput input,
        ControllerControlAnchor expected)
    {
        Assert.True(
            RadialMenuControlAnchorMap.TryResolve(
                input,
                out var actual));
        Assert.Equal(expected, actual);
    }

    [Fact]
    public void FourDPadDirectionsUseFourDistinctAnchors()
    {
        var anchors = new[]
        {
            Resolve(LogicalInput.DPadUp),
            Resolve(LogicalInput.DPadRight),
            Resolve(LogicalInput.DPadDown),
            Resolve(LogicalInput.DPadLeft),
        };

        Assert.Equal(4, anchors.Distinct().Count());
        Assert.DoesNotContain(
            ControllerControlAnchor.LeftStick,
            anchors);
        Assert.DoesNotContain(
            ControllerControlAnchor.RightStick,
            anchors);
    }

    private static ControllerControlAnchor Resolve(LogicalInput input)
    {
        Assert.True(
            RadialMenuControlAnchorMap.TryResolve(
                input,
                out var anchor));
        return anchor;
    }

    private static void AssertBinding(
        AgentRadialSlotBinding binding,
        LogicalInput expectedInput,
        RadialMenuSlotPosition expectedPosition)
    {
        Assert.Equal(expectedInput, binding.Input);
        Assert.Equal(expectedPosition, binding.Position);
    }
}
