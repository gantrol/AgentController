using CodexMicro.Desktop.Services;
using Xunit;

namespace CodexMicro.Desktop.Tests;

public sealed class EncoderStepAccumulatorTests
{
    [Fact]
    public void PendingHistoryIsBounded()
    {
        var accumulator = new EncoderStepAccumulator(3);

        accumulator.Add(20, inputTimestamp: 100);

        Assert.Equal(3, accumulator.Pending);
        Assert.Equal(
            1,
            accumulator.TakeNext(currentTimestamp: 100, maximumAgeTicks: 50)?.Direction);
        Assert.Equal(2, accumulator.Pending);
    }

    [Fact]
    public void OppositeInputCancelsQueuedHistory()
    {
        var accumulator = new EncoderStepAccumulator(3);
        accumulator.Add(3, inputTimestamp: 100);

        accumulator.Add(-2, inputTimestamp: 110);

        Assert.Equal(1, accumulator.Pending);
        Assert.Equal(
            1,
            accumulator.TakeNext(currentTimestamp: 110, maximumAgeTicks: 50)?.Direction);
        Assert.Equal(0, accumulator.Pending);
    }

    [Fact]
    public void ClearDropsStaleStepsBeforeConfirmation()
    {
        var accumulator = new EncoderStepAccumulator();
        accumulator.Add(-3, inputTimestamp: 100);

        accumulator.Clear();

        Assert.Null(accumulator.TakeNext(currentTimestamp: 100, maximumAgeTicks: 50));
    }

    [Fact]
    public void StalePendingIntentIsDiscardedInsteadOfReplayed()
    {
        var accumulator = new EncoderStepAccumulator(3);
        accumulator.Add(3, inputTimestamp: 100);

        var result = accumulator.TakeNext(
            currentTimestamp: 201,
            maximumAgeTicks: 100);

        Assert.Null(result);
        Assert.Equal(0, accumulator.Pending);
    }
}
