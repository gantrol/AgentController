using System.IO;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using CodexController.Models;

namespace CodexController.Services;

public sealed class CodexKeybindingService
{
    private const string ReasoningDownCommand =
        "composer.decreaseReasoningEffort";
    private const string ReasoningUpCommand =
        "composer.increaseReasoningEffort";
    private const string FastToggleCommand =
        "composer.toggleFastMode";
    private const string SubmitCommand =
        "composer.submit";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
    };

    public string KeybindingsPath { get; } = Path.Combine(
        ResolveCodexHome(),
        "keybindings.json");

    public CodexKeybindingMergeResult EnsureBridgeBindings(
        AppSettings settings)
    {
        return EnsureBridgeBindings(settings, KeybindingsPath);
    }

    internal static CodexKeybindingMergeResult EnsureBridgeBindings(
        AppSettings settings,
        string keybindingsPath)
    {
        try
        {
            var root = ReadKeybindings(keybindingsPath);
            var desired = new[]
            {
                new DesiredBinding(
                    ReasoningDownCommand,
                    settings.ReasoningDownShortcut.Trim()),
                new DesiredBinding(
                    ReasoningUpCommand,
                    settings.ReasoningUpShortcut.Trim()),
                new DesiredBinding(
                    FastToggleCommand,
                    settings.FastToggleShortcut.Trim()),
                new DesiredBinding(
                    SubmitCommand,
                    settings.SubmitShortcut.Trim()),
            };

            var added = new List<string>();
            var conflicts = new List<string>();
            var changed = false;

            foreach (var binding in desired)
            {
                if (string.IsNullOrWhiteSpace(binding.Key))
                {
                    continue;
                }

                var conflictingCommand = FindConflictingCommand(
                    root,
                    binding.Command,
                    binding.Key);
                if (conflictingCommand is not null)
                {
                    conflicts.Add(
                        $"{binding.Key} 已由 {conflictingCommand} 使用");
                    continue;
                }

                var hasBinding = root
                    .OfType<JsonObject>()
                    .Any(item =>
                        string.Equals(
                            GetString(item, "command"),
                            binding.Command,
                            StringComparison.Ordinal) &&
                        string.Equals(
                            GetString(item, "key"),
                            binding.Key,
                            StringComparison.OrdinalIgnoreCase));

                for (var index = root.Count - 1; index >= 0; index--)
                {
                    if (
                        root[index] is JsonObject item &&
                        string.Equals(
                            GetString(item, "command"),
                            binding.Command,
                            StringComparison.Ordinal) &&
                        HasNullKey(item))
                    {
                        root.RemoveAt(index);
                        changed = true;
                    }
                }

                if (hasBinding)
                {
                    continue;
                }

                root.Add(new JsonObject
                {
                    ["command"] = binding.Command,
                    ["key"] = binding.Key,
                });
                added.Add($"{binding.Command} = {binding.Key}");
                changed = true;
            }

            if (changed)
            {
                WriteAtomically(keybindingsPath, root);
            }

            return new CodexKeybindingMergeResult(
                keybindingsPath,
                changed,
                added,
                conflicts,
                null);
        }
        catch (Exception exception)
        {
            return new CodexKeybindingMergeResult(
                keybindingsPath,
                false,
                [],
                [],
                exception.Message);
        }
    }

    private static JsonArray ReadKeybindings(string path)
    {
        if (!File.Exists(path))
        {
            return [];
        }

        var text = File.ReadAllText(path);
        if (string.IsNullOrWhiteSpace(text))
        {
            return [];
        }

        if (JsonNode.Parse(text) is not JsonArray root)
        {
            throw new InvalidDataException(
                "Codex keybindings.json 必须是 JSON 数组，已停止写入以保护原配置。");
        }

        foreach (var node in root)
        {
            if (
                node is not JsonObject item ||
                string.IsNullOrWhiteSpace(GetString(item, "command")) ||
                !HasStringOrNullKey(item))
            {
                throw new InvalidDataException(
                    "Codex keybindings.json 含有无法识别的条目，已停止写入以保护原配置。");
            }
        }

        return root;
    }

    private static string? FindConflictingCommand(
        JsonArray root,
        string command,
        string key)
    {
        return root
            .OfType<JsonObject>()
            .Where(item =>
                !string.Equals(
                    GetString(item, "command"),
                    command,
                    StringComparison.Ordinal) &&
                string.Equals(
                    GetString(item, "key"),
                    key,
                    StringComparison.OrdinalIgnoreCase))
            .Select(item => GetString(item, "command"))
            .FirstOrDefault(value => !string.IsNullOrWhiteSpace(value));
    }

    private static bool HasStringOrNullKey(JsonObject item)
    {
        if (!TryGetProperty(item, "key", out var value))
        {
            return false;
        }

        return value is null || GetStringValue(value) is not null;
    }

    private static bool HasNullKey(JsonObject item)
    {
        return
            TryGetProperty(item, "key", out var value) &&
            value is null;
    }

    private static string? GetString(JsonObject item, string name)
    {
        return
            TryGetProperty(item, name, out var value)
                ? GetStringValue(value)
                : null;
    }

    private static string? GetStringValue(JsonNode? value)
    {
        return value is JsonValue jsonValue &&
               jsonValue.TryGetValue<string>(out var text)
            ? text
            : null;
    }

    private static bool TryGetProperty(
        JsonObject item,
        string name,
        out JsonNode? value)
    {
        foreach (var property in item)
        {
            if (string.Equals(
                    property.Key,
                    name,
                    StringComparison.OrdinalIgnoreCase))
            {
                value = property.Value;
                return true;
            }
        }

        value = null;
        return false;
    }

    private static void WriteAtomically(string path, JsonArray root)
    {
        var directory = Path.GetDirectoryName(path);
        if (string.IsNullOrWhiteSpace(directory))
        {
            throw new InvalidOperationException(
                "无法确定 Codex keybindings.json 所在目录。");
        }

        Directory.CreateDirectory(directory);
        var temporaryPath = Path.Combine(
            directory,
            $".keybindings.{Guid.NewGuid():N}.tmp");
        try
        {
            var json = $"{root.ToJsonString(JsonOptions)}{Environment.NewLine}";
            File.WriteAllText(
                temporaryPath,
                json,
                new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
            if (File.Exists(path))
            {
                File.Copy(
                    path,
                    $"{path}.AgentController.bak",
                    overwrite: true);
            }

            File.Move(temporaryPath, path, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporaryPath))
            {
                File.Delete(temporaryPath);
            }
        }
    }

    private static string ResolveCodexHome()
    {
        var configured = Environment.GetEnvironmentVariable("CODEX_HOME");
        if (!string.IsNullOrWhiteSpace(configured))
        {
            return configured;
        }

        return Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            ".codex");
    }

    private sealed record DesiredBinding(string Command, string Key);
}

public sealed record CodexKeybindingMergeResult(
    string Path,
    bool Changed,
    IReadOnlyList<string> Added,
    IReadOnlyList<string> Conflicts,
    string? Error)
{
    public bool Succeeded => Error is null;
}
