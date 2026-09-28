using System.Buffers;
using System.IO;
using System.Text.Json;
using CodexController.Models;

namespace CodexController.Services;

public readonly record struct CodexRolloutStatusSnapshot(
    ThreadStatus Status,
    bool HasPendingQuestion);

/// <summary>
/// Reads the append-only Codex rollout lifecycle without inspecting the UI.
/// This is the honest local fallback when the optional Virtual Micro status
/// observer is unavailable: it can distinguish an open turn, stopped, and failed,
/// but it deliberately does not invent Codex's private unread/approval state.
/// </summary>
public sealed class CodexRolloutStatusReader
{
    private const int ReadBufferSize = 32 * 1024;
    private const int MaximumRetainedLineBytes = 256 * 1024;

    private readonly object _sync = new();
    private readonly byte[] _readBuffer = new byte[ReadBufferSize];
    private readonly Dictionary<string, RolloutCursor> _cursors =
        new(StringComparer.OrdinalIgnoreCase);

    public ThreadStatus Read(string? rolloutPath) => ReadSnapshot(rolloutPath).Status;

    public CodexRolloutStatusSnapshot ReadSnapshot(
        string? rolloutPath,
        IReadOnlyCollection<string>? acceptedQuestionReplies = null)
    {
        if (string.IsNullOrWhiteSpace(rolloutPath) ||
            rolloutPath.Contains("trash", StringComparison.OrdinalIgnoreCase))
        {
            return new(ThreadStatus.Unknown, false);
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
                stream.Position = cursor.Offset;
                while (cursor.Offset < endOffset)
                {
                    var requested = (int)Math.Min(
                        _readBuffer.Length,
                        endOffset - cursor.Offset);
                    var read = stream.Read(_readBuffer, 0, requested);
                    if (read == 0)
                    {
                        break;
                    }

                    cursor.Offset += read;
                    Consume(cursor, _readBuffer.AsMemory(0, read));
                }
            }
            catch (IOException)
            {
                // Codex may rotate or briefly hold a rollout. Keep the last
                // observed state instead of flashing a false state.
                return new(cursor.Status, false);
            }
            catch (UnauthorizedAccessException)
            {
                // Preserve the last observation when the file is unavailable.
                return new(cursor.Status, false);
            }

            if (acceptedQuestionReplies is not null)
            {
                foreach (var reply in acceptedQuestionReplies)
                {
                    try
                    {
                        ReadQuestionAnswers(cursor, reply);
                    }
                    catch (JsonException)
                    {
                    }
                }
            }

            return cursor.Snapshot;
        }
    }

    private static void Consume(
        RolloutCursor cursor,
        ReadOnlyMemory<byte> appended)
    {
        while (!appended.IsEmpty)
        {
            var newline = appended.Span.IndexOf((byte)'\n');
            if (newline < 0)
            {
                cursor.PartialLine.Append(appended.Span);
                return;
            }

            var segment = appended[..newline];
            if (cursor.PartialLine.IsEmpty)
            {
                ConsumeLine(cursor, segment);
            }
            else
            {
                cursor.PartialLine.Append(segment.Span);
                ConsumeLine(cursor, cursor.PartialLine.WrittenMemory);
            }

            cursor.PartialLine.Clear();
            appended = appended[(newline + 1)..];
        }
    }

    private static void ConsumeLine(
        RolloutCursor cursor,
        ReadOnlyMemory<byte> line)
    {
        while (!line.IsEmpty && line.Span[^1] == (byte)'\r')
        {
            line = line[..^1];
        }

        while (line.Span.StartsWith("\uFEFF"u8))
        {
            line = line[3..];
        }

        if (line.IsEmpty)
        {
            return;
        }

        // Avoid parsing the large prompt/message records. Lifecycle names are
        // ASCII and stable in the observed 26.707.12708.0 rollout protocol.
        if (
            line.Span.IndexOf("task_started"u8) < 0 &&
            line.Span.IndexOf("task_complete"u8) < 0 &&
            line.Span.IndexOf("turn_aborted"u8) < 0 &&
            line.Span.IndexOf("stream_error"u8) < 0 &&
            line.Span.IndexOf("\"questions\""u8) < 0 &&
            line.Span.IndexOf("send_user_message_question_reply"u8) < 0 &&
            line.Span.IndexOf("\"type\":\"error\""u8) < 0)
        {
            return;
        }

        try
        {
            using var document = JsonDocument.Parse(line);
            var root = document.RootElement;
            if (
                root.ValueKind != JsonValueKind.Object ||
                !root.TryGetProperty("type", out var outerType) ||
                outerType.ValueKind != JsonValueKind.String ||
                !root.TryGetProperty("payload", out var payload) ||
                payload.ValueKind != JsonValueKind.Object ||
                !payload.TryGetProperty("type", out var payloadType) ||
                payloadType.ValueKind != JsonValueKind.String)
            {
                return;
            }

            if (outerType.ValueEquals("response_item"))
            {
                if (ReadString(payload, "type") == "message" &&
                    ReadString(payload, "role") == "user")
                {
                    ReadQuestionAnswers(cursor, payload);
                }
                return;
            }
            if (!outerType.ValueEquals("event_msg"))
            {
                return;
            }

            if (payloadType.ValueEquals("task_started"))
            {
                var turnId = ReadString(payload, "turn_id");
                if (turnId is null || turnId != cursor.TurnId)
                {
                    cursor.ClearQuestions();
                }
                cursor.TurnId = turnId;
                cursor.Status = ThreadStatus.Thinking;
            }
            else if (
                payloadType.ValueEquals("task_complete") ||
                payloadType.ValueEquals("turn_aborted"))
            {
                cursor.Status = ThreadStatus.Idle;
                cursor.ClearQuestions();
                cursor.TurnId = null;
            }
            else if (
                payloadType.ValueEquals("error") ||
                payloadType.ValueEquals("stream_error"))
            {
                cursor.Status = ThreadStatus.Error;
            }
            else if (payloadType.ValueEquals("item_completed") &&
                payload.TryGetProperty("item", out var item) &&
                item.ValueKind == JsonValueKind.Object)
            {
                ReadQuestionItem(cursor, payload, item);
            }
        }
        catch (JsonException)
        {
            // Ignore malformed/partially persisted records. Complete records
            // arriving later will advance the state.
        }
    }

    private static void ReadQuestionItem(
        RolloutCursor cursor,
        JsonElement payload,
        JsonElement item)
    {
        var type = ReadString(item, "type");
        if (type is "UserMessage" or "userMessage")
        {
            ReadQuestionAnswers(cursor, item);
            return;
        }

        if (cursor.Status != ThreadStatus.Thinking ||
            type is not ("AgentMessage" or "agentMessage") ||
            ReadString(item, "delivery") != "async" ||
            ReadString(item, "id") is not { Length: > 0 } itemId ||
            (cursor.TurnId is not null &&
                ReadString(payload, "turn_id") != cursor.TurnId) ||
            !item.TryGetProperty("questions", out var questions) ||
            questions.ValueKind != JsonValueKind.Array)
        {
            return;
        }

        var index = 0;
        foreach (var question in questions.EnumerateArray())
        {
            var key = (itemId, index++);
            if (ReadString(question, "title") is { Length: > 0 } &&
                !cursor.AnsweredQuestions.Contains(key))
            {
                cursor.PendingQuestions.Add(key);
            }
        }
    }

    private static void ReadQuestionAnswers(RolloutCursor cursor, JsonElement message)
    {
        if (!message.TryGetProperty("content", out var content) ||
            content.ValueKind != JsonValueKind.Array || content.GetArrayLength() != 1 ||
            ReadString(content[0], "text") is not { } text)
        {
            return;
        }

        ReadQuestionAnswers(cursor, text);
    }

    private static void ReadQuestionAnswers(RolloutCursor cursor, string text)
    {
        const string start = "<send_user_message_question_reply>";
        const string end = "</send_user_message_question_reply>";
        text = text.Trim();
        if (!text.StartsWith(start, StringComparison.Ordinal) ||
            !text.EndsWith(end, StringComparison.Ordinal))
        {
            return;
        }

        using var replies = JsonDocument.Parse(text[start.Length..^end.Length]);
        if (replies.RootElement.ValueKind == JsonValueKind.Object)
        {
            ReadQuestionAnswer(cursor, replies.RootElement);
        }
        else if (replies.RootElement.ValueKind == JsonValueKind.Array)
        {
            foreach (var reply in replies.RootElement.EnumerateArray())
            {
                ReadQuestionAnswer(cursor, reply);
            }
        }
    }

    private static void ReadQuestionAnswer(RolloutCursor cursor, JsonElement reply)
    {
        if (ReadString(reply, "questionItemId") is not { } questionId ||
            ReadString(reply, "answer") is null)
        {
            return;
        }

        try
        {
            using var identity = JsonDocument.Parse(questionId);
            var parts = identity.RootElement;
            if (parts.ValueKind == JsonValueKind.Array && parts.GetArrayLength() == 3 &&
                parts[0].ValueKind == JsonValueKind.String &&
                parts[0].ValueEquals("request_user_input_async") &&
                parts[1].ValueKind == JsonValueKind.String &&
                parts[1].GetString() is { Length: > 0 } itemId &&
                parts[2].ValueKind == JsonValueKind.Number &&
                parts[2].TryGetInt32(out var index) && index >= 0)
            {
                var key = (itemId, index);
                cursor.PendingQuestions.Remove(key);
                cursor.AnsweredQuestions.Add(key);
            }
        }
        catch (JsonException)
        {
        }
    }

    private static string? ReadString(JsonElement element, string name) =>
        element.ValueKind == JsonValueKind.Object &&
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString() : null;

    private sealed class RolloutCursor
    {
        public long Offset { get; set; }
        public PooledLineBuffer PartialLine { get; } = new();
        public ThreadStatus Status { get; set; } = ThreadStatus.Unknown;
        public string? TurnId { get; set; }
        public HashSet<(string ItemId, int Index)> PendingQuestions { get; } = [];
        public HashSet<(string ItemId, int Index)> AnsweredQuestions { get; } = [];
        public CodexRolloutStatusSnapshot Snapshot => new(
            Status, Status == ThreadStatus.Thinking && PendingQuestions.Count > 0);

        public void ClearQuestions()
        {
            PendingQuestions.Clear();
            AnsweredQuestions.Clear();
        }

        public void Reset()
        {
            Offset = 0;
            PartialLine.Clear();
            Status = ThreadStatus.Unknown;
            TurnId = null;
            ClearQuestions();
        }
    }

    /// <summary>
    /// Keeps only a line that crosses read boundaries. Most JSONL records are
    /// inspected directly in the shared read buffer; unusually large prompt
    /// records grow this buffer linearly instead of repeatedly copying the
    /// full prefix for every 32 KB chunk.
    /// </summary>
    private sealed class PooledLineBuffer
    {
        private byte[] _buffer =
            ArrayPool<byte>.Shared.Rent(ReadBufferSize);
        private int _length;

        public bool IsEmpty => _length == 0;

        public ReadOnlyMemory<byte> WrittenMemory =>
            _buffer.AsMemory(0, _length);

        public void Append(ReadOnlySpan<byte> value)
        {
            if (value.IsEmpty)
            {
                return;
            }

            EnsureCapacity(checked(_length + value.Length));
            value.CopyTo(_buffer.AsSpan(_length));
            _length += value.Length;
        }

        public void Clear()
        {
            if (_buffer.Length > MaximumRetainedLineBytes)
            {
                var oversized = _buffer;
                _buffer = ArrayPool<byte>.Shared.Rent(ReadBufferSize);
                _length = 0;
                ArrayPool<byte>.Shared.Return(
                    oversized,
                    clearArray: true);
                return;
            }

            if (_length > 0)
            {
                _buffer.AsSpan(0, _length).Clear();
                _length = 0;
            }
        }

        private void EnsureCapacity(int required)
        {
            if (required <= _buffer.Length)
            {
                return;
            }

            var doubled = _buffer.Length <= int.MaxValue / 2
                ? _buffer.Length * 2
                : int.MaxValue;
            var replacement = ArrayPool<byte>.Shared.Rent(
                Math.Max(required, doubled));
            _buffer.AsSpan(0, _length).CopyTo(replacement);
            ArrayPool<byte>.Shared.Return(
                _buffer,
                clearArray: true);
            _buffer = replacement;
        }
    }
}
