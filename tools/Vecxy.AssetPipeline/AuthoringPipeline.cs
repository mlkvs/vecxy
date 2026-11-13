using System.Numerics;
using System.Reflection;
using System.Runtime.Loader;
using System.Text.Json;
using Vecxy.Prototypes;
using Vecxy.Scene;

namespace Vecxy.AssetPipeline;

public static partial class AssetPipeline
{
    /// <summary>Finalize authoring metadata from the just-compiled project and engine dependencies.</summary>
    public static string GenerateAuthoringCatalog(string projectDirectory, string assemblyFile, string? outputFile = null)
    {
        var root = Path.GetFullPath(projectDirectory);
        var assemblyPath = Path.GetFullPath(assemblyFile);
        if (!File.Exists(assemblyPath)) throw new FileNotFoundException("Build the game before finalizing authoring metadata.", assemblyPath);
        var directory = Path.GetDirectoryName(assemblyPath)!;
        var context = new AuthoringLoadContext(assemblyPath);
        try
        {
            var queue = new Queue<Assembly>();
            queue.Enqueue(context.LoadFromAssemblyPath(assemblyPath));
            var seen = new HashSet<string>(StringComparer.Ordinal);
            var components = new List<object>();
            while (queue.TryDequeue(out var assembly))
            {
                if (!seen.Add(assembly.FullName!)) continue;
                foreach (var reference in assembly.GetReferencedAssemblies().OrderBy(a => a.FullName, StringComparer.Ordinal))
                    if (File.Exists(Path.Combine(directory, reference.Name + ".dll"))) queue.Enqueue(context.LoadFromAssemblyName(reference));
                foreach (var type in assembly.GetTypes().Where(t => !t.IsAbstract && typeof(AComponent).IsAssignableFrom(t)).OrderBy(t => t.FullName, StringComparer.Ordinal))
                {
                    var component = type.GetCustomAttribute<AuthoringAttribute>();
                    if (component is null) continue;
                    var factory = type.GetNestedType("Prototype", BindingFlags.Public);
                    if (factory is null || !typeof(IPrototype).IsAssignableFrom(factory))
                        throw new InvalidDataException($"{type.FullName}: Authoring requires a public nested Prototype.");
                    var optionsType = factory.GetNestedType("Options", BindingFlags.Public)
                        ?? throw new InvalidDataException($"{factory.FullName}: public Options type is missing.");
                    var defaults = Activator.CreateInstance(optionsType)!;
                    var fields = new List<object>();
                    foreach (var property in optionsType.GetProperties().OrderBy(p => p.GetCustomAttribute<AuthoringAttribute>()?.Order ?? 0).ThenBy(p => p.Name, StringComparer.Ordinal))
                    {
                        var field = property.GetCustomAttribute<AuthoringAttribute>();
                        if (field is null || !property.CanWrite) continue;
                        var kind = property.PropertyType;
                        string fieldType;
                        object? value = property.GetValue(defaults);
                        if (kind == typeof(Vector3)) { fieldType = "VECTOR3"; var v = (Vector3)value!; value = new[] { v.X, v.Y, v.Z }; }
                        else if (kind == typeof(bool)) fieldType = "BOOL";
                        else if (kind == typeof(string)) fieldType = "STRING";
                        else if (kind == typeof(int)) fieldType = "INT";
                        else if (kind == typeof(float) || kind == typeof(double)) fieldType = "FLOAT";
                        else if (kind.IsEnum) { fieldType = "ENUM"; value = value!.ToString(); }
                        else throw new InvalidDataException($"{optionsType.FullName}.{property.Name}: unsupported authoring field type {kind}.");
                        if (field.Default.Length > 0)
                            value = kind.IsEnum || kind == typeof(string) ? field.Default : JsonSerializer.Deserialize<object>(field.Default);
                        fields.Add(new Dictionary<string, object?>
                        {
                            ["key"] = JsonNamingPolicy.CamelCase.ConvertName(property.Name), ["label"] = field.Name.Length > 0 ? field.Name : property.Name,
                            ["description"] = field.Description, ["kind"] = fieldType, ["default"] = value,
                            ["min"] = double.IsNaN(field.Min) ? null : field.Min,
                            ["max"] = double.IsNaN(field.Max) ? null : field.Max, ["unit"] = field.Unit,
                            ["choices"] = kind.IsEnum ? Enum.GetNames(kind) : []
                        });
                    }
                    components.Add(new { type = type.FullName, label = component.Name.Length > 0 ? component.Name : type.Name, category = component.Category,
                        description = component.Description, key = component.Key.Length > 0 ? component.Key : type.Name, fields });
                }
            }
            var json = JsonSerializer.Serialize(new { format = 1, assembly = Path.GetFileName(assemblyPath), components }, new JsonSerializerOptions { WriteIndented = true }) + "\n";
            var output = outputFile is null ? Path.Combine(root, "Authoring", "vecxy.components.json") : Path.GetFullPath(outputFile, root);
            Directory.CreateDirectory(Path.GetDirectoryName(output)!);
            if (!File.Exists(output) || File.ReadAllText(output) != json)
            {
                var temporary = output + "." + Guid.NewGuid().ToString("N") + ".tmp";
                try
                {
                    File.WriteAllText(temporary, json);
                    File.Move(temporary, output, overwrite: true);
                }
                finally { File.Delete(temporary); }
            }
            return output;
        }
        finally { context.Unload(); }
    }

    // Isolate project dependencies so different games can be scanned in one process.
    // Share only the contract assemblies and framework types with the tool itself.
    private sealed class AuthoringLoadContext(string assemblyPath) : AssemblyLoadContext(isCollectible: true)
    {
        private readonly AssemblyDependencyResolver _resolver = new(assemblyPath);
        protected override Assembly? Load(AssemblyName name)
        {
            foreach (var contract in new[] { typeof(AComponent).Assembly, typeof(AuthoringAttribute).Assembly })
                if (name.Name == contract.GetName().Name) return contract;
            if (name.Name == "netstandard" || File.Exists(Path.Combine(Path.GetDirectoryName(typeof(object).Assembly.Location)!, name.Name + ".dll"))) return null;
            var path = _resolver.ResolveAssemblyToPath(name);
            path ??= Path.Combine(Path.GetDirectoryName(assemblyPath)!, name.Name + ".dll");
            return File.Exists(path) ? LoadFromAssemblyPath(path) : null;
        }
    }
}
