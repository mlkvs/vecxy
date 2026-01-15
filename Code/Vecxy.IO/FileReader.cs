namespace Vecxy.IO;

public static class FileReader
{
    public static string ReadToEnd(string path)
    {
        using var stream = new FileStream(path, FileMode.Open);
        using var reader = new StreamReader(stream);

        var text = reader.ReadToEnd();

        return text;
    }
}