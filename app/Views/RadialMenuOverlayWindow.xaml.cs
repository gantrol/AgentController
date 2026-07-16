using System.Windows;
using CodexController.Models;
using CodexController.ViewModels;

namespace CodexController.Views;

public partial class RadialMenuOverlayWindow : Window
{
    private const double BottomMargin = 28;
    private int _isClosed;

    public RadialMenuOverlayWindow(
        RadialMenuViewModel? viewModel = null)
    {
        InitializeComponent();
        ViewModel = viewModel ?? new RadialMenuViewModel();
        DataContext = ViewModel;
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

        ViewModel.Hide();
        Hide();
    }

    protected override void OnClosed(EventArgs e)
    {
        Interlocked.Exchange(ref _isClosed, 1);
        base.OnClosed(e);
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
