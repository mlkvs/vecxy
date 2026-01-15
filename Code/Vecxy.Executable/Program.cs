using System.Reflection;
using System.Runtime.Loader;
using Vecxy.Engine;

internal class Program
{
    public class GameLoadContext(string gameDllPath, bool isCollectible = false) : AssemblyLoadContext(name: "GameLoadContext", isCollectible: isCollectible)
    {
        private readonly AssemblyDependencyResolver _resolver = new(gameDllPath);

        private static readonly string[] EXTERNAL_MODULES = ["OpenTK", "ImGui.NET", "Newtonsoft.Json"];

        protected override Assembly? Load(AssemblyName assemblyName)
        {
            var name = assemblyName.Name!;

            if (name.StartsWith("System.")) return null;
            if (name.StartsWith("Microsoft.")) return null;
            if (name.StartsWith("Vecxy.")) return null;
            if (EXTERNAL_MODULES.Contains(assemblyName.Name)) return null;

            var assemblyPath = _resolver.ResolveAssemblyToPath(assemblyName);

            return assemblyPath != null ? LoadFromAssemblyPath(assemblyPath) : null;
        }

        protected override IntPtr LoadUnmanagedDll(string unmanagedDllName)
        {
            var libraryPath = _resolver.ResolveUnmanagedDllToPath(unmanagedDllName);

            return libraryPath != null ? LoadUnmanagedDllFromPath(libraryPath) : IntPtr.Zero;
        }
    }

    public static void Main()
    {
        Console.WriteLine("Release");

        var gameDllPath = "./FlappyBird.Game.dll";
        var absolutePath = Path.GetFullPath(gameDllPath);

        if (!File.Exists(absolutePath))
        {
            throw new FileNotFoundException($"Game DLL not found at: {absolutePath}");
        }

        var loadContext = new GameLoadContext(absolutePath, isCollectible: true);

        var gameAssembly = loadContext.LoadFromAssemblyPath(absolutePath);

        var types = gameAssembly.GetTypes();

        Type? appType = null;

        for (int index = 0; index < types.Length; index++)
        {
            var type = types[index];

            if (type.IsClass == false)
            {
                continue;
            }

            if (type.BaseType == null)
            {
                continue;
            }

            if (type.BaseType == typeof(Application))
            {
                appType = type;
                break;
            }
        }

        if (appType == null)
        {
            throw new Exception($"In build {Path.GetFileName(gameDllPath)} not found class, with implementation Application!");
        }

        var application = (Application)Activator.CreateInstance(appType)!;

        var engine = new Engine(application);

        engine.Run();
    }
}