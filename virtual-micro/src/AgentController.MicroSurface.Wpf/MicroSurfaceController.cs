using System.Windows;
using System.Windows.Threading;
using CodexMicro.Desktop;
using CodexMicro.Desktop.Services;

namespace AgentController.MicroSurface.Wpf;

/// <summary>
/// Owns all Micro surfaces for the lifetime of Agent Controller. The process,
/// tray and plugin registry remain singular while each surface keeps its own
/// Agent target, controls, navigation state and persistent placement.
/// </summary>
public sealed class MicroSurfaceController : IDisposable
{
    private sealed record SurfaceEntry(
        string Id,
        MicroProfileSettings Settings,
        MicroSurfaceWindow Window,
        bool IsPrimary);

    private readonly Dispatcher _dispatcher;
    private readonly MicroLocalization _localization;
    private readonly Dictionary<string, SurfaceEntry> _surfaces =
        new(StringComparer.OrdinalIgnoreCase);
    private int _nextOrdinal = 1;
    private bool _disposed;

    public MicroSurfaceController(
        MicroLocalization? localization = null)
    {
        _dispatcher = Dispatcher.CurrentDispatcher;
        _localization = localization ?? new MicroLocalization();
        CreateSurface(
            "primary",
            new MicroProfileSettings(),
            isPrimary: true);
        foreach (var settings in MicroProfileSettings.LoadAdditionalKeypads())
        {
            var id = settings.PersistentKeypadId;
            if (id is not null)
            {
                CreateSurface(id, settings, isPrimary: false);
            }
        }
    }

    public event EventHandler? SurfacesChanged;

    public bool IsVisible =>
        !_disposed &&
        _surfaces.Values.Any(entry => entry.Window.IsVisible);

    public int SurfaceCount => _disposed ? 0 : _surfaces.Count;

    public void Show() => Dispatch(() =>
    {
        foreach (var entry in _surfaces.Values)
        {
            entry.Window.ShowSurface();
        }

        SurfacesChanged?.Invoke(this, EventArgs.Empty);
    });

    public void Hide() => Dispatch(() =>
    {
        foreach (var entry in _surfaces.Values)
        {
            entry.Window.Hide();
        }

        SurfacesChanged?.Invoke(this, EventArgs.Empty);
    });

    public void Toggle() => Dispatch(() =>
    {
        if (_surfaces.Values.Any(entry => entry.Window.IsVisible))
        {
            foreach (var entry in _surfaces.Values)
            {
                entry.Window.Hide();
            }
        }
        else
        {
            foreach (var entry in _surfaces.Values)
            {
                entry.Window.ShowSurface();
            }
        }

        SurfacesChanged?.Invoke(this, EventArgs.Empty);
    });

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        if (_dispatcher.CheckAccess())
        {
            DisposeOnDispatcher();
        }
        else
        {
            _dispatcher.Invoke(DisposeOnDispatcher);
        }
    }

    private void CreateSurface(
        string id,
        MicroProfileSettings settings,
        bool isPrimary)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        var ordinal = _nextOrdinal++;
        var displayName = settings.Current.KeypadName ??
            (_localization.IsEnglish
                ? $"Keypad {ordinal}"
                : $"小键盘 {ordinal}");
        if (!isPrimary && settings.Current.KeypadName is null)
        {
            settings.SetKeypadName(displayName);
        }

        var window = new MicroSurfaceWindow(
            _localization,
            settings,
            harnessId => OpenHarnessInNewSurface(id, harnessId),
            () => CloseSurface(id),
            displayName,
            canCloseKeypad: !isPrimary);
        _surfaces.Add(
            id,
            new SurfaceEntry(id, settings, window, isPrimary));
    }

    private void OpenHarnessInNewSurface(
        string sourceId,
        string harnessId)
    {
        if (!_surfaces.TryGetValue(sourceId, out var source))
        {
            return;
        }

        var id = Guid.NewGuid().ToString("N");
        var ordinal = _nextOrdinal;
        var displayName = _localization.IsEnglish
            ? $"Keypad {ordinal}"
            : $"小键盘 {ordinal}";
        var current = source.Settings.Current;
        var left = double.IsFinite(source.Window.Left)
            ? source.Window.Left + 24
            : (double?)null;
        var top = double.IsFinite(source.Window.Top)
            ? source.Window.Top + 24
            : (double?)null;
        var initial = current with
        {
            ActiveHarnessId = harnessId,
            KeypadName = displayName,
            WindowLeft = ClampLeft(left),
            WindowTop = ClampTop(top),
            WindowTopmost = source.Window.Topmost,
        };
        var settings = MicroProfileSettings.CreateForKeypad(id, initial);
        CreateSurface(id, settings, isPrimary: false);
        _surfaces[id].Window.ShowSurface();
        SurfacesChanged?.Invoke(this, EventArgs.Empty);
    }

    private void CloseSurface(string id)
    {
        if (!_surfaces.TryGetValue(id, out var entry))
        {
            return;
        }

        if (entry.IsPrimary)
        {
            entry.Window.Hide();
            SurfacesChanged?.Invoke(this, EventArgs.Empty);
            return;
        }

        _surfaces.Remove(id);
        entry.Window.CloseForApplicationExit();
        _ = entry.Settings.DeletePersistentKeypad();
        SurfacesChanged?.Invoke(this, EventArgs.Empty);
    }

    private static double? ClampLeft(double? value)
    {
        if (value is not { } left || !double.IsFinite(left))
        {
            return null;
        }

        var maximum = SystemParameters.VirtualScreenLeft +
            Math.Max(0, SystemParameters.VirtualScreenWidth - 442.5);
        return Math.Clamp(left, SystemParameters.VirtualScreenLeft, maximum);
    }

    private static double? ClampTop(double? value)
    {
        if (value is not { } top || !double.IsFinite(top))
        {
            return null;
        }

        var maximum = SystemParameters.VirtualScreenTop +
            Math.Max(0, SystemParameters.VirtualScreenHeight - 457.5);
        return Math.Clamp(top, SystemParameters.VirtualScreenTop, maximum);
    }

    private void Dispatch(Action action)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_dispatcher.CheckAccess())
        {
            action();
        }
        else
        {
            _dispatcher.Invoke(action);
        }
    }

    private void DisposeOnDispatcher()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        foreach (var entry in _surfaces.Values.ToArray())
        {
            entry.Window.CloseForApplicationExit();
        }

        _surfaces.Clear();
    }
}
