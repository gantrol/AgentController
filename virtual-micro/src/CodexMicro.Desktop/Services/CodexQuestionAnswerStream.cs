using System.Globalization;
using System.Text.Json;

namespace CodexMicro.Desktop.Services;

internal sealed class CodexQuestionAnswerStream
{
    private sealed record Submission(string[] Path, string? Reply, string? Status);

    private readonly List<Submission> _submissions = [];
    private readonly HashSet<string> _accepted = new(StringComparer.Ordinal);

    internal void Reset()
    {
        _submissions.Clear();
        _accepted.Clear();
    }

    internal string[] DrainAcceptedReplies()
    {
        var replies = _accepted.ToArray();
        _accepted.Clear();
        return replies;
    }

    internal void ReadState(JsonElement state)
    {
        Reset();
        ReadValue(state, []);
    }

    internal void ApplyPatch(string operation, string[] path, JsonElement value)
    {
        var removes = operation == "remove";
        var position = -1;
        var arrayEdit = path.Length > 0 && IsArrayPath(path[..^1]) &&
            operation is "add" or "remove" &&
            int.TryParse(path[^1], NumberStyles.None, CultureInfo.InvariantCulture, out position);
        if (!removes && path.Length > 0 && path[^1] == "length" &&
            IsArrayPath(path[..^1]) && value.ValueKind == JsonValueKind.Number &&
            value.TryGetInt32(out var length) && length >= 0)
        {
            var parent = path[..^1];
            _submissions.RemoveAll(submission =>
                submission.Path.Length > parent.Length && IsPrefix(parent, submission.Path) &&
                int.TryParse(submission.Path[parent.Length], out var index) && index >= length);
            return;
        }
        for (var index = _submissions.Count - 1; index >= 0; index--)
        {
            var submission = _submissions[index];
            if (IsPrefix(path, submission.Path))
            {
                if (!arrayEdit || removes)
                {
                    _submissions.RemoveAt(index);
                }
            }
            else if (IsPrefix(submission.Path, path))
            {
                var field = path[submission.Path.Length];
                if (field == "status" && path.Length == submission.Path.Length + 1)
                {
                    submission = submission with { Status = removes ? null : ReadString(value) };
                }
                else if (field == "input")
                {
                    submission = submission with
                    {
                        Reply = !removes && path.Length == submission.Path.Length + 1
                            ? ReadReply(value) : null,
                    };
                }
                else
                {
                    continue;
                }

                _submissions[index] = submission;
                Accept(submission);
            }
        }

        if (arrayEdit)
        {
            var parent = path[..^1];
            for (var index = 0; index < _submissions.Count; index++)
            {
                var submission = _submissions[index];
                if (submission.Path.Length <= parent.Length || !IsPrefix(parent, submission.Path) ||
                    !int.TryParse(submission.Path[parent.Length], out var itemIndex) ||
                    itemIndex < position)
                {
                    continue;
                }

                var shifted = (string[])submission.Path.Clone();
                shifted[parent.Length] = (itemIndex + (removes ? -1 : 1))
                    .ToString(CultureInfo.InvariantCulture);
                _submissions[index] = submission with { Path = shifted };
            }
        }

        if (!removes)
        {
            ReadValue(value, path);
        }
    }

    private void ReadValue(JsonElement value, string[] path)
    {
        if (value.ValueKind == JsonValueKind.Object)
        {
            if (path.Length > 0 && IsItemsPath(path[..^1]) &&
                value.TryGetProperty("type", out var type) &&
                ReadString(type) == "steeringUserMessage")
            {
                if (!value.TryGetProperty("input", out var input) || ReadReply(input) is not { } reply)
                {
                    return;
                }

                var submission = new Submission(path, reply,
                    value.TryGetProperty("status", out var status) ? ReadString(status) : null);
                _submissions.Add(submission);
                Accept(submission);
                return;
            }

            foreach (var property in value.EnumerateObject())
            {
                if ((path.Length == 0 && property.Name is "turns" or "turnHistory") ||
                    (path.Length == 1 && path[0] == "turnHistory" && property.Name == "history") ||
                    (path.Length == 2 && path[0] == "turnHistory" && path[1] == "history" &&
                        property.Name == "entitiesByKey") ||
                    IsCanonicalTurnsPath(path) ||
                    (IsTurnPath(path) && property.Name == "items"))
                {
                    ReadValue(property.Value, [.. path, property.Name]);
                }
            }
        }
        else if (value.ValueKind == JsonValueKind.Array && IsArrayPath(path))
        {
            var index = 0;
            foreach (var item in value.EnumerateArray())
            {
                ReadValue(item, [.. path, (index++).ToString(CultureInfo.InvariantCulture)]);
            }
        }
    }

    private void Accept(Submission submission)
    {
        if (submission.Status == "accepted" && submission.Reply is { } reply)
        {
            _accepted.Add(reply);
        }
    }

    private static string? ReadReply(JsonElement input)
    {
        if (input.ValueKind != JsonValueKind.Array || input.GetArrayLength() != 1 ||
            input[0].ValueKind != JsonValueKind.Object ||
            !input[0].TryGetProperty("type", out var type) || ReadString(type) != "text" ||
            !input[0].TryGetProperty("text", out var text) || ReadString(text)?.Trim() is not { } reply)
        {
            return null;
        }

        return reply.StartsWith("<send_user_message_question_reply>", StringComparison.Ordinal) &&
            reply.EndsWith("</send_user_message_question_reply>", StringComparison.Ordinal)
                ? reply : null;
    }

    private static string? ReadString(JsonElement value) =>
        value.ValueKind == JsonValueKind.String ? value.GetString() : null;

    private static bool IsCanonicalTurnsPath(string[] path) =>
        path is ["turnHistory", "history", "entitiesByKey"];

    private static bool IsTurnPath(string[] path) =>
        path is ["turns", _] or ["turnHistory", "history", "entitiesByKey", _];

    private static bool IsItemsPath(string[] path) =>
        path.Length > 0 && path[^1] == "items" && IsTurnPath(path[..^1]);

    private static bool IsArrayPath(string[] path) =>
        path is ["turns"] || IsItemsPath(path);

    private static bool IsPrefix(string[] prefix, string[] path) =>
        prefix.Length <= path.Length && path.AsSpan(0, prefix.Length).SequenceEqual(prefix);
}
