using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;

namespace CodexMicro.Desktop;

public partial class MicroSurfaceWindow
{
    private static readonly TimeSpan TaskKeyMotionDuration = TimeSpan.FromMilliseconds(240);
    private PageKeyVisual[] _presentedTaskKeys = [];
    private string? _taskMotionHarnessId;
    private readonly List<Action> _taskMotionCleanups = [];
    private DispatcherTimer? _taskMotionTimer;
    private bool _taskKeyMotionActive;

    private void InitializeTaskKeyMotion()
    {
        StateChanged += (_, _) =>
        {
            if (WindowState == WindowState.Minimized)
            {
                ResetTaskKeyMotion();
            }
        };
        foreach (var key in _agentKeys.Concat(_monitorKeys))
        {
            // Consume the press itself: a release after the animation must not
            // activate the task that just arrived under the pointer.
            key.PreviewMouseDown += TaskKey_PreviewInput;
            key.PreviewKeyDown += TaskKey_PreviewInput;
        }
    }

    private void TaskKey_PreviewInput(object sender, InputEventArgs e)
    {
        if (_taskKeyMotionActive || _pageSwitching)
        {
            e.Handled = true;
        }
    }

    private void UpdateTaskKeyMotion(bool monitor)
    {
        if (monitor != _monitorPage)
        {
            return;
        }
        if (_windowClosed || !IsLoaded || !IsVisible ||
            WindowState == WindowState.Minimized || _pageSwitching)
        {
            ResetTaskKeyMotion();
            return;
        }

        var harnessId = ActiveHarness().Id;
        var next = PageKeyVisuals(monitor).ToArray();
        var previous = _presentedTaskKeys;
        _presentedTaskKeys = next;
        var sameHarness = _taskMotionHarnessId == harnessId;
        _taskMotionHarnessId = harnessId;
        // Status, title and selection updates leave in-flight motion alone.
        if (sameHarness && previous.Length == next.Length && previous.Zip(next).All(pair =>
            pair.First.Identity == pair.Second.Identity && pair.First.Key == pair.Second.Key))
        {
            if (!SystemParameters.ClientAreaAnimation)
            {
                FinishTaskKeyMotion();
            }
            return;
        }

        _lastAgentTapKey = null;
        _lastAgentTapTimestamp = 0;
        foreach (var key in monitor ? _monitorKeys : _agentKeys)
        {
            if (key.IsKeyboardFocused && key.IsPressed)
            {
                Keyboard.ClearFocus();
            }
            if (key.IsMouseCaptured)
            {
                key.ReleaseMouseCapture();
            }
        }
        if (!sameHarness || !SystemParameters.ClientAreaAnimation || previous.Length == 0)
        {
            FinishTaskKeyMotion();
            return;
        }

        var grid = monitor ? MonitorGrid : ControlGrid;
        var frames = new Dictionary<string, Rect>(StringComparer.Ordinal);
        foreach (var visual in previous)
        {
            if (visual.Key.IsVisible && visual.Key.ActualWidth > 0)
            {
                // Read the animated position before removing the old clocks.
                // Rapid reorders therefore continue from the currently drawn key.
                frames.TryAdd(visual.Identity, visual.Key.TransformToAncestor(grid)
                    .TransformBounds(new Rect(visual.Key.RenderSize)));
            }
        }
        FinishTaskKeyMotion();

        if (frames.Count == 0 || next.Length == 0)
        {
            return;
        }

        _taskKeyMotionActive = true;
        try
        {
            foreach (var visual in next)
            {
                if (!visual.Key.IsVisible || visual.Key.ActualWidth <= 0)
                {
                    continue;
                }

                var target = visual.Key.TransformToAncestor(grid)
                    .TransformBounds(new Rect(visual.Key.RenderSize));
                var matched = frames.TryGetValue(visual.Identity, out var source);
                var offset = matched ? source.TopLeft - target.TopLeft : new Vector(0, 8);
                if (matched && offset.LengthSquared < 0.25)
                {
                    continue;
                }

                // A short opposing arc makes a two-key exchange readable;
                // all members of a larger permutation move at the same time.
                var bend = matched && offset.Length > 1
                    ? new Vector(offset.Y, -offset.X) / offset.Length * 48
                    : new Vector();
                foreach (var part in visual.Parts)
                {
                    _taskMotionCleanups.Add(AnimateTaskKeyPart(part, offset, bend));
                }

                if (!matched)
                {
                    visual.Key.BeginAnimation(OpacityProperty, new DoubleAnimation
                    {
                        From = 0,
                        Duration = TaskKeyMotionDuration,
                        FillBehavior = FillBehavior.Stop,
                    });
                    _taskMotionCleanups.Add(() => visual.Key.BeginAnimation(OpacityProperty, null));
                }
            }

            if (_taskMotionCleanups.Count == 0)
            {
                FinishTaskKeyMotion();
                return;
            }

            // Cleanup also runs if rendering pauses; no Completed callback is
            // needed to restore interaction or release animation clocks.
            if (_taskMotionTimer is null)
            {
                _taskMotionTimer = new DispatcherTimer
                {
                    Interval = TaskKeyMotionDuration + TimeSpan.FromMilliseconds(40),
                };
                _taskMotionTimer.Tick += (_, _) => FinishTaskKeyMotion();
            }
            _taskMotionTimer.Start();
        }
        catch
        {
            FinishTaskKeyMotion();
            throw;
        }
    }

    private static Action AnimateTaskKeyPart(FrameworkElement element, Vector offset, Vector bend)
    {
        var original = element.RenderTransform;
        var translation = new TranslateTransform();
        var group = new TransformGroup { Children = { original, translation } };
        var path = new PathGeometry
        {
            Figures =
            {
                new PathFigure
                {
                    StartPoint = new Point(offset.X, offset.Y),
                    Segments =
                    {
                        new QuadraticBezierSegment(
                            new Point(offset.X / 2 + bend.X, offset.Y / 2 + bend.Y),
                            new Point(), true),
                    },
                },
            },
        };
        path.Freeze();
        element.RenderTransform = group;
        DoubleAnimationUsingPath Motion(PathAnimationSource source) => new()
        {
            PathGeometry = path,
            Source = source,
            Duration = TaskKeyMotionDuration,
            DecelerationRatio = 0.8,
            FillBehavior = FillBehavior.Stop,
        };
        translation.BeginAnimation(TranslateTransform.XProperty, Motion(PathAnimationSource.X));
        translation.BeginAnimation(TranslateTransform.YProperty, Motion(PathAnimationSource.Y));
        return () =>
        {
            translation.BeginAnimation(TranslateTransform.XProperty, null);
            translation.BeginAnimation(TranslateTransform.YProperty, null);
            if (ReferenceEquals(element.RenderTransform, group))
            {
                element.RenderTransform = original;
            }
        };
    }

    private void FinishTaskKeyMotion()
    {
        _taskMotionTimer?.Stop();
        foreach (var cleanup in _taskMotionCleanups)
        {
            cleanup();
        }
        _taskMotionCleanups.Clear();
        _taskKeyMotionActive = false;
    }

    private void ResetTaskKeyMotion()
    {
        FinishTaskKeyMotion();
        _presentedTaskKeys = [];
        _taskMotionHarnessId = null;
    }
}
