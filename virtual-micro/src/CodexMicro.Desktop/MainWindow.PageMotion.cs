using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace CodexMicro.Desktop;

public partial class MicroSurfaceWindow
{
    private sealed record PageKeyVisual(
        string Identity, Button Key, FrameworkElement[] Parts);

    private CancellationTokenSource? _pageMotionCancellation;
    private bool _pageMotionActive;

    private IEnumerable<PageKeyVisual> PageKeyVisuals(bool monitor)
    {
        var harnessId = ActiveHarness().Id;
        if (monitor)
        {
            for (var index = 0; index < _monitorKeys.Length; index++)
            {
                if (_monitorKeys[index].Tag is MonitorTask task)
                {
                    yield return new($"{task.HarnessId}:{task.Id}", _monitorKeys[index],
                        [_monitorKeys[index], _monitorWideGlows[index], _monitorNearGlows[index]]);
                }
            }
            yield break;
        }

        if (IsHarnessMenuNavigationActive(ActiveHarness()))
        {
            yield break;
        }

        for (var index = 0; index < _agentKeys.Length; index++)
        {
            var id = harnessId == "codex"
                ? _latestAgentRoster?.GetSlot(index)?.ThreadId
                : _harnessStateSnapshot?.HarnessId == harnessId
                    ? _harnessStateSnapshot.Sessions.ElementAtOrDefault(index)?.Id
                    : null;
            if (id is not null)
            {
                yield return new($"{harnessId}:{id}", _agentKeys[index],
                    [_agentKeys[index], _agentWideGlows[index], _agentNearGlows[index]]);
            }
        }
    }

    private Dictionary<string, Rect> CapturePageKeyFrames(bool monitor)
    {
        var frames = new Dictionary<string, Rect>(StringComparer.Ordinal);
        foreach (var visual in PageKeyVisuals(monitor))
        {
            if (visual.Key.IsVisible && visual.Key.ActualWidth > 0)
            {
                frames.TryAdd(visual.Identity, visual.Key.TransformToAncestor(DesignSurface)
                    .TransformBounds(new Rect(visual.Key.RenderSize)));
            }
        }
        return frames;
    }

    private async void PageButton_Click(object sender, RoutedEventArgs e)
    {
        if (_pageSwitching || _monitorQuickBusy || _quickModelSwitching ||
            _harnessModelSwitching || _monitorOpening ||
            sender is not RadioButton { Tag: string page })
        {
            ControlPageButton.IsChecked = !_monitorPage;
            MonitorPageButton.IsChecked = _monitorPage;
            return;
        }

        var next = page == "1";
        if (next == _monitorPage)
        {
            return;
        }

        _pageSwitching = true;
        ControlPageButton.IsEnabled = false;
        MonitorPageButton.IsEnabled = false;
        ControlGrid.IsHitTestVisible = false;
        MonitorSurface.IsHitTestVisible = false;
        _sharedPageControls.IsHitTestVisible = false;
        var cleanups = new List<Action>();
        using var cancellation = new CancellationTokenSource();
        _pageMotionCancellation = cancellation;
        try
        {
            CloseMonitorEffortMenu();
            CancelMonitorQuickRead();
            if (_voicePressed)
            {
                await ReleaseVoiceAsync();
            }
            if (_windowClosed || !IsVisible)
            {
                return;
            }
            if (_joystickDragging)
            {
                EndJoystickDrag();
            }
            CancelDialGesture();
            CancelReasoningInput();
            _encoderSteps.Clear();
            _lastAgentTapKey = null;

            var frames = CapturePageKeyFrames(_monitorPage);
            FrameworkElement outgoing = _monitorPage ? MonitorSurface : ControlGrid;
            FrameworkElement incoming = next ? MonitorSurface : ControlGrid;
            _monitorPage = next;
            ControlGrid.Visibility = Visibility.Visible;
            MonitorSurface.Visibility = Visibility.Visible;
            UpdateMonitorRefresh();
            DesignSurface.UpdateLayout();
            cancellation.Token.ThrowIfCancellationRequested();
            _pageMotionActive = true;

            if (SystemParameters.ClientAreaAnimation && IsVisible)
            {
                // Match by task identity, never by slot number. Shared tasks
                // travel to their new homes; the glass shell stays stationary.
                var row = 0;
                foreach (var visual in PageKeyVisuals(next))
                {
                    var target = visual.Key.TransformToAncestor(DesignSurface)
                        .TransformBounds(new Rect(visual.Key.RenderSize));
                    var matched = frames.TryGetValue(visual.Identity, out var source);
                    var inverse = visual.Key.TransformToAncestor(DesignSurface).Inverse;
                    if (inverse is null || target.Width <= 0 || target.Height <= 0)
                    {
                        continue;
                    }
                    var targetCenter = new Point(target.X + target.Width / 2,
                        target.Y + target.Height / 2);
                    var sourceCenter = matched
                        ? new Point(source.X + source.Width / 2, source.Y + source.Height / 2)
                        : new Point(targetCenter.X, targetCenter.Y + (next ? 12 : -12));
                    var offset = inverse.Transform(sourceCenter) - inverse.Transform(targetCenter);
                    var scale = matched ? source.Width / target.Width : 0.98;
                    var delay = TimeSpan.FromMilliseconds((row++ / 4) * 12);
                    foreach (var part in visual.Parts)
                    {
                        cleanups.Add(AnimatePageKey(part, offset, scale, delay));
                    }
                }

                outgoing.BeginAnimation(OpacityProperty, new DoubleAnimation(1, 0,
                    TimeSpan.FromMilliseconds(120)));
                incoming.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1,
                    TimeSpan.FromMilliseconds(200))
                {
                    EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut },
                });
                // A bounded delay also completes when WPF stops rendering a
                // hidden window. It does not depend on a storyboard callback.
                await Task.Delay(280, cancellation.Token);
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception exception)
        {
            if (!_windowClosed)
            {
                SetStatus(exception.Message);
            }
        }
        finally
        {
            foreach (var cleanup in cleanups)
            {
                cleanup();
            }
            ControlGrid.BeginAnimation(OpacityProperty, null);
            MonitorSurface.BeginAnimation(OpacityProperty, null);
            ControlGrid.Opacity = MonitorSurface.Opacity = 1;
            ControlGrid.Visibility = _monitorPage ? Visibility.Collapsed : Visibility.Visible;
            MonitorSurface.Visibility = _monitorPage ? Visibility.Visible : Visibility.Collapsed;
            ControlGrid.IsHitTestVisible = MonitorSurface.IsHitTestVisible = true;
            _sharedPageControls.IsHitTestVisible = true;
            ControlPageButton.IsChecked = !_monitorPage;
            MonitorPageButton.IsChecked = _monitorPage;
            ControlPageButton.IsEnabled = MonitorPageButton.IsEnabled = true;
            _pageMotionActive = false;
            _pageSwitching = false;
            if (ReferenceEquals(_pageMotionCancellation, cancellation))
            {
                _pageMotionCancellation = null;
            }
            if (!_windowClosed)
            {
                RefreshAgentSlotPresentation();
            }
        }
    }

    private static Action AnimatePageKey(
        FrameworkElement element, Vector offset, double startScale, TimeSpan delay)
    {
        var original = element.RenderTransform;
        var origin = element.RenderTransformOrigin;
        var scale = new ScaleTransform(startScale, startScale);
        var translation = new TranslateTransform(offset.X, offset.Y);
        var group = new TransformGroup { Children = { scale, translation } };
        element.RenderTransformOrigin = new Point(0.5, 0.5);
        element.RenderTransform = group;
        DoubleAnimation Motion(double from, double to) => new(from, to,
            TimeSpan.FromMilliseconds(220))
        {
            BeginTime = delay,
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut },
        };
        scale.BeginAnimation(ScaleTransform.ScaleXProperty, Motion(startScale, 1));
        scale.BeginAnimation(ScaleTransform.ScaleYProperty, Motion(startScale, 1));
        translation.BeginAnimation(TranslateTransform.XProperty, Motion(offset.X, 0));
        translation.BeginAnimation(TranslateTransform.YProperty, Motion(offset.Y, 0));
        return () =>
        {
            scale.BeginAnimation(ScaleTransform.ScaleXProperty, null);
            scale.BeginAnimation(ScaleTransform.ScaleYProperty, null);
            translation.BeginAnimation(TranslateTransform.XProperty, null);
            translation.BeginAnimation(TranslateTransform.YProperty, null);
            if (ReferenceEquals(element.RenderTransform, group))
            {
                element.RenderTransform = original;
                element.RenderTransformOrigin = origin;
            }
        };
    }
}
