namespace Vecxy.CLI;

public class CLIOptionAttribute : Attribute
{
    public string LongName;
    public string ShortName;
    public bool Required;
    public object? Default;
}

public interface ICLICommand
{
    public string LongName { get; }
    public string ShortName { get; }
    public string Description { get; }
    
    public void OnExecute(object options);
}

public abstract class CLICommand<TOptions>(string longName, string shorName, string description) : ICLICommand
{
    public string LongName => longName;
    public string ShortName => shorName;
    public string Description => description;
    
    void ICLICommand.OnExecute(object options)
    {
        if (options is not TOptions value)
        {
            throw new ArgumentException("options must be of type TOptions");
        }
        
        OnExecute(value);
    }

    protected abstract void OnExecute(TOptions options);
}

public class CLIProgram
{
    public struct Result
    {
        public STATUS Status;
        public IReadOnlyList<Exception> Exceptions;
        
        public enum STATUS
        {
            PARSED = 0,
            NOT_PARSED = 1
        }
    }
    
    public List<ICLICommand> Commands { get; }

    public Result Parse(string[] args)
    {
        return new Result
        {
            Status = Result.STATUS.PARSED
        };
    }

    public void Execute()
    {
        for (int index = 0, count = Commands.Count; index < count; ++index)
        {
            var command = Commands[index];
            
            command.OnExecute(null);
        }
    }
}