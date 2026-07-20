using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using CodexController.Models;

namespace CodexController.Presentation;

internal static class UiTypography
{
    private static readonly IReadOnlyDictionary<string, double>
        MediumTokenSizes = new Dictionary<string, double>
        {
            ["Font.Size.Caption"] = 14,
            ["Font.Size.Label"] = 15,
            ["Font.Size.Body"] = 16,
            ["Font.Size.BodyLg"] = 17,
            ["Font.Size.Title"] = 19,
            ["Font.Size.TitleLg"] = 21,
            ["Font.Size.Display"] = 25,
            ["Font.Size.Hero"] = 29,
        };

    private static string _currentSize = UiTextSizes.Medium;
    private static double _currentScale = 1;

    internal static void Apply(
        string? size,
        bool resizeOpenWindows = true)
    {
        var normalized = UiTextSizes.Normalize(size);
        var nextScale = UiTextSizes.Scale(normalized);
        var application = System.Windows.Application.Current;
        if (application is null)
        {
            _currentSize = normalized;
            _currentScale = nextScale;
            return;
        }

        foreach (var (key, mediumSize) in MediumTokenSizes)
        {
            application.Resources[key] =
                Math.Round(mediumSize * nextScale, 1);
        }

        if (
            resizeOpenWindows &&
            Math.Abs(nextScale - _currentScale) > 0.001)
        {
            var ratio = nextScale / _currentScale;
            foreach (Window window in application.Windows)
            {
                ScaleVisualTree(window, ratio);
            }
        }

        _currentSize = normalized;
        _currentScale = nextScale;
    }

    internal static string CurrentSize => _currentSize;

    private static void ScaleVisualTree(
        DependencyObject root,
        double ratio)
    {
        var targets = new List<FontTarget>();
        CollectFontTargets(root, targets);
        foreach (var target in targets)
        {
            target.Element.SetCurrentValue(
                target.Property,
                Math.Clamp(
                    Math.Round(target.Size * ratio, 1),
                    8,
                    64));
        }
    }

    private static void CollectFontTargets(
        DependencyObject element,
        ICollection<FontTarget> targets)
    {
        if (element is TextBlock textBlock)
        {
            AddIfOwned(
                textBlock,
                TextBlock.FontSizeProperty,
                textBlock.FontSize,
                targets);
        }
        else if (element is System.Windows.Controls.Control control)
        {
            AddIfOwned(
                control,
                System.Windows.Controls.Control.FontSizeProperty,
                control.FontSize,
                targets);
        }

        for (var index = 0;
             index < VisualTreeHelper.GetChildrenCount(element);
             index++)
        {
            CollectFontTargets(
                VisualTreeHelper.GetChild(element, index),
                targets);
        }
    }

    private static void AddIfOwned(
        DependencyObject element,
        DependencyProperty property,
        double size,
        ICollection<FontTarget> targets)
    {
        var source = DependencyPropertyHelper.GetValueSource(
            element,
            property);
        if (
            source.BaseValueSource == BaseValueSource.Inherited ||
            source.IsExpression)
        {
            return;
        }

        targets.Add(new FontTarget(element, property, size));
    }

    private sealed record FontTarget(
        DependencyObject Element,
        DependencyProperty Property,
        double Size);
}
