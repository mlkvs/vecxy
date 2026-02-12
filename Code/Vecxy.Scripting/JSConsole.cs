namespace Vecxy.Scripting;

[JSGlobal(Name = "console")]
public class JSConsole
{
    public void log(params object[] args)
    {
        Console.WriteLine($"[JS]: {string.Join(" ", args)}");
    }
}