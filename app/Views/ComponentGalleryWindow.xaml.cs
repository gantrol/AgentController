using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using CodexController.Presentation;

namespace CodexController.Views;

public partial class ComponentGalleryWindow : Window
{
    public static readonly DependencyProperty PreviewWidthProperty = DependencyProperty.Register(
        nameof(PreviewWidth), typeof(double), typeof(ComponentGalleryWindow), new PropertyMetadata(440d));

    private IReadOnlyList<ComponentGallerySample> _samples = [];
    private bool _ready;

    public ComponentGalleryWindow()
    {
        InitializeComponent();
        SearchLabel.Text = ComponentGalleryCatalog.Text("搜索", "Search");
        WidthLabel.Text = ComponentGalleryCatalog.Text("宽度", "Width");
        ResetButton.ToolTip = ComponentGalleryCatalog.Text("重置", "Reset");
        AutomationProperties.SetName(SearchBox, SearchLabel.Text);
        AutomationProperties.SetName(PreviewWidthBox, WidthLabel.Text);
        AutomationProperties.SetName(ResetButton, ResetButton.ToolTip.ToString()!);
        AutomationProperties.SetName(CategoryList, ComponentGalleryCatalog.Text("分类", "Categories"));
        PreviewWidthBox.ItemsSource = new[] { 280d, 360d, 440d };
        PreviewWidthBox.SelectedItem = PreviewWidth;
        ReloadSamples();
        _ready = true;
        ApplyFilter();
    }

    public double PreviewWidth
    {
        get => (double)GetValue(PreviewWidthProperty);
        set => SetValue(PreviewWidthProperty, value);
    }

    private void ReloadSamples()
    {
        _samples = ComponentGalleryCatalog.Build();
        CategoryList.ItemsSource = new[] { ComponentGalleryCatalog.Text("全部", "All") }
            .Concat(_samples.Select(sample => sample.Category).Distinct()).ToArray();
        CategoryList.SelectedIndex = 0;
    }

    private void ApplyFilter()
    {
        if (!_ready) return;
        var query = SearchBox.Text.Trim();
        var category = CategoryList.SelectedItem as string;
        var visible = _samples.Where(sample =>
            (CategoryList.SelectedIndex <= 0 || sample.Category == category) &&
            (query.Length == 0 || sample.Key.Contains(query, StringComparison.OrdinalIgnoreCase) ||
             sample.Category.Contains(query, StringComparison.OrdinalIgnoreCase))).ToArray();
        SectionList.ItemsSource = visible.GroupBy(sample => sample.Category)
            .Select(group => new { Name = group.Key, Samples = group.ToArray() }).ToArray();
        SampleCount.Text = $"{visible.Length} / {_samples.Count}";
        GalleryScroll.ScrollToTop();
    }

    private void SearchBox_TextChanged(object sender, TextChangedEventArgs e) => ApplyFilter();

    private void CategoryList_SelectionChanged(object sender, SelectionChangedEventArgs e) => ApplyFilter();

    private void PreviewWidthBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (PreviewWidthBox.SelectedItem is double width) PreviewWidth = width;
    }

    private void ResetButton_Click(object sender, RoutedEventArgs e)
    {
        _ready = false;
        SearchBox.Clear();
        ReloadSamples();
        _ready = true;
        ApplyFilter();
    }
}
