using CodexController.Core.Bridge;

namespace CodexController.Presentation.Feedback;

/// <summary>
/// Converts a locale-independent bridge event into text for presentation
/// surfaces. A localization-aware implementation can be swapped at runtime.
/// </summary>
public interface IBridgeFeedbackFormatter
{
    BridgeFeedbackContent Format(BridgeEvent bridgeEvent);
}

/// <summary>
/// Renders a toast request without exposing a WPF Window to the presenter.
/// </summary>
public interface IOverlayPresenter
{
    void Present(BridgeOverlayRequest request);
}

public sealed record BridgeFeedbackContent
{
    public BridgeFeedbackContent(
        string logText,
        string? footerText = null,
        BridgeToastText? toast = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(logText);
        if (footerText is not null)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(footerText);
        }

        LogText = logText;
        FooterText = footerText;
        Toast = toast;
    }

    public string LogText { get; }

    /// <summary>
    /// Falls back to <see cref="LogText"/> when a footer-targeted event does
    /// not need distinct wording.
    /// </summary>
    public string? FooterText { get; }

    public BridgeToastText? Toast { get; }
}

public sealed record BridgeToastText
{
    public BridgeToastText(string title, string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(title);
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        Title = title;
        Value = value;
    }

    public string Title { get; }

    public string Value { get; }
}

/// <summary>
/// A bindable, rendered event row that retains its structured source for
/// diagnostics and later re-localization.
/// </summary>
public sealed record BridgeFeedbackLogRow(
    BridgeEvent Source,
    string Text)
{
    public DateTimeOffset Timestamp => Source.Timestamp;

    public string Time => Timestamp.LocalDateTime.ToString("HH:mm:ss");

    public BridgeEventKey Key => Source.Key;

    public BridgeEventSeverity Severity => Source.Severity;
}

public sealed record BridgeFooterStatus(
    BridgeEvent Source,
    string Text)
{
    public DateTimeOffset Timestamp => Source.Timestamp;

    public BridgeEventKey Key => Source.Key;

    public BridgeEventSeverity Severity => Source.Severity;
}

/// <summary>
/// Fully rendered toast payload plus presentation-neutral scheduling hints.
/// </summary>
public sealed record BridgeOverlayRequest(
    BridgeEvent Source,
    string Title,
    string Value,
    TimeSpan? Duration,
    string? CoalesceKey);

/// <summary>
/// Small adapter that lets the composition root wrap an existing overlay
/// implementation without making it implement a presentation-layer interface.
/// </summary>
public sealed class DelegateOverlayPresenter : IOverlayPresenter
{
    private readonly Action<BridgeOverlayRequest> _present;

    public DelegateOverlayPresenter(Action<BridgeOverlayRequest> present)
    {
        ArgumentNullException.ThrowIfNull(present);
        _present = present;
    }

    public void Present(BridgeOverlayRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        _present(request);
    }
}
