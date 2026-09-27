using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using CodexMicro.Desktop.Controls;

namespace CodexMicro.Storybook;

public partial class StorybookWindow : Window
{
    private bool _ready;

    public StorybookWindow()
    {
        InitializeComponent();
        _ready = true;
        UpdatePreview();
    }

    private void Option_Changed(object sender, RoutedEventArgs e) => UpdatePreview();

    private void Quota_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e) => UpdatePreview();

    private void UpdatePreview()
    {
        if (!_ready)
        {
            return;
        }

        PreviewKnob.HasFiveHourWindow = SelectedTag(ProfilePicker) != "weekly";
        FiveHourControls.Visibility = PreviewKnob.HasFiveHourWindow ? Visibility.Visible : Visibility.Collapsed;
        PreviewKnob.FiveHourRemaining = PreviewKnob.HasFiveHourWindow && FiveHourUnknown.IsChecked != true
            ? FiveHourSlider.Value
            : null;
        PreviewKnob.WeeklyRemaining = WeeklyUnknown.IsChecked == true ? null : WeeklySlider.Value;
        FiveHourValue.Text = PreviewKnob.HasFiveHourWindow && PreviewKnob.FiveHourRemaining is { } fiveHour
            ? $"{fiveHour:0}%"
            : "—";
        WeeklyValue.Text = PreviewKnob.WeeklyRemaining is { } weekly ? $"{weekly:0}%" : "—";
        PreviewKnob.ModelId = SelectedTag(ModelPicker);
        PreviewKnob.ReasoningEffort = SelectedTag(EffortPicker);
        PreviewKnob.DisplayMode = Enum.Parse<QuotaKnobDisplayMode>(SelectedTag(ModePicker));
        PreviewKnob.IsUpdating = Updating.IsChecked == true;
        var scale = double.Parse(SelectedTag(ScalePicker), CultureInfo.InvariantCulture);
        PreviewScale.ScaleX = PreviewScale.ScaleY = scale;
    }

    private static string SelectedTag(ComboBox picker) =>
        (string)((ComboBoxItem)picker.SelectedItem).Tag;
}
