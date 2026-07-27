using System.Reflection;

namespace Vecxy.Diagnostics.Console;

public enum ConsoleLogLevel
{
    Trace,
    Debug,
    Information,
    Warning,
    Error,
    Critical,
    Command,
    CommandResult
}

public enum ConsoleMemberKind
{
    Field,
    Property,
    Method
}

public enum ConsoleAssignmentOperator
{
    Assign,
    AddAssign,
    SubtractAssign,
    MultiplyAssign,
    DivideAssign
}

public enum ConsoleSuggestionKind
{
    Object,
    Member,
    Method,
    Value,
    Signature,
    Alias
}

public sealed record ConsoleLogEntry(
    DateTime Timestamp,
    ConsoleLogLevel Level,
    string Category,
    string Message,
    string? StackTrace);

public sealed record ConsoleExecutionResult(
    bool Success,
    string Message,
    object? Value = null,
    ConsoleLogLevel Level = ConsoleLogLevel.CommandResult);

public sealed record ConsoleParseResult(
    bool Success,
    ConsoleExpression? Expression,
    string? Error);

public sealed record ConsoleSuggestion(
    ConsoleSuggestionKind Kind,
    string DisplayText,
    string InsertText,
    string? Description,
    int ReplaceStart,
    int ReplaceLength);

public sealed record ConsoleParameterDescriptor(
    string Name,
    Type ParameterType,
    bool IsOptional,
    object? DefaultValue);

public sealed record FileDialogFilter(
    string Name,
    IReadOnlyList<string> Patterns);

public interface IDebugConsole
{
    bool IsOpen { get; }

    void Open();
    void Close();
    void Toggle();

    ConsoleExecutionResult Execute(string command);

    void Write(ConsoleLogEntry entry);
    void Clear();
    IReadOnlyList<ConsoleLogEntry> GetSnapshot();
}

public interface IConsoleRegistry
{
    void Register(object instance);
    void Register(Type staticType);
    void RegisterAssembly(Assembly assembly);

    bool Unregister(object instance);

    IReadOnlyCollection<IConsoleObjectDescriptor> Objects { get; }
}

public interface IConsoleCommandParser
{
    ConsoleParseResult Parse(string input);
}

public interface IConsoleCommandExecutor
{
    ConsoleExecutionResult Execute(ConsoleExpression expression);
}

public interface IConsoleValueConverter
{
    bool TryConvert(
        ConsoleValueNode source,
        Type targetType,
        out object? value,
        out string? error);
}

public interface IConsoleSuggestionProvider
{
    IReadOnlyList<ConsoleSuggestion> GetSuggestions(
        string input,
        int cursorPosition);
}

public interface IConsoleObjectResolver
{
    bool TryResolve(Type objectType, out object? instance);
}

public interface IConsoleLogBuffer
{
    void Write(ConsoleLogEntry entry);
    void Clear();
    IReadOnlyList<ConsoleLogEntry> GetSnapshot();
}

public interface ISystemFileDialog
{
    string? ShowSaveFileDialog(
        string title,
        string defaultFileName,
        IReadOnlyList<FileDialogFilter> filters);
}

public interface IConsoleObjectDescriptor
{
    string Name { get; }
    string? Description { get; }
    Type ObjectType { get; }
    bool IsStatic { get; }
    object? RegisteredInstance { get; }
    IReadOnlyDictionary<string, IConsoleMemberDescriptor> Members { get; }

    bool TryGetMember(string name, out IConsoleMemberDescriptor descriptor);
    bool TryResolveTarget(out object? target, out string? error);
}

public interface IConsoleMemberDescriptor
{
    string Name { get; }
    string QualifiedName { get; }
    string? Description { get; }
    ConsoleMemberKind Kind { get; }
    ConsoleAccess Access { get; }
    Type ValueType { get; }
    bool IsStatic { get; }
    bool IsReadable { get; }
    bool IsWritable { get; }
    bool IsExecutable { get; }
    IReadOnlyList<ConsoleParameterDescriptor> Parameters { get; }
    MemberInfo MemberInfo { get; }
    string DisplaySignature { get; }

    object? Read(object? target);
    void Write(object? target, object? value);
    object? Invoke(object? target, object?[] arguments);
}
