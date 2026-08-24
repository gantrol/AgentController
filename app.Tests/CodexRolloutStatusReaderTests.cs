using System.Text.Json;
using CodexController.Models;
using CodexController.Services;

namespace CodexController.Tests;

public sealed class CodexRolloutStatusReaderTests
{
    [Fact]
    public void LifecycleTransitionsAreReadIncrementally()
    {
        var path = Path.GetTempFileName();
        try
        {
            var reader = new CodexRolloutStatusReader();
            Assert.Equal(ThreadStatus.Unknown, reader.Read(path));

            AppendEvent(path, "task_started");
            Assert.Equal(ThreadStatus.Thinking, reader.Read(path));

            AppendEvent(path, "task_complete");
            Assert.Equal(ThreadStatus.Idle, reader.Read(path));

            AppendEvent(path, "task_started");
            AppendEvent(path, "stream_error");
            Assert.Equal(ThreadStatus.Error, reader.Read(path));
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void PartialTrailingRecordDoesNotChangeStatusEarly()
    {
        var path = Path.GetTempFileName();
        try
        {
            var reader = new CodexRolloutStatusReader();
            File.AppendAllText(
                path,
                "{\"type\":\"event_msg\",\"payload\":{\"type\":\"task_started\"}}");

            Assert.Equal(ThreadStatus.Unknown, reader.Read(path));

            File.AppendAllText(path, "\n");
            Assert.Equal(ThreadStatus.Thinking, reader.Read(path));
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void TruncatedRolloutResetsCachedState()
    {
        var path = Path.GetTempFileName();
        try
        {
            var reader = new CodexRolloutStatusReader();
            AppendEvent(path, "task_started");
            Assert.Equal(ThreadStatus.Thinking, reader.Read(path));

            File.WriteAllText(path, string.Empty);
            Assert.Equal(ThreadStatus.Unknown, reader.Read(path));
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void MessageTextCannotSpoofLifecycle()
    {
        var path = Path.GetTempFileName();
        try
        {
            File.WriteAllText(
                path,
                JsonSerializer.Serialize(new
                {
                    type = "event_msg",
                    payload = new
                    {
                        type = "user_message",
                        text = "task_started and stream_error",
                    },
                }) + "\n");

            Assert.Equal(
                ThreadStatus.Unknown,
                new CodexRolloutStatusReader().Read(path));
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void Utf8ScalarSplitAcrossReadsIsPreserved()
    {
        var path = Path.GetTempFileName();
        try
        {
            var reader = new CodexRolloutStatusReader();
            var line = JsonSerializer.Serialize(new
            {
                type = "event_msg",
                payload = new
                {
                    type = "task_started",
                    note = "模型",
                },
            }) + "\n";
            var bytes = System.Text.Encoding.UTF8.GetBytes(line);
            var split = Array.IndexOf(bytes, (byte)0xE6) + 1;

            using (var stream = new FileStream(path, FileMode.Append))
            {
                stream.Write(bytes, 0, split);
            }

            Assert.Equal(ThreadStatus.Unknown, reader.Read(path));

            using (var stream = new FileStream(path, FileMode.Append))
            {
                stream.Write(bytes, split, bytes.Length - split);
            }

            Assert.Equal(ThreadStatus.Thinking, reader.Read(path));
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void MultiMegabyteRecordUsesLinearMemoryAndPreservesNextEvent()
    {
        var path = Path.GetTempFileName();
        try
        {
            const int payloadBytes = 2 * 1024 * 1024;
            File.WriteAllText(
                path,
                "{\"type\":\"event_msg\",\"payload\":{" +
                "\"type\":\"user_message\",\"text\":\"" +
                new string('x', payloadBytes) +
                "\"}}\n");
            AppendEvent(path, "task_started");
            var reader = new CodexRolloutStatusReader();
            var allocatedBefore =
                GC.GetAllocatedBytesForCurrentThread();

            var status = reader.Read(path);

            var allocated =
                GC.GetAllocatedBytesForCurrentThread() - allocatedBefore;
            Assert.Equal(ThreadStatus.Thinking, status);
            // A regression to prefix + chunk concatenation allocates more
            // than 100 MB for this 2 MB line. Keep enough headroom for a cold
            // ArrayPool while enforcing linear rather than quadratic growth.
            Assert.InRange(allocated, 0, 32L * 1024 * 1024);
        }
        finally
        {
            File.Delete(path);
        }
    }

    private static void AppendEvent(string path, string type)
    {
        File.AppendAllText(
            path,
            JsonSerializer.Serialize(new
            {
                type = "event_msg",
                payload = new { type },
            }) + "\n");
    }
}
