using CodexController.Services;

namespace CodexController.Tests;

public sealed class PlanModeAutomationPolicyTests
{
    [Theory]
    [InlineData(false, true)]
    [InlineData(true, false)]
    public void AcceptsOnlyAnObservedPlanStateTransition(
        bool before,
        bool after)
    {
        Assert.True(
            PlanModeAutomationPolicy.DidStateChange(before, after));
        Assert.False(
            PlanModeAutomationPolicy.DidStateChange(before, before));
    }

    [Theory]
    [InlineData("Plan")]
    [InlineData("计划")]
    [InlineData("規劃")]
    [InlineData("計劃")]
    public void RecognizesInstalledCodexPlanIndicatorLabels(string label)
    {
        Assert.Contains(
            label,
            PlanModeAutomationPolicy.IndicatorNames);
    }

    [Theory]
    [InlineData("Plan mode")]
    [InlineData("Plan mode Turn plan mode on")]
    [InlineData("计划模式 开启计划模式")]
    [InlineData("規劃模式 開啟規劃模式")]
    public void RecognizesPlanSlashCommandButtons(string accessibleName)
    {
        Assert.True(
            PlanModeAutomationPolicy.IsSlashCommand(accessibleName));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("Plan")]
    [InlineData("My Plan mode notes")]
    [InlineData("Planning mode")]
    public void RejectsUnrelatedPlanLabels(string? accessibleName)
    {
        Assert.False(
            PlanModeAutomationPolicy.IsSlashCommand(accessibleName));
    }

    [Theory]
    [InlineData("Stop")]
    [InlineData("Cancel request")]
    [InlineData("停止")]
    public void RecognizesRunningTurnActions(string accessibleName)
    {
        Assert.Contains(
            accessibleName,
            PlanModeAutomationPolicy.RunningActionNames);
    }
}
