using System.Diagnostics;

internal static class RunCommand
{
    public static async Task<int> RunAsync(string project, string mode, string? platformOption,
        string? runtimeOption, string? outputOption, bool debug, bool noBuild, bool rebuild, string[] arguments, CancellationToken cancellationToken)
    {
        var platform = BuildCommand.ResolvePlatform(platformOption);
        var host = BuildCommand.ResolvePlatform(null);
        if (platform != host || (runtimeOption is not null && runtimeOption != BuildCommand.DefaultRuntime(host)))
            throw new ArgumentException("run only launches the current desktop platform and architecture. Use build for cross-platform packages.");
        if (debug && mode != "dev")
            throw new ArgumentException("Debugger waiting requires dev mode. Use vecxy run --debug.");
        var output = Path.GetFullPath(outputOption ?? Path.Combine(project, "artifacts", platform, mode));
        var cache = new RunBuildCache(project, mode, runtimeOption ?? BuildCommand.DefaultRuntime(host), output);
        if (!noBuild)
        {
            var ready = !rebuild && cache.IsCurrent();
            if (ready) Console.WriteLine($"Run build is up to date; reusing {mode} output.");
            if (!ready && !rebuild && cache.CanRefreshAssets())
            {
                var before = cache.CaptureInputs();
                Console.WriteLine("Only runtime assets changed; refreshing packages without compiling code.");
                ready = await AssetRefreshCommand.RunAsync(project, platform, output, cancellationToken);
                if (ready) SaveCache(before);
            }
            if (!ready)
            {
                cache.Invalidate();
                var before = cache.CaptureInputs();
                var result = await BuildCommand.RunAsync(project, mode, platform, runtimeOption, output,
                    null, null, null, null, null, cancellationToken);
                if (result != 0) return result;
                SaveCache(before);
            }
        }
        void SaveCache(Dictionary<string, string> before)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Console.WriteLine("Saving run build cache...");
            try { cache.Save(before); }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException)
            { Console.Error.WriteLine("Run cache could not be saved; the next run will build again."); }
        }
        if (!Directory.Exists(output))
            throw new DirectoryNotFoundException($"Build directory does not exist: {output}. Run vecxy run without --no-build first.");
        var configs = Directory.GetFiles(output, "*.runtimeconfig.json", SearchOption.TopDirectoryOnly);
        if (configs.Length != 1)
            throw new InvalidDataException($"Expected one game runtimeconfig.json in {output}; found {configs.Length}.");
        var name = Path.GetFileName(configs[0])[..^".runtimeconfig.json".Length];
        var executable = Path.Combine(output, name + (OperatingSystem.IsWindows() ? ".exe" : ""));
        var start = new ProcessStartInfo { UseShellExecute = false, WorkingDirectory = output };
        if (File.Exists(executable)) start.FileName = executable;
        else
        {
            var assembly = Path.Combine(output, name + ".dll");
            if (!File.Exists(assembly)) throw new FileNotFoundException("Game executable or assembly is missing.", executable);
            start.FileName = "dotnet";
            start.ArgumentList.Add(assembly);
        }
        foreach (var argument in arguments) start.ArgumentList.Add(argument);
        // The current desktop backend uses X11 when available. Set this in the
        // child environment before native window libraries load; changing only
        // managed environment variables inside Main can be too late on Wayland.
        if (OperatingSystem.IsLinux() && start.Environment.TryGetValue("DISPLAY", out var display) &&
            !string.IsNullOrWhiteSpace(display) &&
            (!start.Environment.TryGetValue("SDL_VIDEODRIVER", out var driver) ||
                string.IsNullOrEmpty(driver) || driver == "x11"))
        {
            start.Environment["SDL_VIDEODRIVER"] = "x11";
            start.Environment["XDG_SESSION_TYPE"] = "x11";
            start.Environment.Remove("WAYLAND_DISPLAY");
        }
        // Do not let a stale environment flag accidentally pause ordinary launches.
        if (debug) start.Environment["VECXY_WAIT_FOR_DEBUGGER"] = "1";
        else start.Environment.Remove("VECXY_WAIT_FOR_DEBUGGER");
        return await CliProcess.RunAsync(start, cancellationToken, child =>
        {
            Console.WriteLine($"Game process: {name}, PID {child.Id}");
            Console.WriteLine($"Working directory: {output}");
        });
    }
}
