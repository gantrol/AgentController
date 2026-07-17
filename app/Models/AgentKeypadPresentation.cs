namespace CodexController.Models;

public sealed record AgentKeypadPresentation(
    string Instruction,
    string DismissGlyph,
    string DismissHint,
    string SelectionHint,
    string IdleLabel,
    string ThinkingLabel,
    string CompleteUnreadLabel,
    string RequiresInputLabel,
    string ErrorLabel,
    string UnassignedLabel)
{
    public static AgentKeypadPresentation Empty { get; } = new(
        string.Empty,
        string.Empty,
        string.Empty,
        string.Empty,
        string.Empty,
        string.Empty,
        string.Empty,
        string.Empty,
        string.Empty,
        string.Empty);
}
