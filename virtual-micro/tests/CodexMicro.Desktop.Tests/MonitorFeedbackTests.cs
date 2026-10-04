using System.IO;
using CodexController.Models;
using CodexController.Services;
using CodexMicro.Desktop.Services;
using Xunit;

namespace CodexMicro.Desktop.Tests;

public sealed class MonitorFeedbackTests
{
    [Fact]
    public async Task ConfirmedUnreadSurvivesAnOlderReadAndQuestionPollButAcceptsALaterRead()
    {
        var id = Guid.NewGuid().ToString();
        var stale = new TaskCompletionSource<CodexUnreadStateSnapshot?>(TaskCreationOptions.RunContinuationsAsynchronously);
        var reads = 0;
        var monitor = new CodexTaskMonitorService(
            codexRoot: Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString()),
            readThreads: _ => Task.FromResult<IReadOnlyList<CodexRecentThread>?>(
                [new(id, "Idle chat", null, DateTimeOffset.UtcNow)]),
            readUnread: _ => ++reads == 2 ? stale.Task :
                Task.FromResult<CodexUnreadStateSnapshot?>(new(new HashSet<string>(), null)));
        await monitor.ReadAsync(CancellationToken.None);
        var oldRead = monitor.ReadAsync(CancellationToken.None);
        Assert.False(oldRead.IsCompleted);
        Assert.Equal(ThreadStatus.CompleteUnread, Assert.Single(monitor.ObserveUnreadConfirmed(id)!.Tasks).Status);
        stale.SetResult(new(new HashSet<string>(), null));
        Assert.Equal(ThreadStatus.CompleteUnread, Assert.Single((await oldRead)!.Tasks).Status);
        Assert.Equal(ThreadStatus.CompleteUnread, Assert.Single(
            (await monitor.ReadPendingQuestionsAsync(CancellationToken.None))!.Tasks).Status);
        Assert.Equal(ThreadStatus.Unknown, Assert.Single(
            (await monitor.ReadAsync(CancellationToken.None))!.Tasks).Status);
    }

    [Fact]
    public void AnsweringOneOfTwoQuestionsKeepsYellowUntilTheOtherIsResolved()
    {
        var path = Path.GetTempFileName();
        try
        {
            File.WriteAllText(path,
                """{"type":"event_msg","payload":{"type":"task_started","turn_id":"t"}}""" + "\n" +
                """{"type":"event_msg","payload":{"type":"item_completed","turn_id":"t","item":{"type":"AgentMessage","id":"q","delivery":"async","questions":[{"title":"First"},{"title":"Second"}]}}}""" + "\n");
            var reader = new CodexRolloutStatusReader();
            Assert.True(reader.ReadSnapshot(path).HasPendingQuestion);
            Assert.True(reader.ReadSnapshot(path, [
                """<send_user_message_question_reply>[{"questionItemId":"[\"request_user_input_async\",\"q\",0]","answer":"Yes"}]</send_user_message_question_reply>"""
            ]).HasPendingQuestion);
            var remaining = Assert.Single(reader.GetPendingQuestions(path));
            Assert.Equal(1, remaining.Index);
            reader.ObserveSkippedQuestion(path, remaining);
            var resolved = reader.ReadSnapshot(path);
            Assert.False(resolved.HasPendingQuestion);
            Assert.Equal(ThreadStatus.Thinking, resolved.Status);
        }
        finally { File.Delete(path); }
    }
}
