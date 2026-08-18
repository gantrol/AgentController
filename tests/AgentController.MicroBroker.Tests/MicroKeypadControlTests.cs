using AgentController.MicroBroker;
using Xunit;

namespace AgentController.MicroBroker.Tests;

public sealed class MicroKeypadControlTests
{
    [Fact]
    public async Task SameUserPipeRoundTripsCommandAndInstanceIdentity()
    {
        var pipeName = $"CodexMicro.Keypad.Tests.{Guid.NewGuid():N}";
        MicroKeypadControlCommand? observed = null;
        var responseSent = new TaskCompletionSource<
            MicroKeypadControlCommand>(
                TaskCreationOptions.RunContinuationsAsynchronously);
        await using var server = new MicroKeypadControlServer(
            pipeName,
            (command, _) =>
            {
                observed = command;
                return Task.FromResult(new MicroKeypadControlResponse(
                    MicroKeypadControlClient.ProtocolVersion,
                    Accepted: true,
                    MicroKeypadControlState.Restarting,
                    InstanceId: "old-instance"));
            },
            (command, _) =>
            {
                responseSent.TrySetResult(command);
                return Task.CompletedTask;
            });
        server.Start();

        var response = await MicroKeypadControlClient.TrySendAsync(
            pipeName,
            MicroKeypadControlCommand.Restart,
            TimeSpan.FromSeconds(3));

        Assert.Equal(MicroKeypadControlCommand.Restart, observed);
        Assert.NotNull(response);
        Assert.True(response.Accepted);
        Assert.Equal(MicroKeypadControlState.Restarting, response.State);
        Assert.Equal("old-instance", response.InstanceId);
        Assert.Equal(
            MicroKeypadControlCommand.Restart,
            await responseSent.Task.WaitAsync(TimeSpan.FromSeconds(1)));
    }

    [Fact]
    public async Task MissingKeypadReturnsUnavailableInsteadOfThrowing()
    {
        var response = await MicroKeypadControlClient.TrySendAsync(
            $"CodexMicro.Keypad.Tests.{Guid.NewGuid():N}",
            MicroKeypadControlCommand.Ping,
            TimeSpan.FromMilliseconds(150));

        Assert.Null(response);
    }
}
