using System.Windows.Threading;
using CodexMicro.Desktop;

namespace AgentController.MicroSurface.Wpf;

/// <summary>
/// Owns the optional Micro surface for the lifetime of Agent Controller.
/// Closing the surface hides it; only application shutdown destroys it and
/// releases the surface's independent broker lease.
/// </summary>
public sealed class MicroSurfaceController : IDisposable
{
    private readonly Dispatcher _dispatcher;
    private MicroSurfaceWindow? _window;
    private bool _disposed;

    public MicroSurfaceController()
    {
        _dispatcher = Dispatcher.CurrentDispatcher;
    }

    public bool IsVisible =>
        !_disposed &&
        _window is { IsVisible: true };

    public void Show() => Dispatch(() => Window.ShowSurface());

    public void Hide() => Dispatch(() => _window?.Hide());

    public void Toggle() => Dispatch(() =>
    {
        if (_window is { IsVisible: true })
        {
            _window.Hide();
        }
        else
        {
            Window.ShowSurface();
        }
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

    private MicroSurfaceWindow Window
    {
        get
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            return _window ??= new MicroSurfaceWindow();
        }
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
        _window?.CloseForApplicationExit();
        _window = null;
    }
}
