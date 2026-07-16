namespace CodexController.Agents;

/// <summary>
/// Locale-independent automation failure shared by Agent adapters and
/// presentation code. <see cref="Code"/> is stable and safe for branching;
/// <see cref="Detail"/> is optional diagnostic context and is never a
/// localized user-facing sentence.
/// </summary>
public readonly record struct AgentAutomationError(
    string Code,
    string? Detail = null);

/// <summary>
/// Internal exception used when a lower-level parser must abort while
/// preserving an automation error code for the service result.
/// </summary>
public sealed class AgentAutomationException : Exception
{
    public AgentAutomationException(
        string code,
        string? detail = null,
        Exception? innerException = null)
        : base(code, innerException)
    {
        Error = new AgentAutomationError(code, detail);
    }

    public AgentAutomationError Error { get; }
}

/// <summary>
/// Stable error codes returned by Agent automation services.
/// </summary>
public static class AgentAutomationErrorCodes
{
    public const string BridgeSafePreview = "bridge-safe-preview";
    public const string AgentNotForeground = "agent-not-foreground";
    public const string AgentWindowNotFound = "agent-window-not-found";
    public const string ElementNotFound = "automation-element-not-found";
    public const string FocusRejected = "automation-focus-rejected";
    public const string ComposerEmpty = "composer-empty";
    public const string InputInjectionFailed = "input-injection-failed";
    public const string ElementUnsupported =
        "automation-element-unsupported";
    public const string OperationCanceled = "operation-canceled";
    public const string AutomationStale = "automation-stale";
    public const string NavigationUnavailable =
        "navigation-unavailable";
    public const string KeybindingsInvalid = "keybindings-invalid";
    public const string KeybindingsPathUnavailable =
        "keybindings-path-unavailable";
    public const string Unexpected = "automation-unexpected";
    public const string CapabilityUnavailable =
        "capability-unavailable";

    public static bool IsImmediateFailure(string? code)
    {
        return code is
            BridgeSafePreview or
            AgentNotForeground or
            AgentWindowNotFound or
            OperationCanceled;
    }
}
