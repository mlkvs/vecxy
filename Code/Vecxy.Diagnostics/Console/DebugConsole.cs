using Autofac;
using Vecxy.Kernel;

namespace Vecxy.Diagnostics.Console;

public sealed class DebugConsole(
    IConsoleCommandParser parser,
    IConsoleCommandExecutor executor,
    IConsoleLogBuffer buffer) : IDebugConsole
{
    private int _isOpen;

    public bool IsOpen => Volatile.Read(ref _isOpen) != 0;

    public void Open() => Volatile.Write(ref _isOpen, 1);

    public void Close() => Volatile.Write(ref _isOpen, 0);

    public void Toggle() => Volatile.Write(ref _isOpen, IsOpen ? 0 : 1);

    public ConsoleExecutionResult Execute(string command)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(command);

        Write(
            new ConsoleLogEntry(
                DateTime.Now,
                ConsoleLogLevel.Command,
                "Console",
                $"> {command}",
                null));

        var parseResult = parser.Parse(command);
        if (!parseResult.Success || parseResult.Expression is null)
        {
            var failure = new ConsoleExecutionResult(
                false,
                parseResult.Error ?? "Command parse failed.",
                null,
                ConsoleLogLevel.Error);
            WriteResult(failure);
            return failure;
        }

        var executionResult = executor.Execute(parseResult.Expression);
        WriteResult(executionResult);
        return executionResult;
    }

    public void Write(ConsoleLogEntry entry) => buffer.Write(entry);

    public void Clear() => buffer.Clear();

    public IReadOnlyList<ConsoleLogEntry> GetSnapshot() => buffer.GetSnapshot();

    private void WriteResult(ConsoleExecutionResult result)
    {
        Write(
            new ConsoleLogEntry(
                DateTime.Now,
                result.Level,
                "Console",
                result.Message,
                null));
    }
}

internal sealed class AutofacConsoleObjectResolver(
    ILifetimeScope lifetimeScope) : IConsoleObjectResolver
{
    public bool TryResolve(Type objectType, out object? instance)
    {
        if (lifetimeScope.TryResolve(objectType, out instance))
            return true;

        instance = null;
        return false;
    }
}

public sealed class DebugConsoleModule(
    IConsoleRegistry registry,
    IDebugConsole console,
    ConsoleCommands commands,
    ConsoleLogSink logSink) :
    IModule
{
    public sealed class Definition : ADefinition
    {
        public override void RegisterLocal(ContainerBuilder builder)
        {
            builder
                .RegisterType<DebugConsoleModule>()
                .AsSelf()
                .As<IModule>()
                .SingleInstance();

            builder
                .RegisterType<DebugConsole>()
                .AsSelf()
                .As<IDebugConsole>()
                .SingleInstance();

            builder
                .RegisterType<ConsoleTokenizer>()
                .AsSelf()
                .SingleInstance();

            builder
                .RegisterType<ConsoleCommandParser>()
                .As<IConsoleCommandParser>()
                .SingleInstance();

            builder
                .RegisterType<ConsoleValueConverter>()
                .As<IConsoleValueConverter>()
                .SingleInstance();

            builder
                .RegisterType<ConsoleCommandExecutor>()
                .As<IConsoleCommandExecutor>()
                .SingleInstance();

            builder
                .RegisterType<ConsoleSuggestionProvider>()
                .As<IConsoleSuggestionProvider>()
                .SingleInstance();

            builder
                .RegisterType<ConsoleLogBuffer>()
                .As<IConsoleLogBuffer>()
                .SingleInstance();

            builder
                .RegisterType<ConsoleLogSink>()
                .AsSelf()
                .SingleInstance();

            builder
                .RegisterType<ConsoleCommands>()
                .AsSelf()
                .SingleInstance();

            builder
                .Register(context => new AutofacConsoleObjectResolver(
                    context.Resolve<ILifetimeScope>()))
                .As<IConsoleObjectResolver>()
                .SingleInstance();

            builder
                .RegisterType<ConsoleRegistry>()
                .As<IConsoleRegistry>()
                .SingleInstance();
        }
    }

    public void OnInitialize()
    {
        registry.Register(commands);
        logSink.Attach();
        console.Write(
            new ConsoleLogEntry(
                DateTime.Now,
                ConsoleLogLevel.Information,
                "Console",
                "Debug console initialized.",
                null));
    }

    public void OnShutdown()
    {
        logSink.Dispose();
    }

    public void Dispose()
    {
        logSink.Dispose();
    }
}
