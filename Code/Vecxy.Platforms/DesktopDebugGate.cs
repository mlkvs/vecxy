using System.Diagnostics;

namespace Vecxy.Platforms;

/// <summary>Optional console confirmation before any desktop game initialization.</summary>
internal static class DesktopDebugGate
{
    private static int _entered;

    public static bool WaitIfRequested()
    {
        if (Environment.GetEnvironmentVariable("VECXY_WAIT_FOR_DEBUGGER") != "1" ||
            Interlocked.Exchange(ref _entered, 1) != 0) return true;

        Console.WriteLine($"Vecxy is waiting before game initialization. Attach a managed .NET debugger to PID {Environment.ProcessId}.");
        Console.WriteLine("After attaching, press Enter in this console to start the game. Ctrl+C cancels.");
        if (Console.ReadLine() is null)
        {
            Console.Error.WriteLine("Debugger confirmation input was closed. The game was not started.");
            Environment.ExitCode = 1;
            return false;
        }
        Console.WriteLine(Debugger.IsAttached
            ? "Debugger attached. Starting game."
            : "Starting game after manual confirmation (no managed debugger detected).");
        return true;
    }
}
