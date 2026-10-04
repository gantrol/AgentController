using System.ComponentModel;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;
using Media = System.Windows.Media;
using WpfApplication = System.Windows.Application;

namespace CodexController.Themes.Controls;

// Keep the native tray menu's activation, dismissal and keyboard handling.
internal sealed class TrayMenu : ContextMenuStrip
{
    private Font? _menuFont;

    internal TrayMenu()
    {
        ShowImageMargin = false;
        ShowCheckMargin = false;
    }

    protected override void OnOpening(CancelEventArgs e)
    {
        var resources = WpfApplication.Current;
        var family = (Media.FontFamily)resources.FindResource("Font.Ui");
        var size = (double)resources.FindResource("Font.Size.Caption");
        var nextFont = SystemInformation.HighContrast
            ? (Font)SystemFonts.MenuFont!.Clone()
            : new Font(
                family.Source.Split(',')[0].Trim(),
                Math.Max((float)(size * 72 / 96), SystemFonts.MenuFont!.SizeInPoints),
                FontStyle.Regular,
                GraphicsUnit.Point);
        var previousFont = _menuFont;
        Font = _menuFont = nextFont;
        previousFont?.Dispose();

        Padding = new Padding(LogicalToDeviceUnits(6));
        MinimumSize = new Size(LogicalToDeviceUnits(196), 0);
        foreach (ToolStripItem item in Items)
        {
            item.Padding = item is ToolStripSeparator
                ? new Padding(0, LogicalToDeviceUnits(4), 0, LogicalToDeviceUnits(4))
                : new Padding(
                    LogicalToDeviceUnits(10), LogicalToDeviceUnits(6),
                    LogicalToDeviceUnits(10), LogicalToDeviceUnits(6));
        }

        Renderer = SystemInformation.HighContrast
            ? new ToolStripSystemRenderer()
            : new MenuRenderer();
        UpdateRegion();
        base.OnOpening(e);
    }

    protected override void OnSizeChanged(EventArgs e)
    {
        base.OnSizeChanged(e);
        UpdateRegion();
    }

    private void UpdateRegion()
    {
        var previous = Region;
        if (SystemInformation.HighContrast || Width < 2 || Height < 2)
        {
            Region = null;
        }
        else
        {
            using var path = RoundedRectangle(new RectangleF(0, 0, Width, Height), 8 * DeviceDpi / 96f);
            Region = new Region(path);
        }
        previous?.Dispose();
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        if (disposing)
        {
            _menuFont?.Dispose();
            _menuFont = null;
        }
    }

    private static Color ThemeColor(string key)
    {
        var brush = (Media.SolidColorBrush)WpfApplication.Current.FindResource(key);
        var color = brush.Color;
        return Color.FromArgb(color.A, color.R, color.G, color.B);
    }

    private static GraphicsPath RoundedRectangle(RectangleF bounds, float radius)
    {
        var diameter = Math.Min(2 * radius, Math.Min(bounds.Width, bounds.Height));
        var path = new GraphicsPath();
        path.AddArc(bounds.Left, bounds.Top, diameter, diameter, 180, 90);
        path.AddArc(bounds.Right - diameter, bounds.Top, diameter, diameter, 270, 90);
        path.AddArc(bounds.Right - diameter, bounds.Bottom - diameter, diameter, diameter, 0, 90);
        path.AddArc(bounds.Left, bounds.Bottom - diameter, diameter, diameter, 90, 90);
        path.CloseFigure();
        return path;
    }

    private sealed class MenuColors : ProfessionalColorTable
    {
        internal Color Highlight { get; } = ThemeColor("Brush.Bg.Surface");
        internal Color Surface { get; } = ThemeColor("Brush.Bg.Panel");
        internal Color Line { get; } = ThemeColor("Brush.Border.Subtle");
        internal Color Hover { get; } = ThemeColor("Brush.Accent.Soft");
        internal Color Text { get; } = ThemeColor("Brush.Text.Primary");
        internal Color DisabledText { get; } = ThemeColor("Brush.Text.Secondary");

        internal MenuColors() => UseSystemColors = false;

        public override Color ToolStripDropDownBackground => Surface;
        public override Color ImageMarginGradientBegin => Surface;
        public override Color ImageMarginGradientMiddle => Surface;
        public override Color ImageMarginGradientEnd => Surface;
        public override Color MenuBorder => Line;
        public override Color MenuItemSelected => Hover;
        public override Color MenuItemBorder => Line;
        public override Color SeparatorDark => Line;
        public override Color SeparatorLight => Surface;
    }

    private sealed class MenuRenderer : ToolStripProfessionalRenderer
    {
        private readonly MenuColors _colors;

        internal MenuRenderer() : this(new MenuColors()) { }

        private MenuRenderer(MenuColors colors) : base(colors)
        {
            _colors = colors;
            RoundedEdges = false;
        }

        protected override void OnRenderToolStripBackground(ToolStripRenderEventArgs e)
        {
            var tint = Color.FromArgb(
                (_colors.Surface.R + _colors.Hover.R) / 2,
                (_colors.Surface.G + _colors.Hover.G) / 2,
                (_colors.Surface.B + _colors.Hover.B) / 2);
            using var brush = new LinearGradientBrush(
                e.ToolStrip.ClientRectangle, _colors.Highlight, tint, 90)
            {
                InterpolationColors = new ColorBlend
                {
                    Colors = [_colors.Highlight, _colors.Surface, tint],
                    Positions = [0, 0.3f, 1],
                },
            };
            e.Graphics.FillRectangle(brush, e.ToolStrip.ClientRectangle);
        }

        protected override void OnRenderToolStripBorder(ToolStripRenderEventArgs e)
        {
            using var path = RoundedRectangle(
                new RectangleF(0.5f, 0.5f, e.ToolStrip.Width - 1, e.ToolStrip.Height - 1),
                8 * e.ToolStrip.DeviceDpi / 96f);
            using var brush = new LinearGradientBrush(
                e.ToolStrip.ClientRectangle, _colors.Highlight, _colors.Line, 45);
            using var pen = new Pen(brush, 1);
            var state = e.Graphics.Save();
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            e.Graphics.DrawPath(pen, path);
            e.Graphics.Restore(state);
        }

        protected override void OnRenderMenuItemBackground(ToolStripItemRenderEventArgs e)
        {
            if (!e.Item.Selected || !e.Item.Enabled)
            {
                return;
            }

            var scale = (e.ToolStrip?.DeviceDpi ?? 96) / 96f;
            var bounds = new RectangleF(1, 1, e.Item.Width - 2, e.Item.Height - 2);
            using var path = RoundedRectangle(bounds, 4 * scale);
            using var brush = new SolidBrush(_colors.Hover);
            var state = e.Graphics.Save();
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            e.Graphics.FillPath(brush, path);
            e.Graphics.Restore(state);
        }

        protected override void OnRenderItemText(ToolStripItemTextRenderEventArgs e)
        {
            e.TextColor = e.Item.Enabled ? _colors.Text : _colors.DisabledText;
            base.OnRenderItemText(e);
        }

        protected override void OnRenderSeparator(ToolStripSeparatorRenderEventArgs e)
        {
            var inset = (int)Math.Round(10 * (e.ToolStrip?.DeviceDpi ?? 96) / 96d);
            using var pen = new Pen(_colors.Line);
            e.Graphics.DrawLine(pen, inset, e.Item.Height / 2, e.Item.Width - inset, e.Item.Height / 2);
        }
    }
}
