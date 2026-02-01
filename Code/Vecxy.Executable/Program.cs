using System.Reflection;
using System.Runtime.Loader;
using System.Diagnostics;
using Vecxy.Engine;

internal class Program
{
    public class GameLoadContext(string gameDllPath) : AssemblyLoadContext(name: "GameLoadContext", isCollectible: true)
    {
        private readonly AssemblyDependencyResolver _resolver = new(gameDllPath);
        private static readonly string[] EXTERNAL_MODULES = ["OpenTK", "ImGui.NET", "Newtonsoft.Json", "Silk.NET"];

        protected override Assembly? Load(AssemblyName assemblyName)
        {
            var name = assemblyName.Name!;
            if (name.StartsWith("System.") || name.StartsWith("Microsoft.") ||
                name.StartsWith("Vecxy.") || EXTERNAL_MODULES.Any(m => name.Contains(m)))
                return null;

            var assemblyPath = _resolver.ResolveAssemblyToPath(assemblyName);
            return assemblyPath != null ? LoadFromAssemblyPath(assemblyPath) : null;
        }
    }

    public static void Main(string[] args)
    {
        try
        {
            // Берем имя запущенного EXE (FlappyBird)
            string currentExeName = Path.GetFileNameWithoutExtension(Process.GetCurrentProcess().MainModule?.FileName);

            // Ищем FlappyBird.Game.dll
            string gameDllPath = Path.Combine(AppContext.BaseDirectory, $"FlappyBird.Game.dll");

            if (!File.Exists(gameDllPath))
                throw new FileNotFoundException($"Game project DLL not found: {gameDllPath}");

            Console.WriteLine($"[Runner] Loading Game: {Path.GetFileName(gameDllPath)}");
            var loadContext = new GameLoadContext(gameDllPath);
            var gameAssembly = loadContext.LoadFromAssemblyPath(gameDllPath);

            var appType = gameAssembly.GetTypes().FirstOrDefault(t =>
                t.IsClass && !t.IsAbstract && IsSubclassOfAppLayer(t));

            if (appType == null)
                throw new Exception($"No class inheriting from AppLayer found in {gameDllPath}");

            var application = (AppLayer)Activator.CreateInstance(appType)!;
            var engine = new Engine([application]);
            engine.Run(); 
        }
        catch (Exception ex)
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine("\n--- CRITICAL ERROR ---");
            Console.WriteLine(ex.Message);
            Console.WriteLine(ex.StackTrace);
            Console.ResetColor();
            Console.WriteLine("\nPress any key to exit...");
            Console.ReadKey();
        }
    }

    private static bool IsSubclassOfAppLayer(Type type)
    {
        var current = type.BaseType;
        while (current != null)
        {
            if (current.FullName == typeof(AppLayer).FullName) return true;
            current = current.BaseType;
        }
        return false;
    }
}