using System.Runtime.CompilerServices;

namespace Vecxy.Diagnostics;

public static class Logger
{
    public static event Action<Log> OnLog;
    public static LogLevel Level { get; set; } = LogLevel.Info;
    
    
    public static void Info(string message, [CallerMemberName] string caller = "")
    {
        Log(LogLevel.Info, message, caller);
    }
    
    public static void Warning(string message, [CallerMemberName] string caller = "")
    {
        Log(LogLevel.Warning, message, caller);
    }
    
    public static void Error(string message, [CallerMemberName] string caller = "")
    {
        Log(LogLevel.Error, message, caller);
    }
    
    public static void Error(Exception exception, string message = "", [CallerMemberName] string caller = "")
    {
        Log(LogLevel.Error, $"{message}: {exception}", caller);
    }
    
    private static void Log(LogLevel level, string message, string caller)
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