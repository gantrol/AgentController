using System.IO;
using System.Text;
using System.Text.Json;
using CodexController.Models;

namespace CodexController.Services;

/// <summary>
/// Reads the append-only Codex rollout lifecycle without inspecting the UI.
/// This is the honest local fallback when the optional Virtual Micro status
/// observer is unavailable: it can distinguish an open turn, stopped, and failed,
/// but it deliberately does not invent Codex's private unread/approval state.
/// </summary>
public sealed class CodexRolloutStatusReader
{
    private const int ReadBufferSize = 32 * 1024;

    private readonly object _sync = new();
    private readonly Dictionary<string, RolloutCursor> _cursors =
        new(StringComparer.OrdinalIgnoreCase);

    public ThreadStatus Read(string? rolloutPath)
    {
        if (string.IsNullOrWhiteSpace(rolloutPath))
        {
            return ThreadStatus.Unknown;
        }

        lock (_sync)
        {
            if (!_cursors.TryGetValue(rolloutPath, out var cursor))
            {
                cursor = new RolloutCursor();
                _cursors[rolloutPath] = cursor;
            }

            try
            {
                using var stream = new FileStream(
                    rolloutPath,
                    FileMode.Open,
                    FileAccess.Read,
                    FileShare.ReadWrite | FileShare.Delete);
                if (stream.Length < cursor.Offset)
                {
                    cursor.Reset();
                }

                var endOffset = stream.Length;
                if (endOffset == cursor.Offset)
                {
                    return cursor.Status;
                }

                stream.Position = cursor.Offset;
                var bytes = new byte[ReadBufferSize];
                var characters = new char[
                    Encoding.UTF8.GetMaxCharCount(ReadBufferSize)];
                while (cursor.Offset < endOffset)
                {
                    var requested = (int)Math.Min(
                        bytes.Length,
                        endOffset - cursor.Offset);
                    var read = stream.Read(bytes, 0, requested);
                    if (read == 0)
                    {
                        break;
                    }

                    cursor.Offset += read;
                    var characterCount = cursor.Decoder.GetChars(
                        bytes.AsSpan(0, read),
                        characters,
                        flush: false);
                    if (characterCount > 0)
                    {
                        Consume(
                            cursor,
                            new string(characters, 0, characterCount));
                    }
                }
            }
            catch (IOException)
            {
                // Codex may rotate or briefly hold a rollout. Keep the last
                // observed state instead of flashing a false state.
            }
            catch (UnauthorizedAccessException)
            {
                // Preserve the last observation when the file is unavailable.
            }

            return cursor.Status;
        }
    }

    private static void Consume(RolloutCursor cursor, string appended)
    {
        var text = cursor.PartialLine + appended;
        var start = 0;
        while (true)
        {
            var newline = text.IndexOf('\n', start);
            if (newline < 0)
            {
                cursor.PartialLine = text[start..];
                return;
            }

            var line = text.AsSpan(start, newline - start).TrimEnd('\r');
            if (!line.IsEmpty)
            {
                ConsumeLine(cursor, line);
            }

            start = newline + 1;
        }
    }

    private static void ConsumeLine(
        RolloutCursor cursor,
        ReadOnlySpan<char> line)
    {
        line = line.TrimStart('\uFEFF');

        // Avoid parsing the large prompt/message records. Lifecycle names are
        // ASCII and stable in the observed 26.707.12708.0 rollout protocol.
        if (
            !line.Contains("task_started", StringComparison.Ordinal) &&
            !line.Contains("task_complete", StringComparison.Ordinal) &&
            !line.Contains("turn_aborted", StringComparison.Ordinal) &&
            !line.Contains("stream_error", StringComparison.Ordinal) &&
            !line.Contains("\"type\":\"error\"", StringComparison.Ordinal))
        {
            return;
        }

        try
        {
            using var document = JsonDocument.Parse(line.ToString());
            var root = document.RootElement;
            if (
                !root.TryGetProperty("type", out var outerType) ||
                !outerType.ValueEquals("event_msg") ||
                !root.TryGetProperty("payload", out var payload) ||
                !payload.TryGetProperty("type", out var payloadType))
            {
                return;
            }

            if (payloadType.ValueEquals("task_started"))
            {
                cursor.Status = ThreadStatus.Thinking;
            }
            else if (
                payloadType.ValueEquals("task_complete") ||
                payloadType.ValueEquals("turn_aborted"))
            {
                cursor.Status = ThreadStatus.Idle;
            }
            else if (
                payloadType.ValueEquals("error") ||
                payloadType.ValueEquals("stream_error"))
            {
                cursor.Status = ThreadStatus.Error;
            }
        }
        catch (JsonException)
        {
            // Ignore malformed/partially persisted records. Complete records
            // arriving later will advance the state.
        }
    }

    private sealed class RolloutCursor
    {
        public long Offset { get; set; }
        public string PartialLine { get; set; } = string.Empty;
        public ThreadStatus Status { get; set; } = ThreadStatus.Unknown;
        public Decoder Decoder { get; } = Encoding.UTF8.GetDecoder();

        public void Reset()
        {
            Offset = 0;
            PartialLine = string.Empty;
            Status = ThreadStatus.Unknown;
            Decoder.Reset();
        }
    }
}
