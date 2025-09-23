using Vecxy.UI;

namespace Vecxy.Sandbox;

internal static class Program
{
    public static void Main(string[] args)
    {
        var app = new UIApp();
        
        var window = app.CreateWindow("E:\\Projects\\vecxy\\Vecxy.Sandbox\\UI\\MainWindow.xml");
    }
}