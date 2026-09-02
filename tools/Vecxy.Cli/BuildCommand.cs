using System.Diagnostics;
using System.Runtime.InteropServices;
using Vecxy.AssetPipeline;
using Vecxy.Assets;
using Pipeline = Vecxy.AssetPipeline.AssetPipeline;

internal static class BuildCommand
{
    public static async Task<int> RunAsync(
        string project,
        string mode,
        string? platformOption,
        string? runtimeOption,
        string? outputOption,
        string? formatOption,
        string? keystore,
        string? alias,
        string? version,
        string? versionCode)
    {
        var configuration = mode.ToLowerInvariant() switch
        {
            "dev" => "Debug",
            "release" => "Release",
            _ => throw new ArgumentException("Build mode must be 'dev' or 'release'.")
        };
        var platform = ResolvePlatform(platformOption);
        var projectFile = Directory.EnumerateFiles(project, "*.csproj", SearchOption.TopDirectoryOnly).Single();
        var output = Path.GetFullPath(outputOption ?? Path.Combine(project, "artifacts", platform, mode.ToLowerInvariant()));
        Directory.CreateDirectory(output);

        Prepare(project);
        var vpackPlatform = platform switch
        {
            "android" => VPackPlatform.Android,
            "windows" => VPackPlatform.Windows,
            "linux" => VPackPlatform.Linux,
            _ => throw new ArgumentException($"Unsupported build platform '{platform}'.")
        };
        await VPackPipeline.BuildAsync(project, vpackPlatform);
        var packageDirectory = Path.Combine(project, "Build", vpackPlatform.ToString());

        if (platform == "android")
            return BuildAndroid(projectFile, configuration, output, packageDirectory, formatOption, keystore, alias, version, versionCode);
        return BuildDesktop(projectFile, configuration, output, packageDirectory, runtimeOption ?? DefaultRuntime(platform));
    }

    private static int BuildDesktop(string project, string configuration, string output, string packages, string runtime)
    {
        var arguments = new[]
        {
            "publish", project, "--configuration", configuration, "--framework", "net10.0",
            "--runtime", runtime, "--self-contained", "true", "--output", output,
            "-p:VecxyPlatform=Desktop", "-p:VecxySkipAssetPipeline=true",
            $"-p:VecxyPackagesDirectory={packages}", "-p:ErrorOnDuplicatePublishOutputFiles=false"
        };
        var result = RunDotnet(arguments);
        if (result == 0) Console.WriteLine($"Desktop build: {output}");
        return result;
    }

    private static int BuildAndroid(
        string project,
        string configuration,
        string output,
        string packages,
        string? formatOption,
        string? keystore,
        string? alias,
        string? version,
        string? versionCode)
    {
        var formats = (formatOption ?? "both").ToLowerInvariant() switch
        {
            "apk" => "apk",
            "aab" => "aab",
            "both" => "aab;apk",
            _ => throw new ArgumentException("Android format must be 'apk', 'aab', or 'both'.")
        };
        var restore = RunDotnet(["restore", project, "--runtime", "android-arm64", "-p:VecxyPlatform=Android"]);
        if (restore != 0) return restore;

        var arguments = new List<string>
        {
            "publish", project, "--configuration", configuration, "--no-restore",
            "--framework", "net10.0-android", "--runtime", "android-arm64", "--output", output,
            "-p:VecxyPlatform=Android", "-p:VecxySkipAssetPipeline=true", $"-p:VecxyPackagesDirectory={packages}",
            $"-p:AndroidPackageFormats={formats}", "-p:EmbedAssembliesIntoApk=true", "-p:AndroidEnableFastDeployment=false"
        };
        if (!string.IsNullOrWhiteSpace(version)) arguments.Add($"-p:ApplicationDisplayVersion={version}");
        if (!string.IsNullOrWhiteSpace(versionCode))
        {
            if (!int.TryParse(versionCode, out var code) || code <= 0) throw new ArgumentException("--version-code must be a positive integer.");
            arguments.Add($"-p:ApplicationVersion={code}");
        }
        if (!string.IsNullOrWhiteSpace(keystore) || !string.IsNullOrWhiteSpace(alias))
        {
            if (string.IsNullOrWhiteSpace(keystore) || !File.Exists(keystore)) throw new FileNotFoundException("A valid --keystore is required for signing.", keystore);
            if (string.IsNullOrWhiteSpace(alias)) throw new ArgumentException("--alias is required for signing.");
            var storePassword = Environment.GetEnvironmentVariable("VECXY_ANDROID_STORE_PASSWORD");
            var keyPassword = Environment.GetEnvironmentVariable("VECXY_ANDROID_KEY_PASSWORD") ?? storePassword;
            if (string.IsNullOrEmpty(storePassword)) throw new InvalidOperationException("Set VECXY_ANDROID_STORE_PASSWORD for a signed Android build.");
            arguments.AddRange(["-p:AndroidKeyStore=true", $"-p:AndroidSigningKeyStore={Path.GetFullPath(keystore)}",
                $"-p:AndroidSigningKeyAlias={alias}"]);
            var signedResult = RunDotnet(arguments, new Dictionary<string, string>
            {
                ["AndroidSigningStorePass"] = storePassword,
                ["AndroidSigningKeyPass"] = keyPassword!
            });
            if (signedResult == 0) Console.WriteLine($"Android build: {output}");
            return signedResult;
        }
        var result = RunDotnet(arguments);
        if (result == 0) Console.WriteLine($"Android build: {output}");
        return result;
    }

    private static void Prepare(string project)
    {
        var manifest = Pipeline.Scan(project);
        Pipeline.Generate(project);
        var references = Pipeline.Analyze(project);
        var missing = Pipeline.Validate(project, references);
        if (missing.Count > 0) throw new InvalidDataException($"Asset validation failed: {missing.Count} missing asset(s).");
        var packageErrors = VPackPipeline.ValidatePackageDependencies(manifest);
        if (packageErrors.Count > 0) throw new InvalidDataException(string.Join(Environment.NewLine, packageErrors));
    }

    private static string ResolvePlatform(string? value)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Equals("desktop", StringComparison.OrdinalIgnoreCase))
        {
            if (OperatingSystem.IsWindows()) return "windows";
            if (OperatingSystem.IsLinux()) return "linux";
            throw new PlatformNotSupportedException("Desktop VPack builds currently support Windows and Linux.");
        }
        return value.ToLowerInvariant();
    }

    private static string DefaultRuntime(string platform)
    {
        var architecture = RuntimeInformation.OSArchitecture switch
        {
            Architecture.Arm64 => "arm64",
            Architecture.X64 => "x64",
            _ => throw new PlatformNotSupportedException($"Unsupported architecture: {RuntimeInformation.OSArchitecture}")
        };
        return $"{platform}-{architecture}";
    }

    private static int RunDotnet(IEnumerable<string> arguments, IReadOnlyDictionary<string, string>? environment = null)
    {
        var start = new ProcessStartInfo("dotnet") { UseShellExecute = false };
        foreach (var argument in arguments) start.ArgumentList.Add(argument);
        if (environment is not null)
            foreach (var item in environment) start.Environment[item.Key] = item.Value;
        using var process = Process.Start(start) ?? throw new InvalidOperationException("Could not start dotnet.");
        process.WaitForExit();
        return process.ExitCode;
    }
}
