using System.Windows;
using System.Windows.Automation;
using System.Windows.Automation.Peers;
using System.Windows.Media.Animation;
using System.Windows.Threading;

namespace CodexController;

public partial class OverlayWindow : Window
{
    private static readonly TimeSpan VisibleDuration = TimeSpan.FromMilliseconds(1050);
    private const double BottomMargin = 52;

    private readonly DispatcherTimer _hideTimer;
    private int _animationVersion;
    private int _isClosed;

    public OverlayWindow()
    {
        InitializeComponent();
        Opacity = 0;
        _hideTimer = new DispatcherTimer
        {
            Interval = VisibleDuration,
        };
        _hideTimer.Tick += (_, _) =>
        {
            _hideTimer.Stop();
            HideOverlay();
        };
    }

    public void ShowMessage(
        string title,
        string value,
        TimeSpan? visibleDuration = null)
    {
        if (Volatile.Read(ref _isClosed) != 0)
        {
            return;
        }

        if (!Dispatcher.CheckAccess())
        {
            if (
                Dispatcher.HasShutdownStarted ||
                Dispatcher.HasShutdownFinished)
            {
                return;
            }

            _ = Dispatcher.BeginInvoke(
                () => ShowMessage(title, value, visibleDuration));
            return;
        }

        if (Volatile.Read(ref _isClosed) != 0)
        {
            return;
        }

        _animationVersion++;
        var wasVisible = IsVisible;

        BeginAnimation(OpacityProperty, null);
        OverlayTitle.Text = title;
        OverlayValue.Text = value;
        AutomationProperties.SetName(this, $"{title}：{value}");

        _hideTimer.Stop();
        if (!wasVisible)
        {
            Show();
        }

        UpdateLayout();
        PositionAtBottomCenter();

        if (!SystemParameters.ClientAreaAnimation || wasVisible)
        {
            Opacity = 1;
        }
        else
        {
            Opacity = 0.3;
            BeginAnimation(
                OpacityProperty,
                CreateOpacityAnimation(1, "Duration.Fast"));
        }

        RaiseLiveRegionChanged();
        _hideTimer.Interval =
            visibleDuration is { } duration && duration > TimeSpan.Zero
                ? duration
                : VisibleDuration;
        _hideTimer.Start();
    }

    protected override void OnClosed(EventArgs e)
    {
        Interlocked.Exchange(ref _isClosed, 1);
        _animationVersion++;
        _hideTimer.Stop();
        BeginAnimation(OpacityProperty, null);
        base.OnClosed(e);
    }

    private void PositionAtBottomCenter()
    {
        var workArea = SystemParameters.WorkArea;
        var width = ActualWidth > 0 ? ActualWidth : MinWidth;
        var height = ActualHeight;

        Left = workArea.Left + (workArea.Width - width) / 2;
        Top = Math.Max(
            workArea.Top,
            workArea.Bottom - height - BottomMargin);
    }

    private void HideOverlay()
    {
        if (Volatile.Read(ref _isClosed) != 0)
        {
            return;
        }

        var animationVersion = ++_animationVersion;

        if (!SystemParameters.ClientAreaAnimation)
        {
            CompleteHide(animationVersion);
            return;
        }

        var animation = CreateOpacityAnimation(0, "Duration.Base");
        animation.Completed += (_, _) => CompleteHide(animationVersion);
        BeginAnimation(OpacityProperty, animation);
    }

    private void CompleteHide(int animationVersion)
    {
        if (
            Volatile.Read(ref _isClosed) != 0 ||
            animationVersion != _animationVersion)
        {
            return;
        }

        BeginAnimation(OpacityProperty, null);
        Opacity = 0;
        Hide();
    }

    private DoubleAnimation CreateOpacityAnimation(double to, string durationKey)
    {
        var duration = TryFindResource(durationKey) is Duration tokenDuration
            ? tokenDuration
            : new Duration(TimeSpan.Zero);

        return new DoubleAnimation(to, duration)
        {
            EasingFunction = TryFindResource("Ease.Out") as IEasingFunction,
        };
    }

    private void RaiseLiveRegionChanged()
    {
        var peer = UIElementAutomationPeer.FromElement(this)
                   ?? UIElementAutomationPeer.CreatePeerForElement(this);
        peer?.RaiseAutomationEvent(AutomationEvents.LiveRegionChanged);
    }
}
