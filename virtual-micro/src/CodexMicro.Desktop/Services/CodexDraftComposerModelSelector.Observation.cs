using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows.Automation;

namespace CodexMicro.Desktop.Services;

internal sealed partial class CodexDraftComposerModelSelector
{
    private CodexModelToggleService.ForegroundDraftPresentationContext? _observationContext;
    private AutomationElement? _observationTrigger;
    private AutomationElement? _observationPowerMenu;
    private long _lastObservationDiscoveryAt;

    internal Task<(CodexQuickModel Model, string? Effort)?> ObserveSelectionAsync(
        CodexModelToggleService.ForegroundDraftPresentationContext context,
        bool includePowerMenu,
        Func<bool> isCurrent,
        CancellationToken cancellationToken) => Task.Run<(CodexQuickModel, string?)?>(() =>
        {
            var previousCatalog = _operationCatalog;
            _operationCatalog = CodexModelCatalog.Load();
            try
            {
                EnsureCurrent(context.Window, isCurrent, cancellationToken);
                if (_observationContext != context)
                {
                    _observationContext = context;
                    _observationTrigger = null;
                    _observationPowerMenu = null;
                    _lastObservationDiscoveryAt = 0;
                }

                var cache = CreateObservationCache();
                var searchedPower = _observationPowerMenu is not null || includePowerMenu;
                if (searchedPower)
                {
                    var power = ReadObservationPowerSelection(RequireRoot(context.Window), cache);
                    if (power.IsOpen)
                    {
                        EnsureCurrent(context.Window, isCurrent, cancellationToken);
                        return power.Selection is { } current ? (current.Model, current.Effort) : null;
                    }
                }

                AutomationElement? snapshot;
                if (_observationTrigger is { } trigger)
                {
                    snapshot = trigger.GetUpdatedCache(cache);
                }
                else
                {
                    if (_lastObservationDiscoveryAt != 0 &&
                        Stopwatch.GetElapsedTime(_lastObservationDiscoveryAt) < TimeSpan.FromSeconds(5))
                    {
                        return null;
                    }

                    _lastObservationDiscoveryAt = Stopwatch.GetTimestamp();
                    snapshot = FindObservationTrigger(RequireRoot(context.Window), cache);
                    _observationTrigger = snapshot;
                }

                if (snapshot is null)
                {
                    return null;
                }
                if (!snapshot.Cached.IsEnabled || snapshot.Cached.IsOffscreen ||
                    snapshot.Cached.BoundingRectangle.IsEmpty)
                {
                    _observationTrigger = null;
                    return null;
                }

                var selection = ParseSelection(ReadObservationText(snapshot));
                if (!searchedPower && (selection.Effort is null ||
                    snapshot.GetCachedPropertyValue(ExpandCollapsePattern.ExpandCollapseStateProperty, true)
                        is ExpandCollapseState.Expanded))
                {
                    var power = ReadObservationPowerSelection(RequireRoot(context.Window), cache);
                    if (power.IsOpen)
                    {
                        EnsureCurrent(context.Window, isCurrent, cancellationToken);
                        return power.Selection is { } current ? (current.Model, current.Effort) : null;
                    }
                }
                EnsureCurrent(context.Window, isCurrent, cancellationToken);
                return selection.Model == CodexQuickModel.Unknown
                    ? null
                    : (selection.Model, selection.Effort);
            }
            catch (Exception exception) when (exception is DraftUiException or
                ElementNotAvailableException or InvalidOperationException or COMException)
            {
                _observationTrigger = null;
                return null;
            }
            finally
            {
                _operationCatalog = previousCatalog;
            }
        }, cancellationToken);

    private static CacheRequest CreateObservationCache()
    {
        var cache = new CacheRequest
        {
            TreeScope = TreeScope.Subtree,
            TreeFilter = System.Windows.Automation.Condition.TrueCondition,
        };
        cache.Add(AutomationElement.NameProperty);
        cache.Add(AutomationElement.HelpTextProperty);
        cache.Add(AutomationElement.ItemStatusProperty);
        cache.Add(AutomationElement.IsEnabledProperty);
        cache.Add(AutomationElement.IsOffscreenProperty);
        cache.Add(AutomationElement.BoundingRectangleProperty);
        cache.Add(ExpandCollapsePattern.ExpandCollapseStateProperty);
        return cache;
    }

    private (bool IsOpen, ComposerSelection? Selection) ReadObservationPowerSelection(
        AutomationElement root, CacheRequest cache)
    {
        if (_observationPowerMenu is { } cachedMenu)
        {
            try
            {
                var snapshot = cachedMenu.GetUpdatedCache(cache);
                if (!snapshot.Cached.IsOffscreen && !snapshot.Cached.BoundingRectangle.IsEmpty)
                {
                    var values = ReadObservationStrings(snapshot);
                    if (IsObservationPowerMenu(values))
                    {
                        return (true, snapshot.Cached.IsEnabled ? ParseObservationPowerSelection(values) : null);
                    }
                }
            }
            catch (Exception exception) when (exception is
                ElementNotAvailableException or InvalidOperationException or COMException)
            {
            }
            _observationPowerMenu = null;
        }

        AutomationElementCollection menus;
        using (cache.Activate())
        {
            menus = root.FindAll(TreeScope.Descendants, new AndCondition(
                new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Menu),
                new PropertyCondition(AutomationElement.IsOffscreenProperty, false)));
        }

        var candidates = new List<(AutomationElement Menu, IReadOnlyList<string> Values)>();
        foreach (AutomationElement menu in menus)
        {
            var values = ReadObservationStrings(menu);
            if (IsObservationPowerMenu(values))
            {
                candidates.Add((menu, values));
            }
        }

        if (candidates.Count != 1)
        {
            return (candidates.Count > 0, null);
        }

        _observationPowerMenu = candidates[0].Menu;
        return (true, _observationPowerMenu.Cached.IsEnabled
            ? ParseObservationPowerSelection(candidates[0].Values) : null);
    }

    private static bool IsObservationPowerMenu(IReadOnlyList<string> values) =>
        values.Any(value => string.Equals(value, "Power", StringComparison.OrdinalIgnoreCase));

    private static ComposerSelection? ParseObservationPowerSelection(IReadOnlyList<string> values)
    {
        var distinct = values.Select(ParseSelection)
            .Where(selection => selection.Model != CodexQuickModel.Unknown && selection.Effort is not null &&
                selection.Position > 0 && selection.Position <= selection.Count)
            .Distinct().ToArray();
        return distinct.Length == 1 ? distinct[0] : null;
    }

    private static AutomationElement? FindObservationTrigger(AutomationElement root, CacheRequest cache)
    {
        var rootRectangle = root.Current.BoundingRectangle;
        AutomationElementCollection buttons;
        using (cache.Activate())
        {
            buttons = root.FindAll(TreeScope.Descendants, new AndCondition(
                new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Button),
                new PropertyCondition(AutomationElement.IsExpandCollapsePatternAvailableProperty, true),
                new PropertyCondition(AutomationElement.IsEnabledProperty, true),
                new PropertyCondition(AutomationElement.IsOffscreenProperty, false)));
        }

        var candidates = new List<TriggerCandidate>();
        foreach (AutomationElement button in buttons)
        {
            var rectangle = button.Cached.BoundingRectangle;
            if (rectangle.IsEmpty || rectangle.Width <= 0 || rectangle.Height <= 0) continue;

            var text = ReadObservationText(button);
            var selection = ParseSelection(text);
            var knownLabel = text.Contains("Select model", StringComparison.OrdinalIgnoreCase) ||
                text.Contains("Select effort", StringComparison.OrdinalIgnoreCase) ||
                text.Contains("Select ChatGPT model", StringComparison.OrdinalIgnoreCase);
            if (selection.Model == CodexQuickModel.Unknown && !knownLabel)
            {
                continue;
            }

            var score = selection.Model == CodexQuickModel.Unknown ? 50 : 100;
            if (!rootRectangle.IsEmpty && rectangle.Top >= rootRectangle.Top + rootRectangle.Height * 0.45)
            {
                score += 20;
            }
            candidates.Add(new(button, score, rectangle.IsEmpty ? double.MaxValue : rectangle.Width * rectangle.Height));
        }

        var ordered = candidates.OrderByDescending(candidate => candidate.Score)
            .ThenBy(candidate => candidate.Area).ToArray();
        return ordered.Length == 0 || (ordered.Length > 1 &&
            ordered[0].Score == ordered[1].Score && Math.Abs(ordered[0].Area - ordered[1].Area) < 0.5)
                ? null
                : ordered[0].Element;
    }

    private static string ReadObservationText(AutomationElement root) =>
        string.Join(" ", ReadObservationStrings(root));

    private static IReadOnlyList<string> ReadObservationStrings(AutomationElement root)
    {
        var pending = new Stack<AutomationElement>();
        var values = new HashSet<string>(StringComparer.Ordinal);
        pending.Push(root);
        while (pending.TryPop(out var element))
        {
            foreach (var value in new[] { element.Cached.Name, element.Cached.HelpText, element.Cached.ItemStatus })
            {
                if (!string.IsNullOrWhiteSpace(value))
                {
                    values.Add(value.Trim());
                }
            }

            if (element.CachedChildren is { } children)
            {
                for (var index = children.Count - 1; index >= 0; index--)
                {
                    pending.Push(children[index]);
                }
            }
        }
        return values.ToArray();
    }
}
