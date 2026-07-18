using System.Runtime.InteropServices;
using Drawing = System.Drawing;
using Forms = System.Windows.Forms;

namespace CodexMicro.Desktop.Services;

internal sealed class TrayIconController : IDisposable
{
    private Drawing.Icon _icon;
    private readonly Forms.ContextMenuStrip _menu;
    private readonly Forms.ToolStripMenuItem _topmostItem;
    private readonly Forms.NotifyIcon _notifyIcon;
    private bool _disposed;

    public TrayIconController(
        Action showWindow,
        Action reconnect,
        Action<bool> setTopmost,
        Action exit,
        bool topmost)
    {
        ArgumentNullException.ThrowIfNull(showWindow);
        ArgumentNullException.ThrowIfNull(reconnect);
        ArgumentNullException.ThrowIfNull(setTopmost);
        ArgumentNullException.ThrowIfNull(exit);

        _menu = new Forms.ContextMenuStrip();
        var showItem = new Forms.ToolStripMenuItem("显示键盘");
        showItem.Click += (_, _) => showWindow();
        var reconnectItem = new Forms.ToolStripMenuItem("重新连接虚拟 HID");
        reconnectItem.Click += (_, _) => reconnect();
        _topmostItem = new Forms.ToolStripMenuItem("窗口置顶")
        {
            CheckOnClick = true,
            Checked = topmost,
        };
        _topmostItem.Click += (_, _) => setTopmost(_topmostItem.Checked);
        var exitItem = new Forms.ToolStripMenuItem("退出");
        exitItem.Click += (_, _) => exit();

        _menu.Items.Add(showItem);
        _menu.Items.Add(reconnectItem);
        _menu.Items.Add(new Forms.ToolStripSeparator());
        _menu.Items.Add(_topmostItem);
        _menu.Items.Add(new Forms.ToolStripSeparator());
        _menu.Items.Add(exitItem);

        _icon = CreateApplicationIcon();
        _notifyIcon = new Forms.NotifyIcon
        {
            ContextMenuStrip = _menu,
            Icon = _icon,
            Text = "Codex Micro Simulator",
            Visible = true,
        };
        _notifyIcon.DoubleClick += (_, _) => showWindow();
        EnsureVisible();
    }

    public void SetTopmost(bool value)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        _topmostItem.Checked = value;
    }

    public void EnsureVisible()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        _notifyIcon.Visible = true;
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _notifyIcon.Visible = false;
        _notifyIcon.Dispose();
        _menu.Dispose();
        _icon.Dispose();
    }

    internal static Drawing.Icon CreateApplicationIcon()
    {
        using var bitmap = new Drawing.Bitmap(
            32,
            32,
            Drawing.Imaging.PixelFormat.Format32bppArgb);
        using var graphics = Drawing.Graphics.FromImage(bitmap);
        graphics.SmoothingMode = Drawing.Drawing2D.SmoothingMode.AntiAlias;
        graphics.Clear(Drawing.Color.Transparent);

        using var shadowPath = CreateRoundedRectangle(
            new Drawing.RectangleF(2.5f, 3.5f, 27, 27),
            6);
        using var bodyPath = CreateRoundedRectangle(
            new Drawing.RectangleF(2.5f, 2.5f, 27, 27),
            6);
        using var shadow = new Drawing.SolidBrush(
            Drawing.Color.FromArgb(90, 38, 48, 54));
        using var body = new Drawing.SolidBrush(
            Drawing.Color.FromArgb(255, 242, 241, 237));
        using var border = new Drawing.Pen(
            Drawing.Color.FromArgb(255, 125, 136, 143),
            1.2f);
        graphics.FillPath(shadow, shadowPath);
        graphics.FillPath(body, bodyPath);
        graphics.DrawPath(border, bodyPath);

        using var knobRing = new Drawing.SolidBrush(
            Drawing.Color.FromArgb(255, 211, 211, 205));
        using var knobFace = new Drawing.SolidBrush(
            Drawing.Color.FromArgb(255, 42, 39, 36));
        using var knobMarker = new Drawing.Pen(
            Drawing.Color.FromArgb(255, 238, 237, 232),
            1.5f);
        graphics.FillEllipse(knobRing, 5, 5, 11, 11);
        graphics.FillEllipse(knobFace, 6.5f, 6.5f, 8, 8);
        graphics.DrawLine(knobMarker, 10.5f, 7.6f, 10.5f, 10.3f);

        using var key = new Drawing.SolidBrush(
            Drawing.Color.FromArgb(255, 111, 102, 184));
        using var keyHighlight = new Drawing.SolidBrush(
            Drawing.Color.FromArgb(180, 255, 255, 255));
        foreach (var keyRect in new[]
        {
            new Drawing.RectangleF(18, 7, 7, 7),
            new Drawing.RectangleF(7, 18, 7, 7),
            new Drawing.RectangleF(18, 18, 7, 7),
        })
        {
            using var keyPath = CreateRoundedRectangle(keyRect, 2);
            graphics.FillPath(key, keyPath);
            graphics.FillEllipse(
                keyHighlight,
                keyRect.X + 2.2f,
                keyRect.Y + 2.2f,
                2.6f,
                2.6f);
        }

        var handle = bitmap.GetHicon();
        try
        {
            using var borrowed = Drawing.Icon.FromHandle(handle);
            return (Drawing.Icon)borrowed.Clone();
        }
        finally
        {
            _ = DestroyIcon(handle);
        }
    }

    private static Drawing.Drawing2D.GraphicsPath CreateRoundedRectangle(
        Drawing.RectangleF bounds,
        float radius)
    {
        var diameter = radius * 2;
        var path = new Drawing.Drawing2D.GraphicsPath();
        path.AddArc(bounds.Left, bounds.Top, diameter, diameter, 180, 90);
        path.AddArc(bounds.Right - diameter, bounds.Top, diameter, diameter, 270, 90);
        path.AddArc(
            bounds.Right - diameter,
            bounds.Bottom - diameter,
            diameter,
            diameter,
            0,
            90);
        path.AddArc(bounds.Left, bounds.Bottom - diameter, diameter, diameter, 90, 90);
        path.CloseFigure();
        return path;
    }

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DestroyIcon(IntPtr iconHandle);
}
