using System.Globalization;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;

namespace CodexMicro.Desktop.Controls;

public enum QuotaKnobDisplayMode
{
    Auto,
    Quota,
    Model,
}

public sealed class QuotaKnob : Button
{
    public static readonly DependencyProperty FiveHourRemainingProperty =
        DependencyProperty.Register(nameof(FiveHourRemaining), typeof(double?),
            typeof(QuotaKnob), new PropertyMetadata(null, OnReadoutChanged));
    public static readonly DependencyProperty WeeklyRemainingProperty =
        DependencyProperty.Register(nameof(WeeklyRemaining), typeof(double?),
            typeof(QuotaKnob), new PropertyMetadata(null, OnReadoutChanged));
    public static readonly DependencyProperty ModelIdProperty =
        DependencyProperty.Register(nameof(ModelId), typeof(string),
            typeof(QuotaKnob), new PropertyMetadata(string.Empty, OnReadoutChanged));
    public static readonly DependencyProperty ReasoningEffortProperty =
        DependencyProperty.Register(nameof(ReasoningEffort), typeof(string),
            typeof(QuotaKnob), new PropertyMetadata(string.Empty, OnReadoutChanged));
    public static readonly DependencyProperty DisplayModeProperty =
        DependencyProperty.Register(nameof(DisplayMode), typeof(QuotaKnobDisplayMode),
            typeof(QuotaKnob), new PropertyMetadata(QuotaKnobDisplayMode.Auto, OnReadoutChanged));
    public static readonly DependencyProperty HasFiveHourWindowProperty =
        DependencyProperty.Register(nameof(HasFiveHourWindow), typeof(bool),
            typeof(QuotaKnob), new PropertyMetadata(true, OnReadoutChanged));
    public static readonly DependencyProperty HasWeeklyWindowProperty =
        DependencyProperty.Register(nameof(HasWeeklyWindow), typeof(bool),
            typeof(QuotaKnob), new PropertyMetadata(true, OnReadoutChanged));
    public static readonly DependencyProperty IsUpdatingProperty =
        DependencyProperty.Register(nameof(IsUpdating), typeof(bool),
            typeof(QuotaKnob), new PropertyMetadata(false, OnReadoutChanged));
    public static readonly DependencyProperty UseQuotaReadoutProperty =
        DependencyProperty.Register(nameof(UseQuotaReadout), typeof(bool),
            typeof(QuotaKnob), new PropertyMetadata(true, OnContentModeChanged));
    public static readonly DependencyProperty FallbackContentProperty =
        DependencyProperty.Register(nameof(FallbackContent), typeof(object),
            typeof(QuotaKnob), new PropertyMetadata(null, OnContentModeChanged));

    private readonly Viewbox _quotaContent;

    private readonly TextBlock _modelVersion = new()
    {
        FontFamily = new FontFamily("Segoe UI Variable Display, Segoe UI"),
        FontSize = 16,
        LineHeight = 18,
        LineStackingStrategy = LineStackingStrategy.BlockLineHeight,
        FontWeight = FontWeights.SemiBold,
        Foreground = new SolidColorBrush(Color.FromRgb(0xF7, 0xFA, 0xFF)),
        HorizontalAlignment = HorizontalAlignment.Center,
    };
    private readonly TextBlock _modelFamily = new()
    {
        FontFamily = new FontFamily("Segoe UI Variable Text, Segoe UI"),
        FontSize = 14,
        FontWeight = FontWeights.SemiBold,
        LineHeight = 16,
        LineStackingStrategy = LineStackingStrategy.BlockLineHeight,
        Foreground = new SolidColorBrush(Color.FromRgb(0xDD, 0xE7, 0xF2)),
        HorizontalAlignment = HorizontalAlignment.Center,
    };
    private readonly SevenSegmentReadout _fiveHourValue = new();
    private readonly SevenSegmentReadout _weeklyValue = new();
    private readonly SevenSegmentReadout _singleQuotaValue = new();
    private readonly Grid _diagonalReadout = new()
    {
        Width = 34,
        Height = 32,
        HorizontalAlignment = HorizontalAlignment.Center,
        VerticalAlignment = VerticalAlignment.Center,
    };
    private readonly Viewbox _modelReadout = new()
    {
        MaxWidth = 38,
        MaxHeight = 34,
        Stretch = Stretch.Uniform,
        StretchDirection = StretchDirection.DownOnly,
        HorizontalAlignment = HorizontalAlignment.Center,
        VerticalAlignment = VerticalAlignment.Center,
    };
    private readonly Viewbox _singleQuotaReadout = new()
    {
        Width = 36,
        Height = 20,
        Stretch = Stretch.Uniform,
        HorizontalAlignment = HorizontalAlignment.Center,
        VerticalAlignment = VerticalAlignment.Center,
    };
    private readonly Ellipse _fiveHourTrack = new()
    {
        Width = 47,
        Height = 47,
        Stroke = new SolidColorBrush(Color.FromArgb(0x2E, 0xFF, 0xFF, 0xFF)),
        StrokeThickness = 1.4,
    };
    private readonly Ellipse _weeklyTrack = new()
    {
        Width = 41,
        Height = 41,
        Stroke = new SolidColorBrush(Color.FromArgb(0x2E, 0xFF, 0xFF, 0xFF)),
        StrokeThickness = 1.4,
    };
    private readonly Path _fiveHourProgress = CreateProgressRing();
    private readonly Path _weeklyProgress = CreateProgressRing();
    private readonly Path _loadingArc = CreateProgressRing();
    private readonly RotateTransform _loadingRotation = new(0, 26, 26);
    private bool _loadingAnimationRunning;

    private static Path CreateProgressRing() => new()
    {
        StrokeThickness = 1.6,
        StrokeStartLineCap = PenLineCap.Round,
        StrokeEndLineCap = PenLineCap.Round,
    };
    private bool _keyboardPreview;

    public QuotaKnob()
    {
        Width = Height = 96;
        SetResourceReference(StyleProperty, "DarkKnobButton");
        var gauge = new Grid { Width = 52, Height = 52 };
        _weeklyProgress.RenderTransform = new ScaleTransform(41d / 47, 41d / 47, 26, 26);
        gauge.Children.Add(_fiveHourTrack);
        gauge.Children.Add(_weeklyTrack);
        gauge.Children.Add(_fiveHourProgress);
        gauge.Children.Add(_weeklyProgress);
        _loadingArc.Data = MicroSurfaceWindow.CreateQuotaArcGeometry(24);
        _loadingArc.Stroke = new SolidColorBrush(Color.FromRgb(0x9E, 0xBD, 0xFF));
        _loadingArc.StrokeThickness = 1.25;
        var loadingTransform = new TransformGroup();
        loadingTransform.Children.Add(new ScaleTransform(51d / 47, 51d / 47, 26, 26));
        loadingTransform.Children.Add(_loadingRotation);
        _loadingArc.RenderTransform = loadingTransform;
        gauge.Children.Add(_loadingArc);
        var readout = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        readout.Children.Add(_modelVersion);
        readout.Children.Add(_modelFamily);
        _modelReadout.Child = readout;
        gauge.Children.Add(_modelReadout);
        _singleQuotaReadout.Child = _singleQuotaValue;
        gauge.Children.Add(_singleQuotaReadout);
        _diagonalReadout.Children.Add(new Viewbox
        {
            MaxWidth = 27,
            Height = 14,
            Stretch = Stretch.Uniform,
            StretchDirection = StretchDirection.DownOnly,
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Top,
            Child = _fiveHourValue,
        });
        _diagonalReadout.Children.Add(new Viewbox
        {
            MaxWidth = 27,
            Height = 14,
            Stretch = Stretch.Uniform,
            StretchDirection = StretchDirection.DownOnly,
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Bottom,
            Child = _weeklyValue,
        });
        _diagonalReadout.Children.Add(new Line
        {
            X1 = 14,
            Y1 = 18,
            X2 = 20,
            Y2 = 14,
            Stroke = new SolidColorBrush(Color.FromArgb(0x80, 0xDD, 0xE7, 0xF2)),
            StrokeThickness = 0.8,
            StrokeStartLineCap = PenLineCap.Round,
            StrokeEndLineCap = PenLineCap.Round,
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Top,
        });
        gauge.Children.Add(_diagonalReadout);
        _quotaContent = new Viewbox
        {
            Width = 58,
            Height = 58,
            Stretch = Stretch.Uniform,
            IsHitTestVisible = false,
            Child = gauge,
        };
        Loaded += (_, _) => UpdateLoadingAnimation();
        Unloaded += (_, _) => UpdateLoadingAnimation();
        IsVisibleChanged += (_, _) => UpdateLoadingAnimation();
        UpdateContent();
    }

    public double? FiveHourRemaining
    {
        get => (double?)GetValue(FiveHourRemainingProperty);
        set => SetValue(FiveHourRemainingProperty, value);
    }

    public double? WeeklyRemaining
    {
        get => (double?)GetValue(WeeklyRemainingProperty);
        set => SetValue(WeeklyRemainingProperty, value);
    }

    public string ModelId
    {
        get => (string)GetValue(ModelIdProperty);
        set => SetValue(ModelIdProperty, value);
    }

    public string ReasoningEffort
    {
        get => (string)GetValue(ReasoningEffortProperty);
        set => SetValue(ReasoningEffortProperty, value);
    }

    public QuotaKnobDisplayMode DisplayMode
    {
        get => (QuotaKnobDisplayMode)GetValue(DisplayModeProperty);
        set => SetValue(DisplayModeProperty, value);
    }

    public bool HasFiveHourWindow
    {
        get => (bool)GetValue(HasFiveHourWindowProperty);
        set => SetValue(HasFiveHourWindowProperty, value);
    }

    public bool HasWeeklyWindow
    {
        get => (bool)GetValue(HasWeeklyWindowProperty);
        set => SetValue(HasWeeklyWindowProperty, value);
    }

    public bool IsUpdating
    {
        get => (bool)GetValue(IsUpdatingProperty);
        set => SetValue(IsUpdatingProperty, value);
    }

    public bool UseQuotaReadout
    {
        get => (bool)GetValue(UseQuotaReadoutProperty);
        set => SetValue(UseQuotaReadoutProperty, value);
    }

    public object? FallbackContent
    {
        get => GetValue(FallbackContentProperty);
        set => SetValue(FallbackContentProperty, value);
    }

    protected override void OnMouseEnter(MouseEventArgs e)
    {
        base.OnMouseEnter(e);
        UpdateReadout();
    }

    protected override void OnMouseLeave(MouseEventArgs e)
    {
        base.OnMouseLeave(e);
        UpdateReadout();
    }

    protected override void OnGotKeyboardFocus(KeyboardFocusChangedEventArgs e)
    {
        base.OnGotKeyboardFocus(e);
        _keyboardPreview = !IsMouseOver;
        UpdateReadout();
    }

    protected override void OnLostKeyboardFocus(KeyboardFocusChangedEventArgs e)
    {
        base.OnLostKeyboardFocus(e);
        _keyboardPreview = false;
        UpdateReadout();
    }

    protected override void OnPreviewMouseDown(MouseButtonEventArgs e)
    {
        _keyboardPreview = false;
        base.OnPreviewMouseDown(e);
    }

    private static void OnReadoutChanged(DependencyObject sender, DependencyPropertyChangedEventArgs e) =>
        ((QuotaKnob)sender).UpdateReadout();

    private static void OnContentModeChanged(DependencyObject sender, DependencyPropertyChangedEventArgs e) =>
        ((QuotaKnob)sender).UpdateContent();

    private void UpdateContent()
    {
        Content = UseQuotaReadout ? _quotaContent : FallbackContent;
        UpdateReadout();
    }

    private void UpdateReadout()
    {
        UpdateLoadingAnimation();
        if (!UseQuotaReadout)
        {
            return;
        }

        var showModel = DisplayMode == QuotaKnobDisplayMode.Model ||
            DisplayMode == QuotaKnobDisplayMode.Auto && (IsMouseOver || _keyboardPreview);
        var fiveHour = Normalize(FiveHourRemaining);
        var weekly = Normalize(WeeklyRemaining);
        var quotaText = (HasFiveHourWindow, HasWeeklyWindow) switch
        {
            (true, true) => $"{FormatPercent(fiveHour)}/{FormatPercent(weekly)}",
            (true, false) => FormatPercent(fiveHour),
            (false, true) => FormatPercent(weekly),
            _ => "—",
        };
        var showDiagonalQuota = !showModel && HasFiveHourWindow && HasWeeklyWindow;
        _diagonalReadout.Visibility = showDiagonalQuota ? Visibility.Visible : Visibility.Collapsed;
        _modelReadout.Visibility = showModel ? Visibility.Visible : Visibility.Collapsed;
        _singleQuotaReadout.Visibility = !showModel && !showDiagonalQuota ? Visibility.Visible : Visibility.Collapsed;
        _fiveHourValue.Text = FormatPercent(fiveHour);
        _weeklyValue.Text = FormatPercent(weekly);
        _singleQuotaValue.Text = quotaText;
        var (modelVersion, modelFamily) = FormatModel(ModelId);
        _modelVersion.Text = IsUpdating ? "···" : modelVersion;
        _modelFamily.Text = modelFamily;

        if (showModel)
        {
            var effort = ParseEffortRank(ReasoningEffort);
            var outerLevel = effort is { } rank ? Math.Min(rank + 1, 3) * (100d / 3) : (double?)null;
            var innerLevel = effort is { } innerRank ? Math.Max(0, innerRank - 2) * (100d / 3) : (double?)null;
            var effortColor = effort switch
            {
                <= 2 => Color.FromRgb(0xA8, 0xC7, 0xFF),
                <= 4 => Color.FromRgb(0xCE, 0xB1, 0xFF),
                5 => Color.FromRgb(0xFF, 0xD2, 0x7A),
                _ => Color.FromRgb(0x9B, 0xDB, 0xBD),
            };
            _weeklyTrack.Width = _weeklyTrack.Height = 41;
            _weeklyProgress.RenderTransform = new ScaleTransform(41d / 47, 41d / 47, 26, 26);
            UpdateRing(_fiveHourTrack, _fiveHourProgress, true, outerLevel, effortColor);
            UpdateRing(_weeklyTrack, _weeklyProgress, true, innerLevel, effortColor);
        }
        else
        {
            UpdateRing(_fiveHourTrack, _fiveHourProgress, HasFiveHourWindow, fiveHour,
                Color.FromRgb(0xA8, 0xC7, 0xFF));
            var weeklyDiameter = HasFiveHourWindow ? 41d : 47d;
            _weeklyTrack.Width = _weeklyTrack.Height = weeklyDiameter;
            _weeklyProgress.RenderTransform = new ScaleTransform(weeklyDiameter / 47, weeklyDiameter / 47, 26, 26);
            UpdateRing(_weeklyTrack, _weeklyProgress, HasWeeklyWindow, weekly,
                Color.FromRgb(0x9B, 0xDB, 0xBD));
        }
        var quotaName = (HasFiveHourWindow, HasWeeklyWindow) switch
        {
            (true, true) => $"5h {FormatPercent(fiveHour)} · 周 {FormatPercent(weekly)}",
            (true, false) => $"5h {FormatPercent(fiveHour)}",
            (false, true) => $"周 {FormatPercent(weekly)}",
            _ => "—",
        };
        AutomationProperties.SetName(this, showModel
            ? $"{_modelVersion.Text} {_modelFamily.Text} · {ReasoningEffort}"
            : quotaName);
    }

    private void UpdateLoadingAnimation()
    {
        var active = UseQuotaReadout && IsUpdating && IsLoaded && IsVisible;
        _loadingArc.Visibility = active ? Visibility.Visible : Visibility.Collapsed;
        if (active == _loadingAnimationRunning)
        {
            return;
        }

        _loadingRotation.BeginAnimation(RotateTransform.AngleProperty, active
            ? new DoubleAnimation
            {
                From = 0,
                To = 360,
                Duration = TimeSpan.FromMilliseconds(820),
                RepeatBehavior = RepeatBehavior.Forever,
            }
            : null);
        _loadingAnimationRunning = active;
    }

    private static void UpdateRing(Ellipse track, Path progress, bool available, double? remaining, Color accent)
    {
        track.Visibility = progress.Visibility = available ? Visibility.Visible : Visibility.Collapsed;
        progress.Data = remaining is { } percent
            ? MicroSurfaceWindow.CreateQuotaArcGeometry(percent)
            : Geometry.Empty;
        progress.Stroke = new SolidColorBrush(remaining switch
        {
            <= 10 => Color.FromRgb(0xFF, 0x9E, 0x8B),
            <= 30 => Color.FromRgb(0xFF, 0xD2, 0x7A),
            _ => accent,
        });
    }

    private static double? Normalize(double? value) =>
        value is { } percent && double.IsFinite(percent) ? Math.Clamp(percent, 0, 100) : null;

    private static string FormatPercent(double? value) => value is { } percent
        ? Math.Round(percent, MidpointRounding.AwayFromZero).ToString(CultureInfo.InvariantCulture) + "%"
        : "—";

    private static (string Version, string Family) FormatModel(string? modelId)
    {
        if (string.IsNullOrWhiteSpace(modelId))
        {
            return ("—", "—");
        }

        var label = modelId.Trim();
        if (label.StartsWith("gpt-", StringComparison.OrdinalIgnoreCase))
        {
            label = label[4..];
        }
        var parts = label.Split('-', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length == 0)
        {
            return ("—", "—");
        }

        var version = parts[0];
        var family = parts.Length > 1
            ? CultureInfo.InvariantCulture.TextInfo.ToTitleCase(
                string.Join(' ', parts.Skip(1)).ToLowerInvariant())
            : "—";
        return (version, family);
    }

    private static int? ParseEffortRank(string? effort) => effort?.Trim().ToLowerInvariant() switch
    {
        "low" => 0,
        "medium" => 1,
        "high" => 2,
        "xhigh" => 3,
        "max" => 4,
        "ultra" => 5,
        _ => null,
    };
}
