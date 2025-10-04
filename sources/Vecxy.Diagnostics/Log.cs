namespace Vecxy.Diagnostics;

public record Log(LogLevel Level, string Message, string Caller, string Timestamp);