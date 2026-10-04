using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using CodexController.Controllers;
using CodexController.Models;
using CodexController.ViewModels;

namespace CodexController.Views;

public partial class ControllerTutorialView : System.Windows.Controls.UserControl
{
    public ControllerTutorialView()
    {
        InitializeComponent();
        foreach (var input in new[]
        {
            LogicalInput.LeftTrigger, LogicalInput.RightTrigger, LogicalInput.LeftShoulder, LogicalInput.RightShoulder,
            LogicalInput.FaceNorth, LogicalInput.FaceEast, LogicalInput.FaceSouth, LogicalInput.FaceWest,
            LogicalInput.View, LogicalInput.Menu, LogicalInput.DPadUp, LogicalInput.DPadRight,
            LogicalInput.DPadDown, LogicalInput.DPadLeft,
        })
        {
            var bounds = ControllerArtwork.InputBounds(input);
            AddHotspot(input, bounds.X, bounds.Y, bounds.Width, bounds.Height);
        }
        AddStick(LogicalInput.LeftStick, LogicalInput.LeftStickPress);
        AddStick(LogicalInput.RightStick, LogicalInput.RightStickPress);
        DataContextChanged += (_, e) =>
        {
            if (e.OldValue is INotifyPropertyChanged oldModel) oldModel.PropertyChanged -= ModelChanged;
            if (e.NewValue is INotifyPropertyChanged model) model.PropertyChanged += ModelChanged;
            RefreshHotspotNames();
        };
    }

    public void RenderControllerState(ControllerState state, double deadZone) =>
        ControllerArtwork.RenderControllerState(state, deadZone);

    public void SetVoiceHalo(bool active) => ControllerArtwork.SetVoiceHalo(active);

    public void Highlight(TutorialInput? input)
    {
        ControllerArtwork.Highlight(input?.ArtworkLayer);
        foreach (var button in InputButtons(this))
            button.IsLinkedHighlighted = input is { } current &&
                (button is ControllerJoystick stick
                    ? stick.Input == current.Input || stick.StickInput == current.Input
                    : button.Input == current.Input && button.Direction == current.Direction);
    }

    public static IEnumerable<ControllerInputButton> InputButtons(DependencyObject parent)
    {
        if (parent is ControllerInputButton button) yield return button;
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
            foreach (var child in InputButtons(VisualTreeHelper.GetChild(parent, i)))
                yield return child;
    }

    private void AddStick(LogicalInput stick, LogicalInput press)
    {
        var bounds = ControllerArtwork.InputBounds(stick);
        var x = bounds.X;
        var y = bounds.Y;
        var width = bounds.Width;
        var height = bounds.Height;
        AddHotspot(stick, x, y - 65, width, 65, 1);
        AddHotspot(stick, x + width, y, 65, height, 2);
        AddHotspot(stick, x, y + height, width, 65, 3);
        AddHotspot(stick, x - 65, y, 65, height, 4);
        AddHotspot(press, x, y, width, height);
    }

    private void AddHotspot(LogicalInput input, double x, double y, double width, double height, int direction = 0)
    {
        var button = new ControllerInputButton
        {
            Input = input, Direction = direction, Width = width, Height = height,
            Style = (Style)FindResource("Controller.Hotspot"),
        };
        button.SetBinding(ControllerInputButton.ModeProperty, new System.Windows.Data.Binding("Mode"));
        Canvas.SetLeft(button, x);
        Canvas.SetTop(button, y);
        Hotspots.Children.Add(button);
    }

    private void ModelChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(ControllerTutorialViewModel.Items) or nameof(ControllerTutorialViewModel.Mode))
            RefreshHotspotNames();
    }

    private void RefreshHotspotNames()
    {
        if (DataContext is not ControllerTutorialViewModel model) return;
        foreach (ControllerInputButton button in Hotspots.Children)
        {
            var name = model.InputName(button.Input);
            var direction = button.Direction switch { 1 => " ↑", 2 => " →", 3 => " ↓", 4 => " ←", _ => "" };
            button.ToolTip = name + direction;
            System.Windows.Automation.AutomationProperties.SetName(button, name + direction);
        }
    }
}
