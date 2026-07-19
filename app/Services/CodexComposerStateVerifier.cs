using System.Windows.Automation;
using static CodexController.Services.CodexAutomationLocator;

namespace CodexController.Services;

internal static class CodexComposerStateVerifier
{
    internal static string? ReadComposerText(AutomationElement editor)
    {
        if (
            editor.TryGetCurrentPattern(
                TextPattern.Pattern,
                out var textObject) &&
            textObject is TextPattern textPattern)
        {
            return textPattern.DocumentRange.GetText(-1);
        }

        if (
            editor.TryGetCurrentPattern(
                ValuePattern.Pattern,
                out var valueObject) &&
            valueObject is ValuePattern valuePattern)
        {
            return valuePattern.Current.Value;
        }

        return null;
    }

    internal static bool WaitForComposerTextToClear(
        AutomationElement editor,
        int timeoutMs)
    {
        var deadline = Environment.TickCount64 + timeoutMs;
        do
        {
            if (IsComposerEditorEmpty(editor))
            {
                return true;
            }

            var text = ReadComposerText(editor);
            if (IsComposerTextEffectivelyEmpty(text))
            {
                return true;
            }

            Thread.Sleep(24);
        }
        while (Environment.TickCount64 < deadline);

        return
            IsComposerEditorEmpty(editor) ||
            IsComposerTextEffectivelyEmpty(ReadComposerText(editor));
    }

    internal static bool IsComposerTextEffectivelyEmpty(string? text)
    {
        if (text is null)
        {
            return false;
        }

        return string.IsNullOrWhiteSpace(
            text
                .Replace("\u200B", string.Empty, StringComparison.Ordinal)
                .Replace("\uFEFF", string.Empty, StringComparison.Ordinal)
                .Replace("\uFFFC", string.Empty, StringComparison.Ordinal));
    }

    internal static string? TryReadComposerDraft(
        AutomationElement editor)
    {
        if (IsComposerEditorEmpty(editor))
        {
            return string.Empty;
        }

        return ReadComposerText(editor);
    }

    internal static bool IsComposerEditorEmpty(AutomationElement editor)
    {
        try
        {
            var trailingBreaks = editor.FindAll(
                TreeScope.Children,
                new PropertyCondition(
                    AutomationElement.ClassNameProperty,
                    "ProseMirror-trailingBreak"));
            return trailingBreaks.Count > 0;
        }
        catch (ElementNotAvailableException)
        {
            return false;
        }
    }

    internal static bool WaitForComposerDraft(
        AutomationElement window,
        string expected,
        int timeoutMs,
        CancellationToken cancellationToken)
    {
        var deadline = Environment.TickCount64 + timeoutMs;
        do
        {
            cancellationToken.ThrowIfCancellationRequested();
            var editor = FindComposerEditor(window);
            if (
                editor is not null &&
                ComposerDraftEquals(
                    TryReadComposerDraft(editor),
                    expected))
            {
                return true;
            }

            Thread.Sleep(24);
        }
        while (Environment.TickCount64 < deadline);

        return false;
    }

    internal static bool HasInjectedPlanQuery(
        string? actual,
        string originalDraft)
    {
        if (actual is null)
        {
            return false;
        }

        var normalizedActual = NormalizeLineEndings(actual);
        var normalizedDraft = NormalizeLineEndings(originalDraft);
        return
            string.Equals(
                normalizedActual,
                PlanModeAutomationPolicy.SlashCommandQuery +
                    normalizedDraft,
                StringComparison.Ordinal) ||
            string.Equals(
                normalizedActual,
                "/" + normalizedDraft,
                StringComparison.Ordinal);
    }

    internal static bool ComposerDraftEquals(
        string? actual,
        string expected) =>
        actual is not null &&
        string.Equals(
            NormalizeLineEndings(actual),
            NormalizeLineEndings(expected),
            StringComparison.Ordinal);

    internal static string NormalizeLineEndings(string value) =>
        value.Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace('\r', '\n');
}
