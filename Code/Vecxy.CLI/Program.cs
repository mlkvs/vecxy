using Vecxy.Native;

namespace Vecxy.CLI;


public static class Program
{
    public static void Main(string[] args)
    {
        Console.WriteLine("Запуск окна из DLL...");

        var windowThread = new Thread(() =>
        {
            try
            {
                var window = new Window(new WindowSettings
                {
                    Title = "Demo"
                });
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