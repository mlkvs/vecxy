using System.Diagnostics;

internal static class CliProcess
{
    public static async Task<int> RunAsync(ProcessStartInfo start, CancellationToken cancellationToken,
        Action<Process>? started = null)
    {
        cancellationToken.ThrowIfCancellationRequested();
        using var process = Process.Start(start) ?? throw new InvalidOperationException($"Could not start {start.FileName}.");
        try
        {
            started?.Invoke(process);
            await process.WaitForExitAsync(cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            return process.ExitCode;
        }
        finally
        {
            if (!process.HasExited)
            {
                try { process.Kill(entireProcessTree: true); }
                catch (InvalidOperationException) when (process.HasExited) { }
                await process.WaitForExitAsync();
            }
        }
    }

    public static async Task<int> WithCancellation(Func<CancellationToken, Task<int>> work)
    {
        using var cancellation = new CancellationTokenSource();
        ConsoleCancelEventHandler cancel = (_, args) =>
        {
            args.Cancel = true;
            cancellation.Cancel();
        };
        Console.CancelKeyPress += cancel;
        try
        {
            return await work(cancellation.Token);
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
        {
            Console.Error.WriteLine("Vecxy cancelled.");
            return 130;
        }
        finally
        {
            Console.CancelKeyPress -= cancel;
        }
    }
}
