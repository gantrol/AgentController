namespace AgentController.Application.Diagnostics;

public sealed class DeveloperDiagnosticsCoordinator : IDisposable
{
    private readonly object _gate = new();
    private readonly IDeveloperDiagnosticProvider _provider;
    private readonly TimeSpan _debounce;
    private readonly Dictionary<string, ActiveRun> _activeRuns;
    private readonly Dictionary<string, DeveloperDiagnosticRun> _latestRuns;
    private long _generation;
    private bool _disposed;

    public DeveloperDiagnosticsCoordinator(
        IDeveloperDiagnosticProvider provider,
        TimeSpan? debounce = null)
    {
        _provider = provider ??
            throw new ArgumentNullException(nameof(provider));
        _debounce = debounce ?? TimeSpan.FromMilliseconds(350);
        if (_debounce < TimeSpan.Zero ||
            _debounce > TimeSpan.FromSeconds(10))
        {
            throw new ArgumentOutOfRangeException(nameof(debounce));
        }

        var comparer = OperatingSystem.IsWindows()
            ? StringComparer.OrdinalIgnoreCase
            : StringComparer.Ordinal;
        _activeRuns = new(comparer);
        _latestRuns = new(comparer);
    }

    public async Task<DeveloperDiagnosticRun> AnalyzeLatestAsync(
        DeveloperDiagnosticRequest request,
        CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(request);

        var key = CreateTargetKey(request);
        var cancellation = new CancellationTokenSource();
        var generation = Interlocked.Increment(ref _generation);
        ActiveRun? previous;
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            _activeRuns.TryGetValue(key, out previous);
            _activeRuns[key] = new(generation, cancellation);
        }

        previous?.Cancellation.Cancel();
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken,
            cancellation.Token);
        try
        {
            try
            {
                await Task.Delay(_debounce, linked.Token)
                    .ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
            }

            var result = await _provider.AnalyzeAsync(request, linked.Token)
                .ConfigureAwait(false);
            lock (_gate)
            {
                if (_activeRuns.TryGetValue(key, out var current) &&
                    current.Generation == generation)
                {
                    _activeRuns.Remove(key);
                    if (result.Status !=
                        DeveloperDiagnosticRunStatus.Cancelled)
                    {
                        _latestRuns[key] = result;
                    }
                }
            }

            return result;
        }
        finally
        {
            lock (_gate)
            {
                if (_activeRuns.TryGetValue(key, out var current) &&
                    current.Generation == generation)
                {
                    _activeRuns.Remove(key);
                }
            }

            cancellation.Dispose();
        }
    }

    public bool TryGetLatest(
        string workspaceRoot,
        string targetPath,
        out DeveloperDiagnosticRun? run)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        var key = CreateTargetKey(workspaceRoot, targetPath);
        lock (_gate)
        {
            return _latestRuns.TryGetValue(key, out run);
        }
    }

    public bool Cancel(string workspaceRoot, string targetPath)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        var key = CreateTargetKey(workspaceRoot, targetPath);
        ActiveRun? active;
        lock (_gate)
        {
            if (!_activeRuns.Remove(key, out active))
            {
                return false;
            }
        }

        active.Cancellation.Cancel();
        return true;
    }

    public void Dispose()
    {
        ActiveRun[] activeRuns;
        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            activeRuns = _activeRuns.Values.ToArray();
            _activeRuns.Clear();
            _latestRuns.Clear();
        }

        foreach (var active in activeRuns)
        {
            active.Cancellation.Cancel();
        }
    }

    private static string CreateTargetKey(
        DeveloperDiagnosticRequest request) =>
        CreateTargetKey(request.WorkspaceRoot, request.TargetPath);

    private static string CreateTargetKey(
        string workspaceRoot,
        string targetPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workspaceRoot);
        ArgumentException.ThrowIfNullOrWhiteSpace(targetPath);

        var workspace = Path.GetFullPath(workspaceRoot);
        var target = Path.IsPathFullyQualified(targetPath)
            ? Path.GetFullPath(targetPath)
            : Path.GetFullPath(targetPath, workspace);
        return $"{workspace}\u001f{target}";
    }

    private sealed record ActiveRun(
        long Generation,
        CancellationTokenSource Cancellation);
}
