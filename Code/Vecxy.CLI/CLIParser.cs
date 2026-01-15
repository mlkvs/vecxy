namespace Vecxy.CLI;

public interface ICLICommand
{
    public string Name { get; }
}

public abstract class CLICommandBase<TParameters> : ICLICommand
{
    public abstract string Name { get; }

    public abstract void Execute(TParameters parameters);
}

[AttributeUsage(AttributeTargets.Field)]
public class CLIParameterAttribute : Attribute
{
    public string Name;
    public string Alias;
    public object Default;
}

public abstract class CLIParser
{
    public static void Parse(string[] args, ICLICommand[] commands)
    {
    }

    public static ICLICommand Command<TParameters>(string name, Action<TParameters> execute) => new TempCommand<TParameters>(name, execute);
    
    private class TempCommand<TParameters>(string name, Action<TParameters> execute) : CLICommandBase<TParameters>
    {
        public override string Name => name;
        
        public override void Execute(TParameters parameters)
        {
            execute(parameters);
        }
    }
}