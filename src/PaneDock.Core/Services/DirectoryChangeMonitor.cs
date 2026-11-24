namespace PaneDock.Core.Services;

/// <summary>
/// Observes a single directory (not its descendants). Bursty OS notifications are
/// coalesced into one refresh request. A failed watcher is retried until disposed.
/// Events are raised on ThreadPool threads; the UI must dispatch them.
/// </summary>
public sealed class DirectoryChangeMonitor : IDisposable
{
    private readonly object _gate = new();
    private readonly string _path;
    private readonly int _quietMilliseconds;
    private readonly int _retryMilliseconds;
    private readonly Func<string, bool> _pathAvailable;
    private readonly bool _requiresHealthProbe;
    private readonly Timer _quietTimer;
    private readonly Timer _retryTimer;
    private readonly Timer _healthTimer;
    private FileSystemWatcher? _watcher;
    private bool _connecting;
    private bool _checkingHealth;
    private bool _disposed;

    public event EventHandler? Changed;
    public event Action<Exception>? MonitoringFailed;
    public event EventHandler? MonitoringRestored;

    public Exception? LastError { get; private set; }
    public string Path => _path;

    public DirectoryChangeMonitor(
        string path, TimeSpan? quietPeriod = null,
        TimeSpan? retryPeriod = null, TimeSpan? healthPeriod = null,
        Func<string, bool>? availabilityProbe = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        var period = quietPeriod ?? TimeSpan.FromMilliseconds(400);
        if (period < TimeSpan.FromMilliseconds(50) || period > TimeSpan.FromSeconds(10))
            throw new ArgumentOutOfRangeException(nameof(quietPeriod));
        var retry = retryPeriod ?? TimeSpan.FromSeconds(3);
        var health = healthPeriod ?? TimeSpan.FromSeconds(6);
        if (retry < TimeSpan.FromMilliseconds(100) || retry > TimeSpan.FromMinutes(1))
            throw new ArgumentOutOfRangeException(nameof(retryPeriod));
        if (health < TimeSpan.FromMilliseconds(100) || health > TimeSpan.FromMinutes(1))
            throw new ArgumentOutOfRangeException(nameof(healthPeriod));

        _path = System.IO.Path.GetFullPath(path);
        _quietMilliseconds = checked((int)period.TotalMilliseconds);
        _retryMilliseconds = checked((int)retry.TotalMilliseconds);
        _pathAvailable = availabilityProbe ?? Directory.Exists;
        _requiresHealthProbe = availabilityProbe is not null
            || NetworkPathService.IsNetworkPath(_path);
        _quietTimer = new Timer(_ => RaiseChanged(), null, Timeout.Infinite, Timeout.Infinite);
        _retryTimer = new Timer(_ => Connect(), null, Timeout.Infinite, Timeout.Infinite);
        _healthTimer = new Timer(_ => CheckHealth(), null,
            _requiresHealthProbe ? checked((int)health.TotalMilliseconds) : Timeout.Infinite,
            _requiresHealthProbe ? checked((int)health.TotalMilliseconds) : Timeout.Infinite);
        // A disconnected UNC share can block during watcher creation; never
        // do that potentially slow network call on the WPF UI thread.
        if (_requiresHealthProbe && availabilityProbe is null)
            ThreadPool.QueueUserWorkItem(_ => Connect());
        else
            Connect();
    }

    private void Connect()
    {
        lock (_gate)
        {
            if (_disposed || _watcher is not null || _connecting)
                return;
            _connecting = true;
        }

        // FileSystemWatcher creation/EnableRaisingEvents may wait on an SMB
        // redirector. Do not hold _gate while performing any remote I/O:
        // Dispose() must never wait for a hung server.
        FileSystemWatcher? watcher = null;
        Exception? failure = null;
        try
        {
            // A normal local watcher constructor already checks the path.
            // Avoid an extra filesystem round-trip on every folder navigation.
            if (_requiresHealthProbe && !_pathAvailable(_path))
                throw new IOException("폴더가 없거나 연결이 끊겼습니다: " + _path);
            watcher = new FileSystemWatcher(_path)
            {
                IncludeSubdirectories = false,
                NotifyFilter = NotifyFilters.FileName | NotifyFilters.DirectoryName
                    | NotifyFilters.LastWrite | NotifyFilters.Size
                    | NotifyFilters.Attributes | NotifyFilters.CreationTime,
                InternalBufferSize = 16 * 1024
            };
            watcher.Changed += OnChanged;
            watcher.Created += OnChanged;
            watcher.Deleted += OnChanged;
            watcher.Renamed += OnRenamed;
            watcher.Error += OnError;
            watcher.EnableRaisingEvents = true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException
                                   or ArgumentException or System.ComponentModel.Win32Exception)
        {
            watcher?.Dispose();
            watcher = null;
            failure = ex;
        }

        bool restored = false;
        bool notifyFailed = false;
        bool discard;
        lock (_gate)
        {
            _connecting = false;
            discard = _disposed || _watcher is not null;
            if (!discard)
            {
                if (failure is not null)
                {
                    notifyFailed = LastError is null;
                    LastError = failure;
                    _retryTimer.Change(_retryMilliseconds, Timeout.Infinite);
                }
                else
                {
                    restored = LastError is not null;
                    _watcher = watcher;
                    LastError = null;
                    _retryTimer.Change(Timeout.Infinite, Timeout.Infinite);
                }
            }
        }

        if (discard)
            watcher?.Dispose();
        else if (notifyFailed)
            MonitoringFailed?.Invoke(failure!);
        if (restored)
        {
            // An OS watcher does not replay events missed during a disconnect.
            MonitoringRestored?.Invoke(this, EventArgs.Empty);
            QueueChange();
        }
    }

    private void OnChanged(object sender, FileSystemEventArgs e) => QueueChange();
    private void OnRenamed(object sender, RenamedEventArgs e) => QueueChange();

    private void QueueChange()
    {
        lock (_gate)
        {
            if (!_disposed)
                _quietTimer.Change(_quietMilliseconds, Timeout.Infinite);
        }
    }

    private void RaiseChanged()
    {
        lock (_gate)
        {
            if (_disposed)
                return;
        }
        Changed?.Invoke(this, EventArgs.Empty);
    }

    private void CheckHealth()
    {
        FileSystemWatcher? watcher;
        lock (_gate)
        {
            if (_disposed || _watcher is null || _checkingHealth)
                return;
            watcher = _watcher;
            _checkingHealth = true;
        }

        // Some SMB disconnects don't raise FileSystemWatcher.Error. Checking
        // reachability on a ThreadPool timer allows the regular retry path
        // to reconnect even when Windows never reports the lost watcher.
        // A slow SMB request may exceed one timer period; don't accumulate
        // overlapping probes and exhaust ThreadPool workers.
        try
        {
            if (!_pathAvailable(_path))
                OnError(watcher, new ErrorEventArgs(
                    new IOException("폴더 연결이 끊겼거나 접근할 수 없습니다: " + _path)));
        }
        finally
        {
            lock (_gate)
                _checkingHealth = false;
        }
    }

    private void OnError(object sender, ErrorEventArgs e)
    {
        var error = e.GetException();
        FileSystemWatcher? old;
        lock (_gate)
        {
            if (_disposed || !ReferenceEquals(sender, _watcher))
                return;
            old = _watcher;
            _watcher = null;
            LastError = error;
            _retryTimer.Change(_retryMilliseconds, Timeout.Infinite);
        }
        old?.Dispose();
        MonitoringFailed?.Invoke(error);
        QueueChange(); // Covers buffer overflow by forcing a complete listing.
    }

    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed)
                return;
            _disposed = true;
            _quietTimer.Dispose();
            _retryTimer.Dispose();
            _healthTimer.Dispose();
            _watcher?.Dispose();
            _watcher = null;
            Changed = null;
            MonitoringFailed = null;
            MonitoringRestored = null;
        }
    }
}
