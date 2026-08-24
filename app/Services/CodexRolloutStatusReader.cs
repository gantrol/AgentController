using System.Buffers;
using System.IO;
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
    private const int MaximumRetainedLineBytes = 256 * 1024;

    private readonly object _sync = new();
    private readonly byte[] _readBuffer = new byte[ReadBufferSize];
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
            }
            catch (UnauthorizedAccessException)
            {
                // Preserve the last observation when the file is unavailable.
            }

            return cursor.Status;
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
            line.Span.IndexOf("\"type\":\"error\""u8) < 0)
        {
            return;
        }

        try
        {
            using var document = JsonDocument.Parse(line);
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
        public PooledLineBuffer PartialLine { get; } = new();
        public ThreadStatus Status { get; set; } = ThreadStatus.Unknown;

        public void Reset()
        {
            Offset = 0;
            PartialLine.Clear();
            Status = ThreadStatus.Unknown;
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
