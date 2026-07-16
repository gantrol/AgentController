using CodexController.Controllers;

namespace CodexController.Tests;

public sealed class StickTuningTests
{
    [Theory]
    [MemberData(nameof(NonFiniteValues))]
    public void RejectsNonFiniteStickDeadZone(double deadZone)
    {
        var exception = Assert.Throws<ArgumentOutOfRangeException>(
            () => new StickTuning(
                stickDeadZone: deadZone));

        Assert.Equal("stickDeadZone", exception.ParamName);
    }

    [Theory]
    [MemberData(nameof(NonFiniteValues))]
    public void RejectsNonFiniteTriggerDeadZone(double deadZone)
    {
        var exception = Assert.Throws<ArgumentOutOfRangeException>(
            () => new StickTuning(
                triggerDeadZone: deadZone));

        Assert.Equal("triggerDeadZone", exception.ParamName);
    }

    [Theory]
    [InlineData(0.0, 0.0)]
    [InlineData(0.24, 0.05)]
    [InlineData(0.999, 0.999)]
    public void AcceptsFiniteDeadZonesWithinRange(
        double stickDeadZone,
        double triggerDeadZone)
    {
        var tuning = new StickTuning(
            stickDeadZone,
            triggerDeadZone);

        Assert.Equal(stickDeadZone, tuning.StickDeadZone);
        Assert.Equal(triggerDeadZone, tuning.TriggerDeadZone);
    }

    public static IEnumerable<object[]> NonFiniteValues
    {
        get
        {
            yield return [double.NaN];
            yield return [double.PositiveInfinity];
            yield return [double.NegativeInfinity];
        }
    }
}
