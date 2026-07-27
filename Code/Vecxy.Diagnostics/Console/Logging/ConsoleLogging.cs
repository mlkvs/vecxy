namespace Vecxy.Diagnostics.Console;

public sealed class ConsoleLogBuffer(int capacity = 10_000) : IConsoleLogBuffer
{
    private readonly object _sync = new();
    private readonly Queue<ConsoleLogEntry> _entries = new(Math.Max(1, capacity));

    public int Capacity { get; } = Math.Max(1, capacity);

    public void Write(ConsoleLogEntry entry)
    {
        lock (_sync)
        {
            _entries.Enqueue(entry);
            while (_entries.Count > Capacity)
                _entries.Dequeue();
        }
    }

    public void Clear()
    {
        lock (_sync)
        {
            _entries.Clear();
        }
    }

    public IReadOnlyList<ConsoleLogEntry> GetSnapshot()
    {
        lock (_sync)
        {
            return _entries.ToArray();
        }
    }
}

public sealed class ConsoleLogSink(IConsoleLogBuffer buffer) : IDisposable
{
    private bool _attached;

    public void Attach()
    {
        if (_attached)
            return;

        Logger.OnLog += OnLog;
        _attached = true;
    }

    public void Dispose()
    {
        if (!_attached)
            return;

        Logger.OnLog -= OnLog;
        _attached = false;
    }

    private void OnLog(Log log)
    {
        buffer.Write(
            new ConsoleLogEntry(
                log.TimestampValue.LocalDateTime,
                MapLevel(log.Level),
                string.IsNullOrWhiteSpace(log.Caller) ? "Runtime" : log.Caller,
                log.Message,
                log.StackTrace));
    }

    private static ConsoleLogLevel MapLevel(ELogLevel level) =>
        level switch
        {
            ELogLevel.Trace => ConsoleLogLevel.Trace,
            ELogLevel.Debug => ConsoleLogLevel.Debug,
            ELogLevel.Info => ConsoleLogLevel.Information,
            ELogLevel.Warning => ConsoleLogLevel.Warning,
            ELogLevel.Error => ConsoleLogLevel.Error,
            _ => ConsoleLogLevel.Information
        };
}
