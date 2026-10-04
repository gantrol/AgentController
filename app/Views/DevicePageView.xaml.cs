using System.Windows.Controls;
using System.Windows.Input;
using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;
using CodexController.Models;
using CodexController.Controllers;

namespace CodexController.Views;

public partial class DevicePageView :
    System.Windows.Controls.UserControl
{
    public DevicePageView()
    {
        InitializeComponent();
    }

    public event SelectionChangedEventHandler? SidebarSelectionChanged;

    public event MouseButtonEventHandler? SidebarMouseDoubleClick;
    public event EventHandler<SidebarSectionTab>? SidebarSectionRequested;

    private void SidebarSection_Click(object sender, System.Windows.RoutedEventArgs e)
    {
        if (sender is System.Windows.Controls.RadioButton { DataContext: SidebarSectionTab section } && section.CanNavigate)
            SidebarSectionRequested?.Invoke(this, section);
    }

    private void SidebarSection_EnsureVisible(object sender, System.Windows.RoutedEventArgs e)
    {
        if (sender is not System.Windows.Controls.RadioButton { IsLoaded: true } tab ||
            tab.IsChecked != true && !tab.IsKeyboardFocusWithin)
            return;

        tab.Dispatcher.BeginInvoke(DispatcherPriority.Loaded, new Action(() =>
        {
            if (tab.IsLoaded && (tab.IsChecked == true || tab.IsKeyboardFocusWithin))
                EnsureSidebarTabVisible(tab);
        }));
    }

    private void EnsureSidebarTabVisible(FrameworkElement tab)
    {
        var bounds = tab.TransformToAncestor(SidebarTabsScroll).TransformBounds(new Rect(tab.RenderSize));
        if (bounds.Left < 0)
            SidebarTabsScroll.ScrollToHorizontalOffset(SidebarTabsScroll.HorizontalOffset + bounds.Left);
        else if (bounds.Right > SidebarTabsScroll.ViewportWidth)
            SidebarTabsScroll.ScrollToHorizontalOffset(
                SidebarTabsScroll.HorizontalOffset + bounds.Right - SidebarTabsScroll.ViewportWidth);
    }

    private void SidebarTabsScroll_Changed(object sender, ScrollChangedEventArgs e)
    {
        var visibility = SidebarTabsScroll.ExtentWidth > SidebarTabStrip.ActualWidth + 1
            ? Visibility.Visible : Visibility.Collapsed;
        SidebarTabsPrevious.Visibility = visibility;
        SidebarTabsNext.Visibility = visibility;
        SidebarTabsPrevious.IsEnabled = SidebarTabsScroll.HorizontalOffset > 0.5;
        SidebarTabsNext.IsEnabled = SidebarTabsScroll.HorizontalOffset < SidebarTabsScroll.ScrollableWidth - 0.5;
    }

    private void SidebarTabsScroll_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        Dispatcher.BeginInvoke(DispatcherPriority.Loaded, new Action(() =>
        {
            if (FindSelectedSidebarTab(SidebarTabs) is { IsLoaded: true } tab)
                EnsureSidebarTabVisible(tab);
        }));
    }

    private static System.Windows.Controls.RadioButton? FindSelectedSidebarTab(DependencyObject parent)
    {
        if (parent is System.Windows.Controls.RadioButton { IsChecked: true } tab) return tab;
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
            if (FindSelectedSidebarTab(VisualTreeHelper.GetChild(parent, i)) is { } selected) return selected;
        return null;
    }

    private void SidebarTabsScroll_MouseWheel(object sender, MouseWheelEventArgs e)
    {
        if (SidebarTabsScroll.ScrollableWidth <= 0) return;
        SidebarTabsScroll.ScrollToHorizontalOffset(SidebarTabsScroll.HorizontalOffset - e.Delta);
        e.Handled = true;
    }

    private void SidebarTabsPrevious_Click(object sender, RoutedEventArgs e) =>
        SidebarTabsScroll.ScrollToHorizontalOffset(
            SidebarTabsScroll.HorizontalOffset - Math.Max(80, SidebarTabsScroll.ViewportWidth * 0.8));

    private void SidebarTabsNext_Click(object sender, RoutedEventArgs e) =>
        SidebarTabsScroll.ScrollToHorizontalOffset(
            SidebarTabsScroll.HorizontalOffset + Math.Max(80, SidebarTabsScroll.ViewportWidth * 0.8));

    public event System.Windows.Input.KeyEventHandler?
        SidebarPreviewKeyDown;

    public SidebarEntry? SelectedEntry =>
        SidebarList.SelectedItem as SidebarEntry;

    public int SelectedIndex => SidebarList.SelectedIndex;

    public void SelectSidebarIndex(int index)
    {
        SidebarList.SelectedIndex = index;
        if (SidebarList.SelectedItem is not null)
        {
            SidebarList.ScrollIntoView(SidebarList.SelectedItem);
        }
    }

    public void ClearSelection()
    {
        SidebarList.SelectedIndex = -1;
    }

    public void RenderControllerState(
        ControllerState state,
        double deadZone)
    {
        ControllerTutorial.RenderControllerState(state, deadZone);
    }

    public void SetVoiceHalo(bool active)
    {
        ControllerTutorial.SetVoiceHalo(active);
    }

    public void HighlightTutorialInput(TutorialInput? input) => ControllerTutorial.Highlight(input);

    private void SidebarList_SelectionChanged(
        object sender,
        SelectionChangedEventArgs e)
    {
        SidebarSelectionChanged?.Invoke(sender, e);
    }

    private void SidebarList_MouseDoubleClick(
        object sender,
        MouseButtonEventArgs e)
    {
        SidebarMouseDoubleClick?.Invoke(sender, e);
    }

    private void SidebarList_PreviewKeyDown(
        object sender,
        System.Windows.Input.KeyEventArgs e)
    {
        SidebarPreviewKeyDown?.Invoke(sender, e);
    }
}
