using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using Vecxy.Diagnostics.Console;

namespace Vecxy.Editor;

internal sealed class SystemFileDialog : ISystemFileDialog
{
    public string? ShowSaveFileDialog(
        string title,
        string defaultFileName,
        IReadOnlyList<FileDialogFilter> filters)
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            return ShowWindowsSaveFileDialog(title, defaultFileName, filters);

        if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
            return ShowLinuxSaveFileDialog(title, defaultFileName, filters);

        throw new PlatformNotSupportedException("System save file dialog is not implemented for this platform.");
    }

    private static string? ShowWindowsSaveFileDialog(
        string title,
        string defaultFileName,
        IReadOnlyList<FileDialogFilter> filters)
    {
        var filterText = filters.Count == 0
            ? "All files (*.*)|*.*"
            : string.Join(
                "|",
                filters.Select(filter =>
                    $"{filter.Name} ({string.Join(", ", filter.Patterns)})|{string.Join(";", filter.Patterns)}"));

        var script = $$"""
        Add-Type -AssemblyName System.Windows.Forms
        $dialog = New-Object System.Windows.Forms.SaveFileDialog
        $dialog.Title = {{EscapePowerShell(title)}}
        $dialog.FileName = {{EscapePowerShell(defaultFileName)}}
        $dialog.Filter = {{EscapePowerShell(filterText)}}
        $dialog.OverwritePrompt = $true
        if ($dialog.ShowDialog() -eq [System.Windows.Forms.DialogResult]::OK) {
            [Console]::Write($dialog.FileName)
        }
        """;

        return RunDialog(
            "powershell",
            ["-NoProfile", "-STA", "-Command", script]);
    }

    private static string? ShowLinuxSaveFileDialog(
        string title,
        string defaultFileName,
        IReadOnlyList<FileDialogFilter> filters)
    {
        var fileName = Path.GetFullPath(defaultFileName);
        var patterns = filters.SelectMany(filter => filter.Patterns).ToArray();

        return RunDialog(
                   "zenity",
                   BuildZenityArguments(title, fileName, patterns)) ??
               RunDialog(
                   "kdialog",
                   BuildKDialogArguments(title, fileName, patterns)) ??
               RunDialog(
                   "qarma",
                   BuildZenityArguments(title, fileName, patterns));
    }

    private static IEnumerable<string> BuildZenityArguments(
        string title,
        string defaultFileName,
        IReadOnlyList<string> patterns)
    {
        yield return "--file-selection";
        yield return "--save";
        yield return "--confirm-overwrite";
        yield return "--title";
        yield return title;
        yield return "--filename";
        yield return defaultFileName;

        if (patterns.Count > 0)
        {
            yield return "--file-filter";
            yield return $"Supported files | {string.Join(' ', patterns)}";
        }
    }

    private static IEnumerable<string> BuildKDialogArguments(
        string title,
        string defaultFileName,
        IReadOnlyList<string> patterns)
    {
        yield return "--getsavefilename";
        yield return defaultFileName;
        yield return patterns.Count > 0
            ? string.Join(' ', patterns)
            : "*";
        yield return "--title";
        yield return title;
    }

    private static string? RunDialog(
        string fileName,
        IEnumerable<string> arguments)
    {
        try
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = fileName,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            foreach (var argument in arguments)
                startInfo.ArgumentList.Add(argument);

            using var process = Process.Start(startInfo);
            if (process is null)
                return null;

            var output = process.StandardOutput.ReadToEnd();
            process.WaitForExit();
            if (process.ExitCode != 0)
                return null;

            var value = output.Trim();
            return value.Length == 0 ? null : value;
        }
        catch
        {
            return null;
        }
    }

    private static string EscapePowerShell(string value)
    {
        return "@\"" + Environment.NewLine +
               value.Replace("\"", "\"\"", StringComparison.Ordinal) +
               Environment.NewLine +
               "\"@";
    }
}
