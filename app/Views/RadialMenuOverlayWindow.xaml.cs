using System.Windows;
using System.Windows.Media.Animation;
using CodexController.Models;
using CodexController.ViewModels;

namespace CodexController.Views;

public partial class RadialMenuOverlayWindow : Window
{
    private static readonly TimeSpan InputAcknowledgementDuration =
        TimeSpan.FromMilliseconds(90);
    private static readonly TimeSpan FadeDuration =
        TimeSpan.FromMilliseconds(180);
    private const double BottomMargin = 28;
    private int _transitionVersion;
    private int _isClosed;

    public RadialMenuOverlayWindow(
        RadialMenuViewModel? viewModel = null)
    {
        InitializeComponent();
        ViewModel = viewModel ?? new RadialMenuViewModel();
        DataContext = ViewModel;
        Opacity = 0;
    }

    public RadialMenuViewModel ViewModel { get; }

    public void ShowState(RadialMenuState state)
    {
        ArgumentNullException.ThrowIfNull(state);
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

            _ = Dispatcher.BeginInvoke(() => ShowState(state));
            return;
        }

        CancelTransition(showAtFullOpacity: true);
        ViewModel.Update(state);
        if (!ViewModel.IsVisible)
        {
            HideMenu();
            return;
        }

        if (!IsVisible)
        {
            Show();
        }

        UpdateLayout();
        PositionAtBottomCenter();
    }

    public string? AcknowledgeInputAndFade(string actionId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(actionId);
        if (Volatile.Read(ref _isClosed) != 0)
        {
            return null;
        }

        if (!Dispatcher.CheckAccess())
        {
            if (
                !Dispatcher.HasShutdownStarted &&
                !Dispatcher.HasShutdownFinished)
            {
                _ = Dispatcher.BeginInvoke(
                    () => AcknowledgeInputAndFade(actionId));
            }

            return null;
        }

        if (!ViewModel.TryAcceptInput(actionId, out var actionTitle))
        {
            return null;
        }

        if (!ViewModel.IsVisible)
        {
            CompleteWaitingTransition(++_transitionVersion);
            return actionTitle;
        }

        if (!IsVisible)
        {
            Show();
        }

        UpdateLayout();
        PositionAtBottomCenter();
        CancelTransition(showAtFullOpacity: true);
        var transitionVersion = _transitionVersion;
        if (!SystemParameters.ClientAreaAnimation)
        {
            CompleteWaitingTransition(transitionVersion);
            return actionTitle;
        }

        var animation = new DoubleAnimation(
            1,
            0,
            new Duration(FadeDuration))
        {
            BeginTime = InputAcknowledgementDuration,
            EasingFunction =
                TryFindResource("Ease.Out") as IEasingFunction,
        };
        animation.Completed += (_, _) =>
            CompleteWaitingTransition(transitionVersion);
        BeginAnimation(OpacityProperty, animation);
        return actionTitle;
    }

    public void HideMenu()
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

            _ = Dispatcher.BeginInvoke(HideMenu);
            return;
        }

        CancelTransition(showAtFullOpacity: false);
        ViewModel.Hide();
        Hide();
    }

    protected override void OnClosed(EventArgs e)
    {
        Interlocked.Exchange(ref _isClosed, 1);
        _transitionVersion++;
        BeginAnimation(OpacityProperty, null);
        base.OnClosed(e);
    }

    private void CancelTransition(bool showAtFullOpacity)
    {
        _transitionVersion++;
        BeginAnimation(OpacityProperty, null);
        Opacity = showAtFullOpacity ? 1 : 0;
    }

    private void CompleteWaitingTransition(int transitionVersion)
    {
        if (
            Volatile.Read(ref _isClosed) != 0 ||
            transitionVersion != _transitionVersion)
        {
            return;
        }

        BeginAnimation(OpacityProperty, null);
        Opacity = 0;
        ViewModel.EnterWaitingForResponse();
        Hide();
    }

    private void PositionAtBottomCenter()
    {
        var workArea = SystemParameters.WorkArea;
        var width = ActualWidth > 0 ? ActualWidth : Width;
        var height = ActualHeight > 0 ? ActualHeight : Height;
        Left = Math.Max(
            workArea.Left,
            workArea.Left + (workArea.Width - width) / 2);
        Top = Math.Max(
            workArea.Top,
            workArea.Bottom - height - BottomMargin);
    }
}
