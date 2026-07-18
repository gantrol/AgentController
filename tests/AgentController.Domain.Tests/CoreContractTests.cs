using AgentController.Domain.Actions;
using AgentController.Domain.Inputs;
using AgentController.Domain.Observations;
using Xunit;

namespace AgentController.Domain.Tests;

public sealed class CoreContractTests
{
    [Fact]
    public void DynamicIdentifiersAreCanonicalized()
    {
        var control = ControlId.Parse(" Controller.Face.South ");
        var action = ActionId.Parse("Composer.Send");
        var context = InputContext.Parse("Composer.Simple");

        Assert.Equal("controller.face.south", control.Value);
        Assert.Equal("composer.send", action.Value);
        Assert.Equal("composer.simple", context.Value);
    }

    [Theory]
    [InlineData("")]
    [InlineData("has space")]
    [InlineData(".starts-with-punctuation")]
    public void InvalidControlIdentifiersAreRejected(string value)
    {
        Assert.False(ControlId.TryParse(value, out _));
    }

    [Fact]
    public void ChordRequiresDistinctDefinedControls()
    {
        var control = ControlId.Parse("controller.face.south");

        Assert.Throws<ArgumentException>(() => Gesture.Chord([control, control]));
    }

    [Fact]
    public void ChordOrderIsCanonical()
    {
        var south = ControlId.Parse("controller.face.south");
        var north = ControlId.Parse("controller.face.north");

        var gesture = Gesture.Chord([south, north]);

        Assert.Equal(
            ["controller.face.north", "controller.face.south"],
            gesture.Controls.Select(control => control.Value));
    }

    [Fact]
    public void HoldAndAxisStepRejectNonActions()
    {
        var control = ControlId.Parse("controller.right-stick");

        Assert.Throws<ArgumentOutOfRangeException>(() =>
            Gesture.Hold(control, TimeSpan.Zero));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            Gesture.AxisStep(control, 0));
    }

    [Fact]
    public void BindingRuleKeepsDynamicInputAndActionIds()
    {
        var rule = new BindingRule(
            "default.send",
            Gesture.Press(ControlId.Parse("controller.face.west")),
            InputContext.Parse("composer.ready"),
            ActionId.Parse("composer.send"),
            priority: 100);

        Assert.Equal("default.send", rule.Id);
        Assert.Equal(100, rule.Priority);
        Assert.Equal("composer.send", rule.ActionId.Value);
    }

    [Fact]
    public void ActionRequestCopiesParameterState()
    {
        var parameters = new Dictionary<string, string>
        {
            ["model"] = "sol",
        };
        var request = CreateRequest(parameters);

        parameters["model"] = "changed";

        Assert.Equal("sol", request.Parameters["model"]);
    }

    [Fact]
    public void ActionOutcomeContainsEveryRequiredTerminalState()
    {
        Assert.Equal(
            [
                "Succeeded",
                "NotSent",
                "AcceptedUnverified",
                "Unsupported",
                "Incompatible",
                "Blocked",
                "Failed",
            ],
            Enum.GetNames<ActionOutcome>());
    }

    [Fact]
    public void ResultPreservesExecutorAndTypedEvidence()
    {
        var request = CreateRequest();
        var evidence = new ActionEvidence(
            ActionEvidenceKind.State,
            "codex.app-server",
            "turn.started",
            DateTimeOffset.UtcNow,
            1);

        var result = new ActionResult(
            request.RequestId,
            request.ActionId,
            ActionOutcome.Succeeded,
            "codex.app-server",
            DateTimeOffset.UtcNow,
            [evidence]);

        Assert.Equal("codex.app-server", result.ExecutorId);
        Assert.Same(evidence, Assert.Single(result.Evidence));
    }

    [Theory]
    [InlineData(-0.01)]
    [InlineData(1.01)]
    [InlineData(double.NaN)]
    public void EvidenceRejectsInvalidConfidence(double confidence)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new ActionEvidence(
            ActionEvidenceKind.Transport,
            "micro.usb",
            "report.sent",
            DateTimeOffset.UtcNow,
            confidence));
    }

    [Fact]
    public void StateObservationCarriesEpochSequenceTimeAndConfidence()
    {
        var observedAt = DateTimeOffset.UtcNow;
        var observation = new StateObservation<string>(
            "running",
            "codex.app-server",
            epoch: 4,
            sequence: 19,
            observedAt,
            confidence: 1);

        Assert.Equal(4, observation.Epoch);
        Assert.Equal(19, observation.Sequence);
        Assert.Equal(observedAt, observation.ObservedAt);
        Assert.Equal(1, observation.Confidence);
    }

    private static ActionRequest CreateRequest(
        IReadOnlyDictionary<string, string>? parameters = null) =>
        new(
            Guid.NewGuid(),
            ActionId.Parse("composer.send"),
            new ActionSource(
                "xinput.0",
                ControlId.Parse("controller.face.west")),
            InputContext.Parse("composer.ready"),
            $"test-{Guid.NewGuid():N}",
            ActionSafetyLevel.Routine,
            DateTimeOffset.UtcNow,
            parameters);
}
