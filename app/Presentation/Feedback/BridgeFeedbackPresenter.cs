using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using CodexController.Core.Bridge;

namespace CodexController.Presentation.Feedback;

/// <summary>
/// Projects structured bridge events onto the event log, footer, and toast
/// surfaces. It owns no WPF objects and can be constructed on any UI framework
/// synchronization context.
/// </summary>
public sealed class BridgeFeedbackPresenter :
    INotifyPropertyChanged,
    IDisposable
{
    public const int MaximumLogRows = 4;

    private readonly object _presentationGate = new();
    private readonly IBridgeFeedbackFormatter _formatter;
    private readonly IOverlayPresenter _overlayPresenter;
    private readonly SynchronizationContext? _synchronizationContext;
    private readonly int _ownerThreadId;
    private readonly int _maximumLogRows;
    private readonly List<BridgeEvent> _retainedEvents = [];
    private readonly ObservableCollection<BridgeFeedbackLogRow> _logRows = [];
    private readonly IDisposable _subscription;

    private BridgeEvent? _footerEvent;
    private BridgeFooterStatus? _footer;
    private int _disposed;

    public BridgeFeedbackPresenter(
        BridgeEventHub eventHub,
        IBridgeFeedbackFormatter formatter,
        IOverlayPresenter overlayPresenter,
        int maximumLogRows = MaximumLogRows)
        : this(
            eventHub,
            formatter,
            overlayPresenter,
            SynchronizationContext.Current,
            maximumLogRows)
    {
    }

    public BridgeFeedbackPresenter(
        BridgeEventHub eventHub,
        IBridgeFeedbackFormatter formatter,
        IOverlayPresenter overlayPresenter,
        SynchronizationContext? synchronizationContext,
        int maximumLogRows = MaximumLogRows)
    {
        ArgumentNullException.ThrowIfNull(eventHub);
        ArgumentNullException.ThrowIfNull(formatter);
        ArgumentNullException.ThrowIfNull(overlayPresenter);
        ArgumentOutOfRangeException.ThrowIfLessThan(maximumLogRows, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(
            maximumLogRows,
            MaximumLogRows);

        _formatter = formatter;
        _overlayPresenter = overlayPresenter;
        _synchronizationContext = synchronizationContext;
        _ownerThreadId = Environment.CurrentManagedThreadId;
        _maximumLogRows = maximumLogRows;
        LogRows = new ReadOnlyObservableCollection<BridgeFeedbackLogRow>(
            _logRows);
        _subscription = eventHub.Subscribe(Receive);
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public ReadOnlyObservableCollection<BridgeFeedbackLogRow> LogRows { get; }

    public BridgeFooterStatus? Footer
    {
        get => _footer;
        private set
        {
            if (Equals(_footer, value))
            {
                return;
            }

            _footer = value;
            OnPropertyChanged();
        }
    }

    /// <summary>
    /// Re-renders retained structured events, for example after a runtime
    /// language change. Toasts are intentionally not replayed.
    /// </summary>
    public void Refresh()
    {
        Dispatch(RefreshCore);
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return;
        }

        _subscription.Dispose();
    }

    private void Receive(BridgeEvent bridgeEvent)
    {
        Dispatch(() => Present(bridgeEvent));
    }

    private void Present(BridgeEvent bridgeEvent)
    {
        var content = FormatSafely(bridgeEvent);

        _retainedEvents.Insert(0, bridgeEvent);
        _logRows.Insert(
            0,
            new BridgeFeedbackLogRow(bridgeEvent, content.LogText));
        TrimRetainedRows();

        var metadata = bridgeEvent.Overlay;
        if (metadata is null)
        {
            return;
        }

        if (TargetsFooter(metadata.Target))
        {
            _footerEvent = bridgeEvent;
            Footer = CreateFooter(bridgeEvent, content);
        }

        if (TargetsToast(metadata.Target) && content.Toast is not null)
        {
            try
            {
                _overlayPresenter.Present(
                    new BridgeOverlayRequest(
                        bridgeEvent,
                        content.Toast.Title,
                        content.Toast.Value,
                        metadata.Duration,
                        metadata.CoalesceKey));
            }
            catch
            {
                // Feedback rendering is best effort. A broken overlay must not
                // interrupt controller input or later event presentation.
            }
        }
    }

    private void RefreshCore()
    {
        var refreshedRows = _retainedEvents
            .Select(bridgeEvent =>
            {
                var content = FormatSafely(bridgeEvent);
                return new BridgeFeedbackLogRow(
                    bridgeEvent,
                    content.LogText);
            })
            .ToArray();

        BridgeFooterStatus? refreshedFooter = null;
        if (_footerEvent is not null)
        {
            var content = FormatSafely(_footerEvent);
            refreshedFooter = CreateFooter(_footerEvent, content);
        }

        _logRows.Clear();
        foreach (var row in refreshedRows)
        {
            _logRows.Add(row);
        }

        Footer = refreshedFooter;
    }

    private BridgeFeedbackContent FormatSafely(BridgeEvent bridgeEvent)
    {
        try
        {
            return _formatter.Format(bridgeEvent);
        }
        catch
        {
            // Locale-neutral last-resort text. The active formatter normally
            // supplies localized fallback content; this path is only for a
            // formatter bug and must not leak one language into another.
            var text = $"Agent Controller · {bridgeEvent.Key.Value}";
            return new BridgeFeedbackContent(text, text);
        }
    }

    private static BridgeFooterStatus CreateFooter(
        BridgeEvent bridgeEvent,
        BridgeFeedbackContent content)
    {
        return new BridgeFooterStatus(
            bridgeEvent,
            content.FooterText ?? content.LogText);
    }

    private void TrimRetainedRows()
    {
        while (_retainedEvents.Count > _maximumLogRows)
        {
            _retainedEvents.RemoveAt(_retainedEvents.Count - 1);
        }

        while (_logRows.Count > _maximumLogRows)
        {
            _logRows.RemoveAt(_logRows.Count - 1);
        }
    }

    private void Dispatch(Action action)
    {
        if (Volatile.Read(ref _disposed) != 0)
        {
            return;
        }

        if (
            _synchronizationContext is not null &&
            Environment.CurrentManagedThreadId != _ownerThreadId)
        {
            _synchronizationContext.Post(
                static state =>
                {
                    var work = (PendingPresentation)state!;
                    work.Owner.RunIfActive(work.Action);
                },
                new PendingPresentation(this, action));
            return;
        }

        RunIfActive(action);
    }

    private void RunIfActive(Action action)
    {
        if (Volatile.Read(ref _disposed) != 0)
        {
            return;
        }

        lock (_presentationGate)
        {
            if (Volatile.Read(ref _disposed) == 0)
            {
                action();
            }
        }
    }

    private static bool TargetsFooter(BridgeOverlayTarget target)
    {
        return target is
            BridgeOverlayTarget.Footer or
            BridgeOverlayTarget.FooterAndToast;
    }

    private static bool TargetsToast(BridgeOverlayTarget target)
    {
        return target is
            BridgeOverlayTarget.Toast or
            BridgeOverlayTarget.FooterAndToast;
    }

    private void OnPropertyChanged(
        [CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(
            this,
            new PropertyChangedEventArgs(propertyName));
    }

    private sealed record PendingPresentation(
        BridgeFeedbackPresenter Owner,
        Action Action);
}
