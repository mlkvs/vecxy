using System.Diagnostics;
using System.Runtime.InteropServices;

namespace Vecxy.Isometric.Editor;

public sealed class EditorFileDialog
{
    public string? OpenAssetsFolder()
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            const string script = "$d=New-Object System.Windows.Forms.FolderBrowserDialog;" +
                                  "$d.Description='Select project or Assets folder';" +
                                  "if($d.ShowDialog() -eq 'OK'){[Console]::Write($d.SelectedPath)}";
            return Run("powershell", ["-NoProfile", "-STA", "-Command",
                "Add-Type -AssemblyName System.Windows.Forms;" + script]);
        }
        return Run("zenity", ["--file-selection", "--directory", "--title", "Select project or Assets folder"])
               ?? Run("kdialog", ["--getexistingdirectory", Directory.GetCurrentDirectory()]);
    }

    public string? OpenTexture(string? initialDirectory)
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            var initial = EscapePowerShell(initialDirectory ?? Directory.GetCurrentDirectory());
            var script = "$d=New-Object System.Windows.Forms.OpenFileDialog;" +
                         "$d.Title='Select a texture';" +
                         "$d.Filter='Images (*.png;*.jpg;*.jpeg;*.bmp;*.tga)|*.png;*.jpg;*.jpeg;*.bmp;*.tga|All files (*.*)|*.*';" +
                         $"$d.InitialDirectory='{initial}';" +
                         "if($d.ShowDialog() -eq 'OK'){[Console]::Write($d.FileName)}";
            return Run("powershell", ["-NoProfile", "-STA", "-Command",
                "Add-Type -AssemblyName System.Windows.Forms;" + script]);
        }
        return Run("zenity", ["--file-selection", "--title", "Select a texture", "--filename", (initialDirectory ?? string.Empty) + Path.DirectorySeparatorChar])
               ?? Run("kdialog", ["--getopenfilename", initialDirectory ?? Directory.GetCurrentDirectory(), "Images (*.png *.jpg *.jpeg *.bmp *.tga)"]);
    }

    private static string EscapePowerShell(string value) => value.Replace("'", "''", StringComparison.Ordinal);

    private static string? Run(string command, IEnumerable<string> arguments)
    {
        try
        {
            var info = new ProcessStartInfo(command)
            {
                RedirectStandardOutput = true,
                UseShellExecute = false,
                CreateNoWindow = true,
                WindowStyle = ProcessWindowStyle.Hidden
            };
            foreach (var argument in arguments) info.ArgumentList.Add(argument);
            using var process = Process.Start(info);
            if (process is null) return null;
            var output = process.StandardOutput.ReadToEnd().Trim();
            process.WaitForExit();
            return process.ExitCode == 0 && output.Length > 0 ? output : null;
        }
        catch
        {
            return null;
        }
    }
}
