using System.Collections.Concurrent;
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Windows.Automation;

namespace CodexMicro.Desktop.Services;

internal sealed class CodexQuestionSkipObserver : IDisposable
{
    private sealed record RefreshRequest(
        IReadOnlyList<CodexMonitoredQuestion> Questions,
        int Generation,
        TaskCompletionSource Completion);

    private sealed class Subscription(
        AutomationElement button, CodexMonitoredQuestion question)
    {
        internal AutomationElement Button { get; } = button;
        internal CodexMonitoredQuestion Question { get; } = question;
        internal AutomationEventHandler Handler { get; set; } = null!;
        internal int Active = 1;
    }

    private readonly object _sync = new();
    private readonly BlockingCollection<RefreshRequest> _requests = new();
    private readonly Dictionary<string, Subscription> _subscriptions = new(StringComparer.Ordinal);
    private Thread? _worker;
    private int _generation;
    private volatile bool _disposed;

    internal event Action<CodexMonitoredQuestion>? QuestionSkipped;

    internal Task RefreshAsync(IReadOnlyList<CodexMonitoredQuestion> questions)
    {
        lock (_sync)
        {
            if (_disposed || (_worker is null && questions.Count == 0))
            {
                return Task.CompletedTask;
            }

            var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            _requests.Add(new(questions, Interlocked.Increment(ref _generation), completion));
            if (_worker is null)
            {
                _worker = new Thread(Observe) { IsBackground = true, Name = nameof(CodexQuestionSkipObserver) };
                _worker.SetApartmentState(ApartmentState.MTA);
                _worker.Start();
            }
            return completion.Task;
        }
    }

    private void Observe()
    {
        try
        {
            foreach (var request in _requests.GetConsumingEnumerable())
            {
                try
                {
                    if (request.Generation == Volatile.Read(ref _generation))
                    {
                        Refresh(request);
                    }
                }
                catch (Exception exception) when (IsUnavailable(exception))
                {
                    RemoveSubscriptions([]);
                }
                finally
                {
                    request.Completion.TrySetResult();
                }
            }
        }
        finally
        {
            RemoveSubscriptions([]);
            _requests.Dispose();
        }
    }

    private void Refresh(RefreshRequest request)
    {
        var matches = new Dictionary<string, (AutomationElement Button, CodexMonitoredQuestion Question)>();
        var window = request.Questions.Count == 0
            ? nint.Zero : CodexWindowActivator.CaptureForegroundWindow();
        if (window != nint.Zero)
        {
            var root = AutomationElement.FromHandle(window);
            var buttons = root.FindAll(TreeScope.Descendants,
                new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Button));
            foreach (AutomationElement button in buttons)
            {
                if (request.Generation != Volatile.Read(ref _generation))
                {
                    return;
                }
                if (button.Current.IsOffscreen || !button.Current.IsEnabled || !IsSkip(button.Current.Name))
                {
                    continue;
                }

                var card = TreeWalker.ControlViewWalker.GetParent(button);
                for (var depth = 0; card is not null && depth < 16; depth++)
                {
                    if ((card.Current.ClassName ?? string.Empty).Contains(
                            "@container/request-card", StringComparison.Ordinal))
                    {
                        var text = card.FindAll(TreeScope.Descendants,
                            new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Text));
                        var visibleText = Normalize(string.Concat(text.Cast<AutomationElement>()
                            .Where(element => !element.Current.IsOffscreen)
                            .Select(element => element.Current.Name)));
                        var questions = request.Questions.Where(question =>
                            Normalize(question.Question.Title) is { Length: > 0 } title &&
                            visibleText.Contains(title, StringComparison.Ordinal)).Take(2).ToArray();
                        if (questions.Length == 1)
                        {
                            matches[string.Join(",", button.GetRuntimeId())] = (button, questions[0]);
                        }
                        break;
                    }
                    card = TreeWalker.ControlViewWalker.GetParent(card);
                }
            }
        }

        if (request.Generation != Volatile.Read(ref _generation))
        {
            return;
        }

        RemoveSubscriptions(matches.Where(pair => _subscriptions.TryGetValue(pair.Key, out var current) &&
            current.Question == pair.Value.Question).Select(pair => pair.Key).ToHashSet(StringComparer.Ordinal));
        foreach (var (key, match) in matches)
        {
            if (_subscriptions.ContainsKey(key))
            {
                continue;
            }

            var subscription = new Subscription(match.Button, match.Question);
            subscription.Handler = (_, _) =>
            {
                if (!_disposed && CodexWindowActivator.IsForegroundWindow(window) &&
                    Interlocked.Exchange(ref subscription.Active, 0) == 1)
                {
                    QuestionSkipped?.Invoke(subscription.Question);
                }
            };
            Automation.AddAutomationEventHandler(InvokePattern.InvokedEvent,
                subscription.Button, TreeScope.Element, subscription.Handler);
            _subscriptions.Add(key, subscription);
        }
    }

    private void RemoveSubscriptions(HashSet<string> retained)
    {
        foreach (var key in _subscriptions.Keys.Where(key => !retained.Contains(key)).ToArray())
        {
            var subscription = _subscriptions[key];
            Interlocked.Exchange(ref subscription.Active, 0);
            try
            {
                Automation.RemoveAutomationEventHandler(InvokePattern.InvokedEvent,
                    subscription.Button, subscription.Handler);
            }
            catch (Exception exception) when (IsUnavailable(exception))
            {
            }
            _subscriptions.Remove(key);
        }
    }

    private static bool IsSkip(string? name) =>
        name is "Skip" or "跳过" or "跳過" or "略過" ||
        name?.StartsWith("Skip, ", StringComparison.OrdinalIgnoreCase) == true ||
        name?.StartsWith("跳过，", StringComparison.Ordinal) == true ||
        name?.StartsWith("跳過，", StringComparison.Ordinal) == true ||
        name?.StartsWith("略過，", StringComparison.Ordinal) == true;

    private static string Normalize(string text) =>
        string.Concat(text.Where(character => !char.IsWhiteSpace(character)));

    private static bool IsUnavailable(Exception exception) =>
        exception is ElementNotAvailableException or InvalidOperationException or
            COMException or Win32Exception or ArgumentException or UnauthorizedAccessException or
            System.Security.SecurityException;

    public void Dispose()
    {
        lock (_sync)
        {
            if (_disposed)
            {
                return;
            }
            _disposed = true;
            Interlocked.Increment(ref _generation);
            _requests.CompleteAdding();
            if (_worker is null)
            {
                _requests.Dispose();
            }
        }
    }
}
