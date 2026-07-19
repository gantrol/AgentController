using System.Windows;
using System.Windows.Threading;
using CodexController.ViewModels;
using Border = System.Windows.Controls.Border;
using UserControl = System.Windows.Controls.UserControl;

namespace CodexController.Views;

public partial class SidebarNavigationMenuView : UserControl
{
    public SidebarNavigationMenuView()
    {
        InitializeComponent();
    }

    private void OnMenuItemLoaded(object sender, RoutedEventArgs e)
    {
        if (
            sender is not Border
            {
                DataContext: SidebarNavigationMenuItemViewModel
                {
                    IsSelected: true,
                },
            } row)
        {
            return;
        }

        _ = Dispatcher.BeginInvoke(
            DispatcherPriority.Loaded,
            () => row.BringIntoView());
    }
}
