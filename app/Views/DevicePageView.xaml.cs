using System.Windows.Controls;
using System.Windows.Input;
using CodexController.Models;

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
