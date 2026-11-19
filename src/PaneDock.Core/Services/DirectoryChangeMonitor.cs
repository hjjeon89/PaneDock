namespace PaneDock.Core.Services;

/// <summary>
/// Observes a single directory (not its descendants). Bursty OS notifications are
/// coalesced into one refresh request. A failed watcher is retried until disposed.
/// Events are raised on ThreadPool threads; the UI must dispatch them.
/// </summary>
public sealed class DirectoryChangeMonitor : IDisposable
{
    private const int RetryDelayMilliseconds = 3000;
    private readonly object _gate = new();
    private readonly string _path;
    private readonly int _quietMilliseconds;
    private readonly Timer _quietTimer;
    private readonly Timer _retryTimer;
    private FileSystemWatcher? _watcher;
    private bool _disposed;

    public event EventHandler? Changed;
    public event Action<Exception>? MonitoringFailed;
    public event EventHandler? MonitoringRestored;

    public Exception? LastError { get; private set; }
    public string Path => _path;

    public DirectoryChangeMonitor(string path, TimeSpan? quietPeriod = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        var period = quietPeriod ?? TimeSpan.FromMilliseconds(400);
        if (period < TimeSpan.FromMilliseconds(50) || period > TimeSpan.FromSeconds(10))
            throw new ArgumentOutOfRangeException(nameof(quietPeriod));

        _path = System.IO.Path.GetFullPath(path);
        _quietMilliseconds = checked((int)period.TotalMilliseconds);
        _quietTimer = new Timer(_ => RaiseChanged(), null, Timeout.Infinite, Timeout.Infinite);
        _retryTimer = new Timer(_ => Connect(), null, Timeout.Infinite, Timeout.Infinite);
        Connect();
    }

    private void Connect()
    {
        bool restored;
        lock (_gate)
        {
            if (_disposed || _watcher is not null)
                return;

            restored = LastError is not null;
            FileSystemWatcher? watcher = null;
            try
            {
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
                _watcher = watcher;
                LastError = null;
                _retryTimer.Change(Timeout.Infinite, Timeout.Infinite);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException
                                       or ArgumentException or System.ComponentModel.Win32Exception)
            {
                watcher?.Dispose();
                LastError = ex;
                _retryTimer.Change(RetryDelayMilliseconds, Timeout.Infinite);
                return;
            }
        }

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
            _retryTimer.Change(RetryDelayMilliseconds, Timeout.Infinite);
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
            _watcher?.Dispose();
            _watcher = null;
            Changed = null;
            MonitoringFailed = null;
            MonitoringRestored = null;
        }
    }
}
