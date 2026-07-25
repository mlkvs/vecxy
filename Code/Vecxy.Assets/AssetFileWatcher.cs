using System.Collections.Concurrent;
using Vecxy.Diagnostics;

namespace Vecxy.Assets;

internal sealed class AssetFileWatcher : IDisposable
{
    private readonly string _assetsDirectory;
    private readonly ConcurrentQueue<string> _changes = new();
    private readonly FileSystemWatcher _watcher;
    private bool _disposed;

    public AssetFileWatcher(string assetsDirectory)
    {
        _assetsDirectory = assetsDirectory;
        _watcher = new FileSystemWatcher(assetsDirectory)
        {
            IncludeSubdirectories = true,
            NotifyFilter =
                NotifyFilters.FileName |
                NotifyFilters.LastWrite |
                NotifyFilters.Size
        };

        _watcher.Changed += OnChanged;
        _watcher.Created += OnChanged;
        _watcher.Deleted += OnChanged;
        _watcher.Renamed += OnRenamed;
        _watcher.Error += OnError;
    }

    public void Start()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        _watcher.EnableRaisingEvents = true;
    }

    public void Drain(Action<string> accept)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(accept);

        while (_changes.TryDequeue(out var path))
        {
            accept(path);
        }
    }

    private void OnChanged(object sender, FileSystemEventArgs eventArgs)
    {
        Enqueue(eventArgs.FullPath);
    }

    private void OnRenamed(object sender, RenamedEventArgs eventArgs)
    {
        Enqueue(eventArgs.OldFullPath);
        Enqueue(eventArgs.FullPath);
    }

    private void Enqueue(string fullPath)
    {
        try
        {
            var relativePath = Path.GetRelativePath(_assetsDirectory, fullPath);
            _changes.Enqueue(AssetsModule.NormalizePath(relativePath));
        }
        catch (Exception exception)
        {
            Logger.Error(exception, $"Cannot process asset file change: {fullPath}");
        }
    }

    private static void OnError(object sender, ErrorEventArgs eventArgs)
    {
        Logger.Error(eventArgs.GetException(), "Asset file watcher failed.");
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _watcher.EnableRaisingEvents = false;
        _watcher.Changed -= OnChanged;
        _watcher.Created -= OnChanged;
        _watcher.Deleted -= OnChanged;
        _watcher.Renamed -= OnRenamed;
        _watcher.Error -= OnError;
        _watcher.Dispose();

        while (_changes.TryDequeue(out _))
        {
        }
    }
}
