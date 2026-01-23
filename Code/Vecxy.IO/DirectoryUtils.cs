using Vecxy.Diagnostics;

namespace Vecxy.IO;

public static class DirectoryUtils
{
    public static DirectoryInfo GetOrCreateDirectory(string directoryPath)
    {
        var directoryInfo = !Directory.Exists(directoryPath) ? Directory.CreateDirectory(directoryPath) : new DirectoryInfo(directoryPath);

        return directoryInfo;
    }

    public static void CopyDirectory(string sourcePath, string destinationPath)
    {
        if (!Directory.Exists(sourcePath))
        {
            Logger.Error($"Source directory '{sourcePath}' does not exist for copying.");
            return;
        }

        Directory.CreateDirectory(destinationPath);

        foreach (var file in Directory.GetFiles(sourcePath))
        {
            var destFileName = Path.Combine(destinationPath, Path.GetFileName(file));
            try
            {
                File.Copy(file, destFileName, true);
                Logger.Info($"Copied file: {Path.GetFileName(file)}");
            }
            catch (Exception ex)
            {
                Logger.Error($"Failed to copy file '{Path.GetFileName(file)}': {ex.Message}");
            }
        }

        foreach (var directory in Directory.GetDirectories(sourcePath))
        {
            var dirName = Path.GetFileName(directory);
            var destDirName = Path.Combine(destinationPath, dirName);

            CopyDirectory(directory, destDirName);
        }

        Logger.Info($"Directory processed: {sourcePath}");
    }

    public static void RenameAllNamesAndContent(string sourceDir, string keyTarget, string replaceValue)
    {
        if (string.IsNullOrEmpty(replaceValue))
        {
            return;
        }
        ;

        var allFiles = Directory.GetFiles(sourceDir, "*.*", SearchOption.AllDirectories);

        foreach (var file in allFiles)
        {
            var content = File.ReadAllText(file);

            if (!content.Contains(keyTarget))
            {
                continue;
            }

            var newContent = content.Replace(keyTarget, replaceValue);

            File.WriteAllText(file, newContent);
            Logger.Info($"Updated content in: {Path.GetFileName(file)}");
        }

        var allEntries = Directory
            .GetFileSystemEntries(sourceDir, "*.*", SearchOption.AllDirectories)
            .OrderByDescending(x => x.Length);

        foreach (var entry in allEntries)
        {
            var fileName = Path.GetFileName(entry);

            if (!fileName.Contains(keyTarget))
            {
                continue;
            }

            var newName = fileName.Replace(keyTarget, replaceValue);
            var directory = Path.GetDirectoryName(entry);
            var newFullPath = Path.Combine(directory, newName);

            if (File.Exists(entry))
            {
                File.Move(entry, newFullPath);
                Logger.Info($"Renamed file: {fileName} -> {newName}");
            }
            else if (Directory.Exists(entry))
            {
                Directory.Move(entry, newFullPath);
                Logger.Info($"Renamed folder: {fileName} -> {newName}");
            }
        }
    }
}