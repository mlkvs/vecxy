using System.Runtime.CompilerServices;

namespace Vecxy.Diagnostics;

public enum LOG_LEVEL : byte
{
    TRACE = 0,
    DEBUG = 1,
    INFO = 2,
    WARNING = 3,
    ERROR = 4
}

public record Log(LOG_LEVEL Level, string Message, string Caller, string Timestamp);

public static class Logger
{
    public static event Action<Log>? OnLog;
    public static LOG_LEVEL Level { get; set; } = LOG_LEVEL.INFO;

    public static void Info(string message, [CallerMemberName] string caller = "")
    {
        Log(LOG_LEVEL.INFO, message, caller);
    }

    public static void Warning(string message, [CallerMemberName] string caller = "")
    {
        Log(LOG_LEVEL.WARNING, message, caller);
    }

    public static void Error(string message, [CallerMemberName] string caller = "")
    {
        Log(LOG_LEVEL.ERROR, message, caller);
    }

    public static void Error(Exception exception, string message = "", [CallerMemberName] string caller = "")
    {
        Log(LOG_LEVEL.ERROR, $"{message}: {exception}", caller);
    }

    private static void Log(LOG_LEVEL level, string message, string caller)
    {
        if (level < Level)
        {
            return;
        }

        var timestamp = DateTime.Now.ToString("HH:mm:ss.fff");

        var logMessage = $"[{timestamp}] [{level}] [{caller}] {message}";

        Console.WriteLine(logMessage);

        var log = new Log(level, message, caller, timestamp);

        OnLog?.Invoke(log);
    }
}