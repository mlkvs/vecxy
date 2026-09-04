using System.Diagnostics;
using System.Runtime.InteropServices;

namespace Vecxy.SpriteEditor;

public sealed class ProjectFolderDialog
{
    public string? Open()
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
            return Run("zenity", ["--file-selection", "--directory", "--title", "Select Vecxy project or Assets folder"])
                   ?? Run("kdialog", ["--getexistingdirectory", Directory.GetCurrentDirectory(), "--title", "Select Vecxy project"]);
        return null;
    }

    private static string? Run(string command, IEnumerable<string> arguments)
    {
        try
        {
            var info = new ProcessStartInfo(command) { RedirectStandardOutput = true, UseShellExecute = false };
            foreach (var argument in arguments) info.ArgumentList.Add(argument);
            using var process = Process.Start(info);
            if (process is null) return null;
            var output = process.StandardOutput.ReadToEnd().Trim();
            process.WaitForExit();
            return process.ExitCode == 0 && output.Length > 0 ? output : null;
        }
        catch { return null; }
    }
}
