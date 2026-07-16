using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Windows.Automation;
using CodexController.Agents;
using CodexController.Models;
using CodexController.Native;

namespace CodexController.Services;

public enum ComposerSettingKind
{
    Model,
    Effort,
    Speed,
}

public sealed record ComposerModelOption(
    string Slug,
    string DisplayName,
    IReadOnlyList<string> Efforts);

public sealed class ComposerCatalog
{
    private static readonly string[] FallbackEfforts =
        ["Light", "Medium", "High", "Extra High", "Max", "Ultra"];

    public required IReadOnlyList<ComposerModelOption> Models { get; init; }
    public required int InitialModelIndex { get; init; }
    public required string InitialEffort { get; init; }
    public required string InitialSpeed { get; init; }

    public IReadOnlyList<string> EffortsForModel(int modelIndex)
    {
        if (Models.Count == 0)
        {
            return FallbackEfforts;
        }

        var safeIndex = Math.Clamp(modelIndex, 0, Models.Count - 1);
        var values = Models[safeIndex].Efforts;
        return values.Count > 0 ? values : FallbackEfforts;
    }
}

public sealed record ComposerAutomationResult(
    bool Succeeded,
    string? Error = null,
    string? ErrorDetail = null)
{
    public AgentAutomationError? Failure =>
        Error is null
            ? null
            : new AgentAutomationError(Error, ErrorDetail);
}

public sealed partial class CodexComposerService
{
    private const string ComposerButtonClassToken =
        "h-token-button-composer";
    private static readonly IReadOnlyList<ComposerModelOption> FallbackModels =
    [
        new("gpt-5.5", "5.5",
            ["Light", "Medium", "High", "Extra High"]),
        new("gpt-5.6-sol", "5.6 Sol",
            ["Light", "Medium", "High", "Extra High", "Max", "Ultra"]),
        new("gpt-5.6-terra", "5.6 Terra",
            ["Light", "Medium", "High", "Extra High", "Max", "Ultra"]),
        new("gpt-5.6-luna", "5.6 Luna",
            ["Light", "Medium", "High", "Extra High", "Max"]),
        new("gpt-5.4", "5.4",
            ["Light", "Medium", "High", "Extra High"]),
        new("gpt-5.4-mini", "5.4 Mini",
            ["Light", "Medium", "High", "Extra High"]),
        new("gpt-5.3-codex-spark", "5.3 Codex Spark",
            ["Light", "Medium", "High", "Extra High"]),
    ];

    public ComposerCatalog LoadCatalog()
    {
        var codexHome = ResolveCodexHome();
        var models = LoadModels(Path.Combine(codexHome, "models_cache.json"));
        if (models.Count == 0)
        {
            models = FallbackModels;
        }

        var preferences = ReadConfig(Path.Combine(codexHome, "config.toml"));
        var buttonName = TryReadComposerButtonName();

        var modelIndex = FindModelIndex(
            models,
            buttonName,
            preferences.ModelSlug);
        var modelEfforts = models[modelIndex].Efforts;
        var effort = FindEffort(
            modelEfforts,
            buttonName,
            EffortLabel(preferences.Effort));

        return new ComposerCatalog
        {
            Models = models,
            InitialModelIndex = modelIndex,
            InitialEffort = effort,
            InitialSpeed = FindSpeed(
                buttonName,
                preferences.ServiceTier),
        };
    }

    public Task<ComposerAutomationResult> SelectAsync(
        ComposerSettingKind kind,
        string target,
        AppSettings settings,
        CancellationToken cancellationToken)
    {
        return Task.Run(
            () => SelectCore(kind, target, settings, cancellationToken),
            cancellationToken);
    }

    public string? TryReadComposerButtonName()
    {
        try
        {
            var context = FindCodexWindow();
            var button =
                context is null
                    ? null
                    : FindComposerButton(context.Value.Window);
            return button?.Current.Name;
        }
        catch
        {
            return null;
        }
    }

    public ComposerAutomationResult InvokeComposerAction(
        AppSettings settings,
        params string[] actionNames)
    {
        if (!settings.BridgeEnabled)
        {
            return new(
                false,
                AgentAutomationErrorCodes.BridgeSafePreview);
        }

        if (
            settings.OnlyWhenCodexForeground &&
            !Win32Input.IsCodexForeground())
        {
            return new(
                false,
                AgentAutomationErrorCodes.AgentNotForeground);
        }

        try
        {
            var context = FindCodexWindow();
            if (context is null)
            {
                return new(
                    false,
                    AgentAutomationErrorCodes.AgentWindowNotFound);
            }

            var targets = actionNames
                .Where(name => !string.IsNullOrWhiteSpace(name))
                .Select(NormalizeChoice)
                .ToHashSet(StringComparer.Ordinal);
            var buttons = context.Value.Window.FindAll(
                TreeScope.Descendants,
                new PropertyCondition(
                    AutomationElement.ControlTypeProperty,
                    ControlType.Button));
            foreach (AutomationElement button in buttons)
            {
                string normalizedName;
                try
                {
                    if (
                        !button.Current.IsEnabled ||
                        button.Current.IsOffscreen ||
                        button.Current.BoundingRectangle.IsEmpty)
                    {
                        continue;
                    }

                    normalizedName = NormalizeChoice(button.Current.Name);
                }
                catch (ElementNotAvailableException)
                {
                    continue;
                }

                if (
                    !targets.Contains(normalizedName) ||
                    !button.TryGetCurrentPattern(
                        InvokePattern.Pattern,
                        out var patternObject) ||
                    patternObject is not InvokePattern pattern)
                {
                    continue;
                }

                pattern.Invoke();
                return new(true);
            }

            return new(
                false,
                AgentAutomationErrorCodes.ElementNotFound,
                $"action:{string.Join("|", actionNames)}");
        }
        catch (Exception exception)
        {
            return new(
                false,
                AgentAutomationErrorCodes.Unexpected,
                exception.Message);
        }
    }

    public Task<ComposerAutomationResult> InvokeComposerActionAsync(
        AppSettings settings,
        int timeoutMs,
        CancellationToken cancellationToken,
        params string[] actionNames)
    {
        return Task.Run(() =>
        {
            var deadline = Environment.TickCount64 + timeoutMs;
            ComposerAutomationResult result;
            do
            {
                cancellationToken.ThrowIfCancellationRequested();
                result = InvokeComposerAction(settings, actionNames);
                if (result.Succeeded)
                {
                    return result;
                }

                if (AgentAutomationErrorCodes.IsImmediateFailure(
                        result.Error))
                {
                    return result;
                }

                Thread.Sleep(45);
            }
            while (Environment.TickCount64 < deadline);

            return result;
        }, cancellationToken);
    }

    public ComposerAutomationResult SubmitComposer(AppSettings settings)
    {
        if (!settings.BridgeEnabled)
        {
            return new(
                false,
                AgentAutomationErrorCodes.BridgeSafePreview);
        }

        if (
            settings.OnlyWhenCodexForeground &&
            !Win32Input.IsCodexForeground())
        {
            return new(
                false,
                AgentAutomationErrorCodes.AgentNotForeground);
        }

        try
        {
            var context = FindCodexWindow();
            if (context is null)
            {
                return new(
                    false,
                    AgentAutomationErrorCodes.AgentWindowNotFound);
            }

            var namedSubmit = InvokeNamedButton(
                context.Value.Window,
                ["Send", "Send message", "Submit", "Submit prompt",
                 "Transcribe and send"]);
            if (namedSubmit.Succeeded)
            {
                return namedSubmit;
            }

            var editor = FindComposerEditor(context.Value.Window);
            if (editor is null)
            {
                return new(
                    false,
                    AgentAutomationErrorCodes.ElementNotFound,
                    "composer-editor");
            }

            if (
                editor.TryGetCurrentPattern(
                    TextPattern.Pattern,
                    out var textObject) &&
                textObject is TextPattern textPattern &&
                string.IsNullOrWhiteSpace(
                    textPattern.DocumentRange.GetText(-1)))
            {
                return new(
                    false,
                    AgentAutomationErrorCodes.ComposerEmpty);
            }

            editor.SetFocus();
            Thread.Sleep(45);
            if (
                !Win32Input.IsCodexForeground() &&
                !Win32Input.FocusCodexAndWait())
            {
                return new(
                    false,
                    AgentAutomationErrorCodes.FocusRejected,
                    "composer-editor");
            }

            editor.SetFocus();
            Thread.Sleep(25);
            return Win32Input.SendKey(0x0D)
                ? new(true)
                : new(
                    false,
                    AgentAutomationErrorCodes.InputInjectionFailed,
                    "Enter");
        }
        catch (Exception exception)
        {
            return new(
                false,
                AgentAutomationErrorCodes.Unexpected,
                exception.Message);
        }
    }

    public ComposerAutomationResult CancelComposer(AppSettings settings)
    {
        if (!settings.BridgeEnabled)
        {
            return new(
                false,
                AgentAutomationErrorCodes.BridgeSafePreview);
        }

        if (
            settings.OnlyWhenCodexForeground &&
            !Win32Input.IsCodexForeground())
        {
            return new(
                false,
                AgentAutomationErrorCodes.AgentNotForeground);
        }

        try
        {
            var context = FindCodexWindow();
            if (context is null)
            {
                return new(
                    false,
                    AgentAutomationErrorCodes.AgentWindowNotFound);
            }

            var namedCancel = InvokeNamedButton(
                context.Value.Window,
                ["Stop", "Cancel", "Cancel request"]);
            if (namedCancel.Succeeded)
            {
                return namedCancel;
            }

            var editor = FindComposerEditor(context.Value.Window);
            if (editor is not null)
            {
                editor.SetFocus();
                Thread.Sleep(45);
            }
            else if (!Win32Input.FocusCodexAndWait())
            {
                return new(
                    false,
                    AgentAutomationErrorCodes.FocusRejected,
                    "composer-editor");
            }

            return Win32Input.SendKey(0x1B)
                ? new(true)
                : new(
                    false,
                    AgentAutomationErrorCodes.InputInjectionFailed,
                    "Escape");
        }
        catch (Exception exception)
        {
            return new(
                false,
                AgentAutomationErrorCodes.Unexpected,
                exception.Message);
        }
    }

    private static ComposerAutomationResult InvokeNamedButton(
        AutomationElement window,
        IReadOnlyCollection<string> actionNames)
    {
        var targets = actionNames
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .Select(NormalizeChoice)
            .ToHashSet(StringComparer.Ordinal);
        var buttons = window.FindAll(
            TreeScope.Descendants,
            new PropertyCondition(
                AutomationElement.ControlTypeProperty,
                ControlType.Button));
        foreach (AutomationElement button in buttons)
        {
            try
            {
                if (
                    !button.Current.IsEnabled ||
                    button.Current.IsOffscreen ||
                    button.Current.BoundingRectangle.IsEmpty ||
                    !targets.Contains(NormalizeChoice(button.Current.Name)) ||
                    !button.TryGetCurrentPattern(
                        InvokePattern.Pattern,
                        out var patternObject) ||
                    patternObject is not InvokePattern pattern)
                {
                    continue;
                }

                pattern.Invoke();
                return new(true);
            }
            catch (ElementNotAvailableException)
            {
                // Continue looking if Chromium replaced a button mid-query.
            }
        }

        return new(
            false,
            AgentAutomationErrorCodes.ElementNotFound,
            $"action:{string.Join("|", actionNames)}");
    }

    private static AutomationElement? FindComposerEditor(
        AutomationElement window)
    {
        var groups = window.FindAll(
            TreeScope.Descendants,
            new PropertyCondition(
                AutomationElement.ControlTypeProperty,
                ControlType.Group));
        foreach (AutomationElement group in groups)
        {
            try
            {
                var className = group.Current.ClassName ?? string.Empty;
                if (
                    group.Current.IsEnabled &&
                    !group.Current.IsOffscreen &&
                    !group.Current.BoundingRectangle.IsEmpty &&
                    className
                        .Split(' ', StringSplitOptions.RemoveEmptyEntries)
                        .Contains("ProseMirror", StringComparer.Ordinal))
                {
                    return group;
                }
            }
            catch (ElementNotAvailableException)
            {
                // Continue looking for the live editor.
            }
        }

        return null;
    }

    private static ComposerAutomationResult SelectCore(
        ComposerSettingKind kind,
        string target,
        AppSettings settings,
        CancellationToken cancellationToken)
    {
        if (!settings.BridgeEnabled)
        {
            return new(
                false,
                AgentAutomationErrorCodes.BridgeSafePreview);
        }

        if (
            settings.OnlyWhenCodexForeground &&
            !Win32Input.IsCodexForeground())
        {
            return new(
                false,
                AgentAutomationErrorCodes.AgentNotForeground);
        }

        if (
            !settings.OnlyWhenCodexForeground &&
            !Win32Input.IsCodexForeground())
        {
            _ = Win32Input.FocusCodex();
            Thread.Sleep(90);
        }

        AutomationElement? composerButton = null;
        AutomationElement? categoryItem = null;
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            var context = FindCodexWindow();
            if (context is null)
            {
                return new(
                    false,
                    AgentAutomationErrorCodes.AgentWindowNotFound);
            }

            composerButton = FindComposerButton(context.Value.Window);
            if (composerButton is null)
            {
                return new(
                    false,
                    AgentAutomationErrorCodes.ElementNotFound,
                    "composer-model-button");
            }

            if (!TryExpand(composerButton))
            {
                return new(
                    false,
                    AgentAutomationErrorCodes.ElementUnsupported,
                    "composer-model-button:expand");
            }

            var category = CategoryLabel(kind);
            categoryItem = WaitForMenuItem(
                context.Value.Window,
                context.Value.ProcessId,
                name =>
                    name.Equals(category, StringComparison.OrdinalIgnoreCase) ||
                    name.StartsWith(
                        $"{category} ",
                        StringComparison.OrdinalIgnoreCase),
                cancellationToken);
            if (categoryItem is null || !TryExpand(categoryItem))
            {
                return new(
                    false,
                    AgentAutomationErrorCodes.ElementNotFound,
                    $"composer-submenu:{category}");
            }

            var option = WaitForBestOption(
                context.Value.Window,
                context.Value.ProcessId,
                category,
                target,
                cancellationToken);
            if (option is null)
            {
                return new(
                    false,
                    AgentAutomationErrorCodes.ElementNotFound,
                    $"composer-option:{target}");
            }

            cancellationToken.ThrowIfCancellationRequested();
            if (
                !option.TryGetCurrentPattern(
                    InvokePattern.Pattern,
                    out var invokeObject) ||
                invokeObject is not InvokePattern invoke)
            {
                return new(
                    false,
                    AgentAutomationErrorCodes.ElementUnsupported,
                    $"composer-option:{target}:select");
            }

            // Only the final settled choice is invoked; previews never touch Codex.
            invoke.Invoke();
            return new(true);
        }
        catch (OperationCanceledException)
        {
            return new(
                false,
                AgentAutomationErrorCodes.OperationCanceled);
        }
        catch (ElementNotAvailableException)
        {
            return new(
                false,
                AgentAutomationErrorCodes.AutomationStale,
                "composer-menu");
        }
        catch (Exception exception)
        {
            return new(
                false,
                AgentAutomationErrorCodes.Unexpected,
                exception.Message);
        }
        finally
        {
            TryCollapse(categoryItem);
            TryCollapse(composerButton);
        }
    }

    private static (AutomationElement Window, int ProcessId)? FindCodexWindow()
    {
        foreach (var process in Process.GetProcessesByName("ChatGPT"))
        {
            if (process.MainWindowHandle == nint.Zero)
            {
                process.Dispose();
                continue;
            }

            var processId = process.Id;
            var handle = process.MainWindowHandle;
            process.Dispose();
            var window = AutomationElement.FromHandle(handle);
            if (window is not null)
            {
                return (window, processId);
            }
        }

        return null;
    }

    private static AutomationElement? FindComposerButton(
        AutomationElement window)
    {
        var buttons = window.FindAll(
            TreeScope.Descendants,
            new PropertyCondition(
                AutomationElement.ControlTypeProperty,
                ControlType.Button));
        foreach (AutomationElement button in buttons)
        {
            string name;
            string className;
            try
            {
                name = button.Current.Name?.Trim() ?? string.Empty;
                className = button.Current.ClassName ?? string.Empty;
                if (
                    !button.Current.IsEnabled ||
                    button.Current.IsOffscreen ||
                    button.Current.BoundingRectangle.IsEmpty)
                {
                    continue;
                }
            }
            catch (ElementNotAvailableException)
            {
                continue;
            }

            if (
                name.Length > 0 &&
                char.IsDigit(name[0]) &&
                className.Contains(
                    ComposerButtonClassToken,
                    StringComparison.OrdinalIgnoreCase))
            {
                return button;
            }
        }

        return null;
    }

    private static AutomationElement? WaitForMenuItem(
        AutomationElement window,
        int processId,
        Func<string, bool> predicate,
        CancellationToken cancellationToken)
    {
        for (var attempt = 0; attempt < 20; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var item = FindMenuItems(window, processId)
                .Where(IsUsableMenuElement)
                .FirstOrDefault(element => predicate(SafeName(element)));
            if (item is not null)
            {
                return item;
            }

            Thread.Sleep(60);
        }

        return null;
    }

    private static AutomationElement? WaitForBestOption(
        AutomationElement window,
        int processId,
        string category,
        string target,
        CancellationToken cancellationToken)
    {
        var normalizedTarget = NormalizeChoice(target);
        for (var attempt = 0; attempt < 20; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var candidates = FindMenuItems(window, processId)
                .Where(IsUsableMenuElement)
                .Select(element => new
                {
                    Element = element,
                    Name = SafeName(element),
                })
                .Where(item =>
                    !item.Name.StartsWith(
                        $"{category} ",
                        StringComparison.OrdinalIgnoreCase))
                .Select(item => new
                {
                    item.Element,
                    item.Name,
                    Normalized = NormalizeChoice(item.Name),
                })
                .Where(item =>
                    item.Normalized.Equals(
                        normalizedTarget,
                        StringComparison.Ordinal) ||
                    item.Normalized.StartsWith(
                        normalizedTarget,
                        StringComparison.Ordinal))
                .OrderBy(item =>
                    item.Normalized.Equals(
                        normalizedTarget,
                        StringComparison.Ordinal)
                        ? 0
                        : 1)
                .ThenBy(item => item.Normalized.Length)
                .ToArray();

            foreach (var candidate in candidates)
            {
                if (
                    candidate.Element.TryGetCurrentPattern(
                        InvokePattern.Pattern,
                        out var pattern) &&
                    pattern is InvokePattern)
                {
                    return candidate.Element;
                }
            }

            Thread.Sleep(60);
        }

        return null;
    }

    private static IEnumerable<AutomationElement> FindMenuItems(
        AutomationElement mainWindow,
        int processId)
    {
        var menuItemCondition = new PropertyCondition(
            AutomationElement.ControlTypeProperty,
            ControlType.MenuItem);
        var processWindowCondition = new PropertyCondition(
            AutomationElement.ProcessIdProperty,
            processId);
        var roots = new List<AutomationElement> { mainWindow };
        var processWindows = AutomationElement.RootElement.FindAll(
            TreeScope.Children,
            processWindowCondition);
        roots.AddRange(
            processWindows
                .Cast<AutomationElement>()
                .Where(item => !item.Equals(mainWindow)));

        foreach (var root in roots)
        {
            AutomationElementCollection collection;
            try
            {
                collection = root.FindAll(
                    TreeScope.Descendants,
                    menuItemCondition);
            }
            catch
            {
                continue;
            }

            foreach (AutomationElement item in collection)
            {
                yield return item;
            }
        }
    }

    private static bool TryExpand(AutomationElement element)
    {
        try
        {
            if (
                !element.TryGetCurrentPattern(
                    ExpandCollapsePattern.Pattern,
                    out var patternObject) ||
                patternObject is not ExpandCollapsePattern pattern)
            {
                return false;
            }

            if (
                pattern.Current.ExpandCollapseState !=
                ExpandCollapseState.Expanded)
            {
                pattern.Expand();
            }

            return true;
        }
        catch
        {
            return false;
        }
    }

    private static void TryCollapse(AutomationElement? element)
    {
        if (element is null)
        {
            return;
        }

        try
        {
            if (
                element.TryGetCurrentPattern(
                    ExpandCollapsePattern.Pattern,
                    out var patternObject) &&
                patternObject is ExpandCollapsePattern pattern &&
                pattern.Current.ExpandCollapseState ==
                ExpandCollapseState.Expanded)
            {
                pattern.Collapse();
            }
        }
        catch
        {
            // Invoking a menu option commonly destroys the popup immediately.
        }
    }

    private static string SafeName(AutomationElement element)
    {
        try
        {
            return element.Current.Name?.Trim() ?? string.Empty;
        }
        catch
        {
            return string.Empty;
        }
    }

    private static bool IsUsableMenuElement(AutomationElement element)
    {
        try
        {
            return element.Current.IsEnabled && !element.Current.IsOffscreen;
        }
        catch
        {
            return false;
        }
    }

    private static IReadOnlyList<ComposerModelOption> LoadModels(string path)
    {
        try
        {
            using var document = JsonDocument.Parse(File.ReadAllText(path));
            if (
                !document.RootElement.TryGetProperty(
                    "models",
                    out var modelsElement) ||
                modelsElement.ValueKind != JsonValueKind.Array)
            {
                return [];
            }

            var models = new List<(
                ComposerModelOption Option,
                int Priority,
                int SourceOrder)>();
            var sourceOrder = 0;
            foreach (var model in modelsElement.EnumerateArray())
            {
                if (
                    GetString(model, "visibility") != "list" ||
                    GetString(model, "slug") is not { Length: > 0 } slug ||
                    GetString(model, "display_name") is not { Length: > 0 }
                        displayName)
                {
                    continue;
                }

                var efforts = new List<string>();
                if (
                    model.TryGetProperty(
                        "supported_reasoning_levels",
                        out var effortElement) &&
                    effortElement.ValueKind == JsonValueKind.Array)
                {
                    foreach (var effort in effortElement.EnumerateArray())
                    {
                        var label = EffortLabel(GetString(effort, "effort"));
                        if (
                            label.Length > 0 &&
                            !efforts.Contains(
                                label,
                                StringComparer.OrdinalIgnoreCase))
                        {
                            efforts.Add(label);
                        }
                    }
                }

                models.Add((
                    new ComposerModelOption(
                        slug,
                        ModelLabel(displayName),
                        efforts),
                    GetInt(model, "priority") ?? int.MaxValue,
                    sourceOrder++));
            }

            return models
                .OrderBy(item => item.Priority)
                .ThenBy(item => item.SourceOrder)
                .Select(item => item.Option)
                .ToArray();
        }
        catch
        {
            return [];
        }
    }

    private static ConfigPreferences ReadConfig(string path)
    {
        try
        {
            var text = File.ReadAllText(path);
            return new(
                MatchTomlString(text, "model"),
                MatchTomlString(text, "model_reasoning_effort"),
                MatchTomlString(text, "service_tier"));
        }
        catch
        {
            return new(null, null, null);
        }
    }

    private static string? MatchTomlString(string text, string key)
    {
        var match = Regex.Match(
            text,
            $@"(?m)^\s*{Regex.Escape(key)}\s*=\s*[""']([^""']+)[""']");
        return match.Success ? match.Groups[1].Value.Trim() : null;
    }

    private static int FindModelIndex(
        IReadOnlyList<ComposerModelOption> models,
        string? buttonName,
        string? configuredSlug)
    {
        if (!string.IsNullOrWhiteSpace(buttonName))
        {
            var normalizedButton = NormalizeChoice(buttonName);
            var match = models
                .Select((model, index) => new
                {
                    Index = index,
                    Length = NormalizeChoice(model.DisplayName).Length,
                    Matches = normalizedButton.StartsWith(
                        NormalizeChoice(model.DisplayName),
                        StringComparison.Ordinal),
                })
                .Where(item => item.Matches)
                .OrderByDescending(item => item.Length)
                .FirstOrDefault();
            if (match is not null)
            {
                return match.Index;
            }
        }

        var configuredIndex = models
            .Select((model, index) => new { model, index })
            .FirstOrDefault(item =>
                string.Equals(
                    item.model.Slug,
                    configuredSlug,
                    StringComparison.OrdinalIgnoreCase))?.index;
        return configuredIndex ?? 0;
    }

    private static string FindEffort(
        IReadOnlyList<string> efforts,
        string? buttonName,
        string configuredEffort)
    {
        if (!string.IsNullOrWhiteSpace(buttonName))
        {
            var normalizedButton = NormalizeChoice(buttonName);
            var fromButton = efforts
                .OrderByDescending(value => value.Length)
                .FirstOrDefault(value =>
                    normalizedButton.EndsWith(
                        NormalizeChoice(value),
                        StringComparison.Ordinal));
            if (fromButton is not null)
            {
                return fromButton;
            }
        }

        return efforts.FirstOrDefault(value =>
                   string.Equals(
                       value,
                       configuredEffort,
                       StringComparison.OrdinalIgnoreCase))
               ?? efforts.FirstOrDefault()
               ?? "Medium";
    }

    private static string FindSpeed(
        string? buttonName,
        string? configuredServiceTier)
    {
        if (!string.IsNullOrWhiteSpace(buttonName))
        {
            var normalized = NormalizeChoice(buttonName);
            if (normalized.EndsWith("standard", StringComparison.Ordinal))
            {
                return "Standard";
            }

            if (normalized.EndsWith("fast", StringComparison.Ordinal))
            {
                return "Fast";
            }
        }

        return string.Equals(
            configuredServiceTier,
            "priority",
            StringComparison.OrdinalIgnoreCase)
            ? "Fast"
            : "Standard";
    }

    private static string ModelLabel(string displayName)
    {
        var value = displayName.StartsWith(
            "GPT-",
            StringComparison.OrdinalIgnoreCase)
            ? displayName[4..]
            : displayName;
        return value.Replace('-', ' ');
    }

    private static string EffortLabel(string? effort)
    {
        return effort?.ToLowerInvariant() switch
        {
            "low" => "Light",
            "medium" => "Medium",
            "high" => "High",
            "xhigh" => "Extra High",
            "max" => "Max",
            "ultra" => "Ultra",
            _ => string.Empty,
        };
    }

    private static string CategoryLabel(ComposerSettingKind kind)
    {
        return kind switch
        {
            ComposerSettingKind.Model => "Model",
            ComposerSettingKind.Effort => "Effort",
            ComposerSettingKind.Speed => "Speed",
            _ => string.Empty,
        };
    }

    private static string NormalizeChoice(string value)
    {
        return new string(
            value
                .Where(char.IsLetterOrDigit)
                .Select(char.ToLowerInvariant)
                .ToArray());
    }

    private static string? GetString(JsonElement element, string property)
    {
        return
            element.TryGetProperty(property, out var value) &&
            value.ValueKind == JsonValueKind.String
                ? value.GetString()
                : null;
    }

    private static int? GetInt(JsonElement element, string property)
    {
        return
            element.TryGetProperty(property, out var value) &&
            value.TryGetInt32(out var number)
                ? number
                : null;
    }

    private static string ResolveCodexHome()
    {
        var configured = Environment.GetEnvironmentVariable("CODEX_HOME");
        return !string.IsNullOrWhiteSpace(configured)
            ? configured
            : Path.Combine(
                Environment.GetFolderPath(
                    Environment.SpecialFolder.UserProfile),
                ".codex");
    }

    private sealed record ConfigPreferences(
        string? ModelSlug,
        string? Effort,
        string? ServiceTier);
}
