using System.Text.Json.Nodes;
using CodexController.Models;
using CodexController.Services;

namespace CodexController.Tests;

public sealed class CodexKeybindingServiceTests
{
    [Fact]
    public void AddsOfficialDictationBindingAndRemainsIdempotent()
    {
        using var store = new TemporaryKeybindingStore();
        var settings = new AppSettings
        {
            DictationShortcut = "Ctrl+Shift+D",
            SubmitShortcut = "F23",
        };

        var first = CodexKeybindingService.EnsureBridgeBindings(
            settings,
            store.Path);
        var second = CodexKeybindingService.EnsureBridgeBindings(
            settings,
            store.Path);
        var bindings = JsonNode.Parse(
            File.ReadAllText(store.Path))!.AsArray();

        Assert.True(first.Succeeded);
        Assert.True(first.Changed);
        Assert.Contains(
            "composer.startDictation = Ctrl+Shift+D",
            first.Added);
        Assert.Contains(
            bindings.OfType<JsonObject>(),
            item =>
                item["command"]?.GetValue<string>() ==
                    "composer.startDictation" &&
                item["key"]?.GetValue<string>() ==
                    "Ctrl+Shift+D");
        Assert.Contains(
            bindings.OfType<JsonObject>(),
            item =>
                item["command"]?.GetValue<string>() ==
                    "composer.submit" &&
                item["key"]?.GetValue<string>() == "F23");
        Assert.DoesNotContain(
            bindings.OfType<JsonObject>(),
            item =>
                item["command"]?.GetValue<string>() ==
                    "composer.togglePlanMode");
        Assert.True(second.Succeeded);
        Assert.False(second.Changed);
        Assert.Empty(second.Added);
    }

    [Fact]
    public void ReportsDictationShortcutConflictWithoutOverwritingIt()
    {
        using var store = new TemporaryKeybindingStore(
            """
            [
              {
                "command": "another.command",
                "key": "Ctrl+Shift+D"
              }
            ]
            """);

        var result = CodexKeybindingService.EnsureBridgeBindings(
            new AppSettings(),
            store.Path);
        var bindings = JsonNode.Parse(
            File.ReadAllText(store.Path))!.AsArray();

        Assert.True(result.Succeeded);
        Assert.Contains(
            "key=Ctrl+Shift+D;command=another.command",
            result.Conflicts);
        Assert.DoesNotContain(
            bindings.OfType<JsonObject>(),
            item =>
                item["command"]?.GetValue<string>() ==
                "composer.startDictation");
    }

    private sealed class TemporaryKeybindingStore : IDisposable
    {
        private readonly string _directory;

        public TemporaryKeybindingStore(string contents = "[]")
        {
            _directory = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(),
                $"agent-controller-keybindings-{Guid.NewGuid():N}");
            Directory.CreateDirectory(_directory);
            Path = System.IO.Path.Combine(
                _directory,
                "keybindings.json");
            File.WriteAllText(Path, contents);
        }

        public string Path { get; }

        public void Dispose()
        {
            if (Directory.Exists(_directory))
            {
                Directory.Delete(_directory, recursive: true);
            }
        }
    }
}
