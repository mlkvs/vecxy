using System.Runtime.InteropServices;

namespace Vecxy.CLI;

public unsafe partial class NativeWindows
{
    [LibraryImport("Vecxy.Native.Windows.dll", EntryPoint = "OpenNativeWindow", StringMarshalling = StringMarshalling.Utf16)]
    private static partial void OpenNativeWindow(string id, string title);


    public static void OpenWindow(string id, string title)
    {
        OpenNativeWindow(id, title);
    }
}

public static class Program
{
    public static void Main(string[] args)
    {
        Console.WriteLine("Запуск окна из DLL...");

        var windowThread = new Thread(() =>
        {
            try
            {
                NativeWindows.OpenWindow("test", "1111");
            }
            catch (DllNotFoundException)
            {
                Console.WriteLine("Ошибка: Не удалось найти NativeWindow.dll!");
            }
        });

        windowThread.SetApartmentState(ApartmentState.STA);
        windowThread.Start();

        Console.WriteLine("asdasd");
    }
}