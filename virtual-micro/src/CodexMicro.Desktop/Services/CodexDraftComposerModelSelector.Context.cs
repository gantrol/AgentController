using System.Runtime.InteropServices;
using System.Windows.Automation;

namespace CodexMicro.Desktop.Services;

internal sealed partial class CodexDraftComposerModelSelector
{
    // IPC following can include background or prewarmed threads on the home route.
    // The actual foreground home surface, rather than that list, owns this draft.
    private const string HomeSurfaceClass = "[container-name:home-main-content]";

    internal sealed class DraftContext(IntPtr window, AutomationElement home, AutomationElement composer, string triggerId)
    {
        private readonly object _sync = new();
        internal CodexModelToggleService.ForegroundDraftPresentationContext Presentation { get; } =
            new(window, "composer:" + triggerId, 0, null);

        internal bool IsCurrent()
        {
            try
            {
                lock (_sync)
                {
                    if (!CodexWindowActivator.IsForegroundWindow(window)) return false;
                    if (ElementsAreCurrent()) return true;
                    var root = AutomationElement.FromHandle(window);
                    // A modal temporarily removes the home subtree from Chromium's accessibility
                    // tree. ToggleCore rejects a decision that was already open at admission.
                    if (HasUltraWarning(root)) return true;
                    // Rebind only through the same DOM trigger ID, never another page's composer.
                    var trigger = root.FindFirst(TreeScope.Descendants,
                        new PropertyCondition(AutomationElement.AutomationIdProperty, triggerId));
                    for (var ancestor = trigger; ancestor is not null; ancestor = TreeWalker.ControlViewWalker.GetParent(ancestor))
                    {
                        if (!ancestor.Current.ClassName.Split(' ').Contains(HomeSurfaceClass)) continue;
                        var edit = ancestor.FindFirst(TreeScope.Descendants,
                            new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Edit));
                        if (edit is null || !edit.Current.ClassName.Split(' ').Contains("ProseMirror")) return false;
                        home = ancestor;
                        composer = edit;
                        return ElementsAreCurrent();
                    }
                    return false;
                }
            }
            catch (Exception error) when (error is ElementNotAvailableException or InvalidOperationException or COMException or ArgumentException)
            {
                return false;
            }
        }

        private bool ElementsAreCurrent()
        {
            try
            {
                return !home.Current.IsOffscreen && !composer.Current.IsOffscreen && composer.Current.IsEnabled &&
                    home.Current.ClassName.Split(' ').Contains(HomeSurfaceClass) && composer.Current.ClassName.Split(' ').Contains("ProseMirror");
            }
            catch (Exception error) when (error is ElementNotAvailableException or InvalidOperationException or COMException)
            {
                return false;
            }
        }
    }

    internal Task<DraftContext?> CaptureDraftContextAsync(DraftContext? previous, CancellationToken token) =>
        Task.Run(() =>
        {
            token.ThrowIfCancellationRequested();
            if (previous?.IsCurrent() == true) return previous;
            var window = CodexWindowActivator.CaptureForegroundWindow();
            if (window == IntPtr.Zero) return null;
            try
            {
                var root = AutomationElement.FromHandle(window);
                var homes = root.FindAll(TreeScope.Descendants, new AndCondition(
                    new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Group),
                    new PropertyCondition(AutomationElement.IsOffscreenProperty, false)))
                    .Cast<AutomationElement>().Where(element => element.Current.ClassName.Split(' ').Contains(HomeSurfaceClass)).Take(2).ToArray();
                if (homes.Length != 1) return null;
                var composers = homes[0].FindAll(TreeScope.Descendants, new AndCondition(
                    new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Edit),
                    new PropertyCondition(AutomationElement.IsOffscreenProperty, false)))
                    .Cast<AutomationElement>().Where(element => element.Current.ClassName.Split(' ').Contains("ProseMirror")).Take(2).ToArray();
                if (composers.Length != 1) return null;
                var triggerId = FindTrigger(homes[0])?.Current.AutomationId;
                if (string.IsNullOrWhiteSpace(triggerId)) return null;
                var context = new DraftContext(window, homes[0], composers[0], triggerId);
                return context.IsCurrent() ? context : null;
            }
            catch (Exception error) when (error is ElementNotAvailableException or InvalidOperationException or COMException)
            {
                return null;
            }
        }, token);
}
