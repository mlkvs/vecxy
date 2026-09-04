using System.Diagnostics;
using System.Runtime.InteropServices;

namespace Vecxy.SpriteEditor;

public sealed class ProjectFolderDialog
{
    public enum UnsavedChoice { Save, Discard, Cancel }
    public string? Open()
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
            return Run("zenity", ["--file-selection", "--directory", "--title", "Select Vecxy project or Assets folder"])
                   ?? Run("kdialog", ["--getexistingdirectory", Directory.GetCurrentDirectory(), "--title", "Select Vecxy project"]);
        return null;
    }

    public string? OpenAsset(string? directory = null)
    {
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Linux)) return null;
        var args = new List<string> { "--file-selection", "--title", "Open texture or atlas", "--file-filter", "Sprite files | *.png *.atlas" };
        if (directory is not null) args.AddRange(["--filename", directory.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar]);
        return Run("zenity", args);
    }

    public string? SaveAtlas(string suggestedPath)
    {
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Linux)) return null;
        return Run("zenity", ["--file-selection", "--save", "--confirm-overwrite", "--title", "Save atlas", "--filename", suggestedPath, "--file-filter", "Vecxy atlas | *.atlas"]);
    }

    public UnsavedChoice ConfirmUnsaved(string documentName)
    {
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Linux)) return UnsavedChoice.Cancel;
        try
        {
            var info = new ProcessStartInfo("zenity") { RedirectStandardOutput = true, UseShellExecute = false };
            foreach (var argument in new[] { "--question", "--title", "Unsaved atlas", "--text", $"Save changes to {documentName}?", "--ok-label", "Save", "--cancel-label", "Cancel", "--extra-button", "Don't Save" }) info.ArgumentList.Add(argument);
            using var process = Process.Start(info);
            if (process is null) return UnsavedChoice.Cancel;
            var output = process.StandardOutput.ReadToEnd().Trim();
            process.WaitForExit();
            if (output.Equals("Don't Save", StringComparison.Ordinal)) return UnsavedChoice.Discard;
            return process.ExitCode == 0 ? UnsavedChoice.Save : UnsavedChoice.Cancel;
        }
        catch { return UnsavedChoice.Cancel; }
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
