namespace Vecxy.IO;

public static class DirectoryUtils
{
    public static DirectoryInfo GetOrCreateDirectory(string directoryPath)
    {
        var directoryInfo = !Directory.Exists(directoryPath) ? Directory.CreateDirectory(directoryPath) : new DirectoryInfo(directoryPath);

        return directoryInfo;
    }
}