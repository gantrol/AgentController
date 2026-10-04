using AgentController.Adapters.Codex.Windows;
using System.Windows;

namespace CodexController.Tests;

// Models the external accessibility boundary. The real controller selects controls and verifies state.
internal sealed class CodexUiDesktopFixture : ICodexUiDesktop, ICodexUiSession
{
    internal List<(string Command, string Target)> Commands { get; } = [];
    internal List<UiNode> Nodes { get; } =
    [
        Node("root", null, UiRole.Other, "Fixture", new(0, 0, 1000, 900)),
        Node("back", "root", UiRole.Button, "Back", new(0, 0, 20, 20)) with { Invokable = true },
        Node("forward", "root", UiRole.Button, "Forward", new(20, 0, 20, 20)) with { Invokable = true },
        Node("route-a", "root", UiRole.Other, "Chat A", new(0, 20, 200, 40)) with { ClassName = "sidebar-item", Aria = "current=page" },
        Node("sidebar", "root", UiRole.Button, "Hide sidebar", new(0, 0, 35, 20)) with { Invokable = true },
        Node("conversation", "root", UiRole.Other, "", new(240, 20, 700, 560)) with { ScrollPercent = 50 },
        Node("composer-box", "root", UiRole.Other, "", new(240, 600, 700, 200)),
        Node("editor", "composer-box", UiRole.Editor, "", new(250, 610, 680, 90)) with { ClassName = "ProseMirror", Text = "synthetic unsent draft" },
        Node("model", "composer-box", UiRole.Button, "Fixture model", new(250, 740, 120, 40)) with { Invokable = true, Expanded = false },
        Node("send", "composer-box", UiRole.Button, "Send", new(880, 740, 40, 40)) with { Invokable = true },
    ];
    public bool IsCurrent { get; set; } = true;
    internal bool Available { get; set; } = true;
    internal bool IgnoreMutations { get; set; }
    internal Action? BeforeRead { get; set; }
    internal Action? AfterMutation { get; set; }
    internal string? PastedName { get; private set; }
    internal string? PastedPath { get; private set; }
    internal bool PasteVerified { get; set; } = true;
    internal int Reads { get; private set; }

    public ICodexUiSession? Capture() => Available ? this : null;
    public UiState Read() { Reads++; BeforeRead?.Invoke(); return CodexUiSelectors.Select(Nodes); }
    public void Dispose() { }
    internal void Set(string id, Func<UiNode, UiNode> update)
    {
        var index = Nodes.FindIndex(node => node.Id == id);
        Nodes[index] = update(Nodes[index]);
    }
    private void Apply(string command, string id, Action change)
    {
        if (!IsCurrent) throw new InvalidOperationException("wrong fixture window");
        Commands.Add((command, id));
        if (!IgnoreMutations) change();
        AfterMutation?.Invoke();
    }
    public void Invoke(string id) => Apply("invoke", id, () =>
    {
        switch (id)
        {
            case "send": Set("editor", node => node with { Text = "" }); Set("send", node => node with { Enabled = false }); break;
            case "sidebar": Set(id, node => node with { Name = node.Name == "Hide sidebar" ? "Show sidebar" : "Hide sidebar" }); break;
            case "model":
                Set(id, node => node with { Expanded = true });
                Nodes.Add(Node("menu", "root", UiRole.Menu, "", new(250, 480, 220, 120)));
                Nodes.Add(Node("choice-a", "menu", UiRole.MenuItem, "A", new(250, 480, 220, 40)) with { Invokable = true });
                Nodes.Add(Node("choice-b", "menu", UiRole.MenuItem, "B", new(250, 520, 220, 40)) with { Invokable = true });
                break;
            case "choice-a": case "choice-b":
                Set("model", node => node with { Name = id, Expanded = false });
                Nodes.RemoveAll(node => node.Id == "menu" || node.ParentId == "menu");
                break;
            default: throw new InvalidOperationException("FIXTURE GAP: " + id);
        }
    });
    public void Focus(string id) => Apply("focus", id, () =>
    {
        for (var index = 0; index < Nodes.Count; index++) Nodes[index] = Nodes[index] with { Focused = Nodes[index].Id == id };
    });
    public void Scroll(string id, CodexUiOperation operation) => Apply(operation.ToString(), id, () =>
    {
        if (id.StartsWith("viewport:", StringComparison.Ordinal))
        {
            Set("message-copy", node => node with { Bounds = new(node.Bounds.X,
                node.Bounds.Y + (operation == CodexUiOperation.ScrollUp ? 40 : -40), node.Bounds.Width, node.Bounds.Height) });
            return;
        }
        Set(id, node => node with { ScrollPercent = operation switch
        {
            CodexUiOperation.ScrollUp => node.ScrollPercent - 10,
            CodexUiOperation.ScrollDown => node.ScrollPercent + 10,
            CodexUiOperation.ScrollTop => 0,
            CodexUiOperation.ScrollBottom => 100,
            _ => throw new InvalidOperationException("FIXTURE GAP"),
        } });
    });
    public void Navigate(bool forward) => Apply("history", forward ? "forward" : "back", () =>
        Set("route-a", node => node with { Name = forward ? "Chat B" : "Chat Previous" }));
    public bool InsertSkill(string composerId, string name, string path, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        Apply("paste-mention", composerId, () => { PastedName = name; PastedPath = path; });
        return PasteVerified;
    }
    private static UiNode Node(string id, string? parent, UiRole role, string name, Rect bounds) => new(id, parent, role, name, "", bounds);
}
