using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Markup;
using WpfBinding = System.Windows.Data.Binding;

namespace CodexController.Localization;

/// <summary>
/// WPF markup extension for a catalog key:
/// <code>
/// Text="{loc:Loc nav.device}"
/// Text="{loc:Loc control.wake-agent, Arg0=MENU, Arg1=Codex}"
/// </code>
/// The binding has an explicit source, so it does not replace or depend on a
/// View's DataContext.
/// </summary>
[MarkupExtensionReturnType(typeof(object))]
public sealed class LocExtension : MarkupExtension
{
    public LocExtension()
    {
    }

    public LocExtension(string key)
    {
        Key = key;
    }

    [ConstructorArgument("key")]
    public string Key { get; set; } = string.Empty;

    /// <summary>
    /// Optional literal format arguments. Dynamic values should be formatted
    /// by a ViewModel with its injected LocalizedStrings instance.
    /// </summary>
    public object? Arg0 { get; set; }

    public object? Arg1 { get; set; }

    public object? Arg2 { get; set; }

    public override object ProvideValue(
        IServiceProvider serviceProvider)
    {
        ArgumentNullException.ThrowIfNull(serviceProvider);
        return CreateBinding().ProvideValue(serviceProvider);
    }

    /// <summary>
    /// Exposed for programmatic WPF composition and unit verification.
    /// </summary>
    public WpfBinding CreateBinding()
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(Key);

        // Fail early with a useful XAML load error instead of leaving a
        // blank label when a catalog key is misspelled.
        _ = LocalizationHost.Strings.Get(Key);

        var binding = new WpfBinding
        {
            Path = new PropertyPath($"[{Key}]"),
            Source = LocalizationHost.Strings,
            Mode = BindingMode.OneWay,
        };

        var arguments = GetArguments();
        if (arguments.Length > 0)
        {
            binding.Converter = LocalizedFormatConverter.Instance;
            binding.ConverterParameter = arguments;
        }

        return binding;
    }

    private object?[] GetArguments()
    {
        if (Arg2 is not null && Arg1 is null)
        {
            throw new InvalidOperationException(
                "Arg1 is required when Arg2 is supplied.");
        }

        if (Arg1 is not null && Arg0 is null)
        {
            throw new InvalidOperationException(
                "Arg0 is required when Arg1 is supplied.");
        }

        if (Arg2 is not null)
        {
            return [Arg0, Arg1, Arg2];
        }

        if (Arg1 is not null)
        {
            return [Arg0, Arg1];
        }

        return Arg0 is null ? [] : [Arg0];
    }

    private sealed class LocalizedFormatConverter : IValueConverter
    {
        public static LocalizedFormatConverter Instance { get; } =
            new();

        public object Convert(
            object value,
            Type targetType,
            object parameter,
            CultureInfo culture)
        {
            if (
                value is not string format ||
                parameter is not object?[] arguments)
            {
                return DependencyProperty.UnsetValue;
            }

            return string.Format(
                LocalizationHost.Strings.Culture,
                format,
                arguments);
        }

        public object ConvertBack(
            object value,
            Type targetType,
            object parameter,
            CultureInfo culture)
        {
            throw new NotSupportedException(
                "Localized text bindings are one-way.");
        }
    }
}
