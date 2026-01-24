namespace Vecxy.Engine;

public struct AppInfo
{
    public string Name { get; internal set; }
    public string Version { get; internal set; }
    public string Author { get; internal set; }
}

public static class App
{
    public static AppInfo Info { get; internal set; }
    public static int TargetFrameRate { get; set; } = 60;
}