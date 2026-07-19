using CodexMicro.Desktop.Services;
using Xunit;

namespace CodexMicro.Desktop.Tests;

public sealed class CodexAgentRosterObserverTests
{
    [Fact]
    public void ParseOrdersRecentThreadsAndAddsProjectName()
    {
        const string sessions = """
            {"id":"older","thread_name":"Older task","updated_at":"2026-07-18T10:00:00Z"}
            {"id":"newer","thread_name":"Newer task","updated_at":"2026-07-18T12:00:00Z"}
            {"id":"older","thread_name":"Older task renamed","updated_at":"2026-07-18T11:00:00Z"}
            """;
        const string globalState = """
            {
              "local-projects": {
                "project-1": {"id":"project-1","name":"AgentController","rootPaths":["D:\\AgentController"]}
              },
              "thread-project-assignments": {
                "newer": {"projectId":"project-1","cwd":"D:\\AgentController"}
              }
            }
            """;

        var result = CodexAgentRosterObserver.Parse(sessions, globalState);

        Assert.Equal(2, result.Entries.Count);
        Assert.Equal("newer", result.Entries[0].ThreadId);
        Assert.Equal("AgentController › Newer task", result.Entries[0].DisplayTitle);
        Assert.Equal("Older task renamed", result.Entries[1].Title);
    }

    [Fact]
    public void ParseSupportsLocalThreadKeyAndWorkspaceFallback()
    {
        const string sessions = """
            {"id":"thread-1","thread_name":"Investigate input","updated_at":1784376000}
            """;
        const string globalState = """
            {
              "thread-project-assignments": {
                "local:thread-1": {"projectId":"missing","cwd":"D:\\work\\virtual-micro\\"}
              }
            }
            """;

        var result = CodexAgentRosterObserver.Parse(sessions, globalState);

        var entry = Assert.Single(result.Entries);
        Assert.Equal("virtual-micro › Investigate input", entry.DisplayTitle);
    }

    [Fact]
    public void ParseKeepsNewestSixAndIgnoresIncompleteLines()
    {
        var sessions = string.Join(
            Environment.NewLine,
            Enumerable.Range(0, 8).Select(index =>
                $"{{\"id\":\"thread-{index}\",\"thread_name\":\"Task {index}\",\"updated_at\":{1_784_376_000 + index}}}")) +
            Environment.NewLine +
            "{partial";

        var result = CodexAgentRosterObserver.Parse(sessions, null);

        Assert.Equal(6, result.Entries.Count);
        Assert.Equal("thread-7", result.Entries[0].ThreadId);
        Assert.Equal("thread-2", result.Entries[^1].ThreadId);
    }

    [Theory]
    [InlineData("pinned")]
    [InlineData("priority")]
    [InlineData("custom")]
    public void ParseDoesNotGuessTitlesForUnsupportedAgentSource(string source)
    {
        const string sessions = """
            {"id":"thread-1","thread_name":"Must not be misassigned","updated_at":1784376000}
            """;
        var globalState = $$"""
            {"codex-micro-agent-source":"{{source}}"}
            """;

        var result = CodexAgentRosterObserver.Parse(sessions, globalState);

        Assert.Empty(result.Entries);
        Assert.Contains(source, result.Source);
    }

    [Fact]
    public void ConfigAgentSourceOverridesDefaultRecentMapping()
    {
        const string sessions = """
            {"id":"thread-1","thread_name":"Must not be misassigned","updated_at":1784376000}
            """;
        const string config = """
            [desktop]
            codex-micro-agent-source = "priority"
            """;

        var result = CodexAgentRosterObserver.Parse(
            sessions,
            globalStateJson: null,
            configToml: config);

        Assert.Empty(result.Entries);
        Assert.Contains("priority", result.Source);
    }
}
