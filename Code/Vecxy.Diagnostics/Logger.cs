using System.Diagnostics;
using System.Runtime.CompilerServices;

namespace Vecxy.Diagnostics;

public enum ELogLevel : byte
{
    Trace = 0,
    Debug = 1,
    Info = 2,
    Warning = 3,
    Error = 4
}

public sealed record Log(ELogLevel Level, string Message, string Caller, string Timestamp)
{
    public DateTimeOffset TimestampValue { get; init; } = DateTimeOffset.Now;

    public string? StackTrace { get; init; }
}

public static class Logger
{
    private static readonly Lock Sync = new();
    private static int _level = (int)ELogLevel.Info;

    public static event Action<Log>? OnLog;

    public static ELogLevel Level
    {
        get => (ELogLevel)Volatile.Read(ref _level);
        set
        {
            if (!Enum.IsDefined(value))
            {
                throw new ArgumentOutOfRangeException(nameof(value), value, "Unknown log level.");
            }

            Volatile.Write(ref _level, (int)value);
        }
    }

    [Conditional("TRACE")]
    public static void Trace(string message, [CallerMemberName] string caller = "") =>
        Write(ELogLevel.Trace, message, caller);

    [Conditional("DEBUG")]
    public static void Debug(string message, [CallerMemberName] string caller = "") =>
        Write(ELogLevel.Debug, message, caller);

    public static void Info(string message, [CallerMemberName] string caller = "") =>
        Write(ELogLevel.Info, message, caller);

    public static void Warning(string message, [CallerMemberName] string caller = "") =>
        Write(ELogLevel.Warning, message, caller);

    public static void Error(string message, [CallerMemberName] string caller = "") =>
        Write(ELogLevel.Error, message, caller);

    public static void Error(
        Exception exception,
        string message = "",
        [CallerMemberName] string caller = "")
    {
        ArgumentNullException.ThrowIfNull(exception);

        var exceptionMessage = string.IsNullOrWhiteSpace(message)
            ? exception.ToString()
            : $"{message}{Environment.NewLine}{exception}";
        var timestamp = DateTimeOffset.Now;
        var entry = new Log(
            ELogLevel.Error,
            exceptionMessage,
            caller,
            timestamp.ToString("HH:mm:ss.fff"))
        {
            TimestampValue = timestamp,
            StackTrace = exception.ToString()
        };
        WriteCore(entry);
    }

    public static void Write(
        ELogLevel level,
        string message,
        [CallerMemberName] string caller = "")
    {
        ArgumentNullException.ThrowIfNull(message);

        if ((int)level < Volatile.Read(ref _level))
        {
            return;
        }

        var timestamp = DateTimeOffset.Now;
        var entry = new Log(
            level,
            message,
            caller,
            timestamp.ToString("HH:mm:ss.fff"))
        {
            TimestampValue = timestamp
        };
        WriteCore(entry);
    }

    private static void WriteCore(Log entry)
    {
        var formattedMessage = Format(entry);

        lock (Sync)
        {
            WriteToConsole(entry.Level, formattedMessage);
        }

        Publish(entry);
    }

    private static string Format(Log entry)
    {
        var caller = string.IsNullOrWhiteSpace(entry.Caller)
            ? string.Empty
            : $" [{entry.Caller}]";

        return $"[{entry.Timestamp}] [{entry.Level.ToString().ToUpperInvariant()}]{caller} {entry.Message}";
    }

    private static void WriteToConsole(ELogLevel level, string message)
    {
        var writer = level >= ELogLevel.Error
            ? global::System.Console.Error
            : global::System.Console.Out;

        // Mobile console implementations forward text to the platform log but do not
        // implement terminal colors. Querying ForegroundColor throws before the
        // actual message can be written on Android.
        if (OperatingSystem.IsAndroid())
        {
            writer.WriteLine(message);
            return;
        }

        if (writer == global::System.Console.Out && global::System.Console.IsOutputRedirected ||
            writer == global::System.Console.Error && global::System.Console.IsErrorRedirected)
        {
            writer.WriteLine(message);
            return;
        }

        var previousColor = global::System.Console.ForegroundColor;

        try
        {
            global::System.Console.ForegroundColor = GetColor(level);
            writer.WriteLine(message);
        }
        finally
        {
            global::System.Console.ForegroundColor = previousColor;
        }
    }

    private static ConsoleColor GetColor(ELogLevel level) =>
        level switch
        {
            ELogLevel.Trace => ConsoleColor.DarkGray,
            ELogLevel.Debug => ConsoleColor.Gray,
            ELogLevel.Info => ConsoleColor.Cyan,
            ELogLevel.Warning => ConsoleColor.Yellow,
            ELogLevel.Error => ConsoleColor.Red,
            _ => ConsoleColor.White
        };

    private static void Publish(Log entry)
    {
        var subscribers = OnLog;

        if (subscribers is null)
        {
            return;
        }

        foreach (Action<Log> subscriber in subscribers.GetInvocationList())
        {
            try
            {
                subscriber(entry);
            }
            catch (Exception exception)
            {
                lock (Sync)
                {
                    WriteToConsole(
                        ELogLevel.Error,
                        $"[{entry.Timestamp}] [LOGGER] Log subscriber failed:{Environment.NewLine}{exception}");
                }
            }
        }
    }
}
