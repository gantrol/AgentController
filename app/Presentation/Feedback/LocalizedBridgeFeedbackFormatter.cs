using CodexController.Core.Bridge;
using CodexController.Localization;

namespace CodexController.Presentation.Feedback;

/// <summary>
/// Formats structured bridge events through the application's live
/// localization facade. Because <see cref="LocalizedStrings"/> keeps a stable
/// identity while its backing catalog changes, one formatter instance follows
/// runtime language changes without being rebuilt.
/// </summary>
public sealed class LocalizedBridgeFeedbackFormatter :
    IBridgeFeedbackFormatter
{
    private readonly LocalizedStrings _strings;
    private readonly string _productName;
    private readonly string _agentName;

    public LocalizedBridgeFeedbackFormatter(
        LocalizedStrings strings,
        string productName = "Agent Controller",
        string agentName = "Codex")
    {
        ArgumentNullException.ThrowIfNull(strings);
        ArgumentException.ThrowIfNullOrWhiteSpace(productName);
        ArgumentException.ThrowIfNullOrWhiteSpace(agentName);

        _strings = strings;
        _productName = productName.Trim();
        _agentName = agentName.Trim();
    }

    public BridgeFeedbackContent Format(BridgeEvent bridgeEvent)
    {
        ArgumentNullException.ThrowIfNull(bridgeEvent);

        return bridgeEvent.Key.Value switch
        {
            "app.ready" => Content(
                _strings.StatusReady(_productName),
                _productName),

            "diagnostic.legacy.message" =>
                FormatLegacyMessage(bridgeEvent),

            "controller.device.connected" or
            "controller.connection.restored" =>
                FormatControllerConnection(bridgeEvent, connected: true),

            "controller.device.disconnected" or
            "controller.connection.lost" =>
                FormatControllerConnection(bridgeEvent, connected: false),

            "controller.session.armed" => ControllerStatus(
                _strings.StatusControllerArmed),

            "controller.session.locked" => ControllerStatus(
                _strings.StatusControllerLocked),

            "controller.session.paused" => ControllerStatus(
                _strings.StatusControllerPaused),

            "controller.session.resumed" => ControllerStatus(
                _strings.StatusControllerResumed),

            "codex.wake.started" or
            "codex.wake.requested" => Content(
                _strings.FeedbackWakeStarted(_agentName),
                _agentName),

            "codex.wake.succeeded" => Content(
                _strings.FeedbackWakeSucceeded(_agentName),
                _agentName),

            "codex.wake.failed" => FormatWakeFailure(bridgeEvent),

            "sidebar.scope.changed" or
            "navigation.scope.changed" =>
                FormatSidebarScope(bridgeEvent),

            "sidebar.focus.changed" or
            "navigation.focus.changed" =>
                FormatSidebarFocus(bridgeEvent),

            "sidebar.entry.opened" =>
                FormatSidebarEntryOpened(bridgeEvent),

            "sidebar.navigation.undone" => Content(
                _strings.FeedbackNavigationUndone,
                _strings.SidebarAgent(_agentName)),

            "model.selection.changed" => FormatValueChange(
                bridgeEvent,
                _strings.Model,
                ["model", "value", "label"]),

            "model.reasoning-effort.changed" => FormatValueChange(
                bridgeEvent,
                _strings.ReasoningEffort,
                ["reasoning", "effort", "value", "label"],
                _strings.ReasoningValue),

            "model.speed.changed" => FormatValueChange(
                bridgeEvent,
                _strings.Speed,
                ["speed", "value", "label"],
                _strings.SpeedValue),

            "composer.dictation.started" => Content(
                _strings.FeedbackListening,
                _strings.ConfigDictation),

            "composer.dictation.stopped" => Content(
                _strings.FeedbackDictationEnded,
                _strings.ConfigDictation),

            "composer.prompt.sent" => Content(
                _strings.FeedbackPromptSent,
                _agentName),

            "composer.action.cancelled" => Content(
                _strings.FeedbackSelectionCanceled,
                _agentName),

            "automation.action.failed" =>
                FormatAutomationFailure(bridgeEvent),

            _ => FormatFallback(bridgeEvent),
        };
    }

    private BridgeFeedbackContent FormatControllerConnection(
        BridgeEvent bridgeEvent,
        bool connected)
    {
        var device = FirstParameter(
            bridgeEvent,
            "device",
            "controller",
            "name");
        var restored = BooleanParameter(bridgeEvent, "restored");
        var requiresNeutral = BooleanParameter(
            bridgeEvent,
            "requiresNeutral");
        var autoResume = BooleanParameter(bridgeEvent, "autoResume");

        var status = connected
            ? _strings.DeviceConnected(
                device ?? _strings.DeviceGamepadBridge)
            : _strings.DeviceDisconnected;
        var instruction = connected
            ? requiresNeutral
                ? _strings.StatusControllerLocked
                : restored
                    ? _strings.StatusControllerResumed
                    : null
            : autoResume
                ? _strings.WaitingForReconnect
                : null;
        var logText = Join(status, instruction) ?? status;

        return Content(
            logText,
            _strings.DeviceGamepadBridge,
            Join(device, instruction) ?? logText);
    }

    private BridgeFeedbackContent FormatLegacyMessage(
        BridgeEvent bridgeEvent)
    {
        var text = FirstParameter(bridgeEvent, "text");
        return Content(
            text ?? FallbackText(bridgeEvent.Key.Value),
            _productName,
            text ?? _strings.FeedbackStatusUpdated);
    }

    private BridgeFeedbackContent FormatWakeFailure(
        BridgeEvent bridgeEvent)
    {
        var failure = _strings.FeedbackWakeFailed(_agentName);
        var reasonCode = FirstParameter(
            bridgeEvent,
            "reasonCode",
            "errorCode");
        var legacyReason = FirstParameter(
            bridgeEvent,
            "reason",
            "error");
        var errorDetail = FirstParameter(
            bridgeEvent,
            "errorDetail",
            "detail");
        var reason = reasonCode is null
            ? legacyReason
            : _strings.ErrorLabel(reasonCode);
        var logText = LabelValue(
            failure,
            Join(reason, errorDetail));

        return Content(
            logText,
            _agentName,
            reason ?? failure);
    }

    private BridgeFeedbackContent FormatSidebarScope(
        BridgeEvent bridgeEvent)
    {
        var rawScope = FirstParameter(
            bridgeEvent,
            "scope",
            "value");
        var scope = rawScope is null
            ? _strings.FeedbackStatusUpdated
            : _strings.ScopeValue(rawScope);
        var project = FirstParameter(bridgeEvent, "project");
        var projectTitle = string.IsNullOrWhiteSpace(project)
            ? null
            : _strings.Format(
                StringKeys.MessageProjectTitle,
                project.Trim());

        return Content(
            Join(
                _strings.FeedbackScopeChanged(scope),
                projectTitle)!,
            projectTitle ?? _strings.SidebarAgent(_agentName),
            scope);
    }

    private BridgeFeedbackContent FormatSidebarFocus(
        BridgeEvent bridgeEvent)
    {
        var label = FirstParameter(
                bridgeEvent,
                "label",
                "title",
                "name",
                "value")
            ?? _strings.FeedbackStatusUpdated;

        return Content(
            _strings.FeedbackFocusChanged(label),
            _strings.SidebarAgent(_agentName),
            label);
    }

    private BridgeFeedbackContent FormatSidebarEntryOpened(
        BridgeEvent bridgeEvent)
    {
        var label = FirstParameter(
                bridgeEvent,
                "label",
                "title",
                "name",
                "value")
            ?? _strings.FeedbackStatusUpdated;

        return Content(
            _strings.FeedbackEntryOpened(label),
            _strings.SidebarAgent(_agentName),
            label);
    }

    private BridgeFeedbackContent FormatValueChange(
        BridgeEvent bridgeEvent,
        string category,
        string[] parameterKeys,
        Func<string, string>? formatValue = null)
    {
        var rawValue = FirstParameter(bridgeEvent, parameterKeys);
        var value = rawValue is null
            ? _strings.FeedbackStatusUpdated
            : formatValue?.Invoke(rawValue) ?? rawValue;
        var logText = _strings.FeedbackSelectionApplied(
            category,
            value);

        return Content(logText, category, value);
    }

    private BridgeFeedbackContent FormatAutomationFailure(
        BridgeEvent bridgeEvent)
    {
        var action = FirstParameter(
            bridgeEvent,
            "action",
            "operation");
        var errorCode = FirstParameter(
            bridgeEvent,
            "errorCode",
            "reasonCode");
        var legacyReason = FirstParameter(
            bridgeEvent,
            "reason",
            "error");
        var errorDetail = FirstParameter(
            bridgeEvent,
            "errorDetail",
            "detail");
        var reason = errorCode is null
            ? legacyReason
            : _strings.ErrorLabel(errorCode);
        var detail = Join(
            Join(action, reason),
            errorDetail);
        var logText = LabelValue(
            _strings.FeedbackOperationFailed,
            detail);

        return Content(
            logText,
            _strings.FeedbackOperationFailed,
            reason ?? action ?? _strings.FeedbackOperationFailed);
    }

    private BridgeFeedbackContent FormatFallback(
        BridgeEvent bridgeEvent)
    {
        return Content(
            FallbackText(bridgeEvent.Key.Value),
            _productName,
            _strings.FeedbackStatusUpdated);
    }

    private BridgeFeedbackContent ControllerStatus(string status)
    {
        return Content(
            status,
            _strings.DeviceGamepadBridge,
            status);
    }

    private string FallbackText(string key)
    {
        return _strings.FeedbackSelectionApplied(
            _productName,
            $"{_strings.FeedbackStatusUpdated} ({key})");
    }

    private string LabelValue(string label, string? value)
    {
        return string.IsNullOrWhiteSpace(value)
            ? label
            : _strings.FeedbackSelectionApplied(label, value);
    }

    private static string? Join(
        string? first,
        string? second)
    {
        var values = new[] { first, second }
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .ToArray();
        return values.Length == 0
            ? null
            : string.Join(" · ", values);
    }

    private static string? FirstParameter(
        BridgeEvent bridgeEvent,
        params string[] keys)
    {
        foreach (var key in keys)
        {
            if (
                bridgeEvent.Parameters.TryGetValue(key, out var value) &&
                !string.IsNullOrWhiteSpace(value))
            {
                return value.Trim();
            }
        }

        return null;
    }

    private static bool BooleanParameter(
        BridgeEvent bridgeEvent,
        string key)
    {
        return
            bridgeEvent.Parameters.TryGetValue(key, out var value) &&
            bool.TryParse(value, out var parsed) &&
            parsed;
    }

    private static BridgeFeedbackContent Content(
        string logText,
        string toastTitle,
        string? toastValue = null)
    {
        return new BridgeFeedbackContent(
            logText,
            logText,
            new BridgeToastText(
                toastTitle,
                toastValue ?? logText));
    }
}
