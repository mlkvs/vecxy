using System.Numerics;
using System.Reflection;
using System.Runtime.Loader;
using System.Text.Json;
using Vecxy.Prototypes;
using Vecxy.Scene;

if (args.Length != 2) throw new ArgumentException("Usage: Vecxy.ComponentCatalog <game.dll> <catalog.json>");
var assemblyPath = Path.GetFullPath(args[0]);
var directory = Path.GetDirectoryName(assemblyPath)!;
AssemblyLoadContext.Default.Resolving += (_, name) =>
{
    var path = Path.Combine(directory, name.Name + ".dll");
    return File.Exists(path) ? AssemblyLoadContext.Default.LoadFromAssemblyPath(path) : null;
};
var queue = new Queue<Assembly>();
queue.Enqueue(AssemblyLoadContext.Default.LoadFromAssemblyPath(assemblyPath));
var seen = new HashSet<string>(StringComparer.Ordinal);
var components = new List<object>();
while (queue.TryDequeue(out var assembly))
{
    if (!seen.Add(assembly.FullName!)) continue;
    foreach (var reference in assembly.GetReferencedAssemblies())
        if (File.Exists(Path.Combine(directory, reference.Name + ".dll"))) queue.Enqueue(Assembly.Load(reference));
    foreach (var type in assembly.GetTypes().Where(t => !t.IsAbstract && typeof(AComponent).IsAssignableFrom(t)))
    {
        var component = type.GetCustomAttribute<EditorComponentAttribute>();
        if (component is null) continue;
        var factory = type.GetNestedType("Prototype", BindingFlags.Public);
        if (factory is null || !typeof(IPrototype).IsAssignableFrom(factory))
            throw new InvalidDataException($"{type.FullName}: EditorComponent requires a public nested Prototype.");
        var optionsType = factory.GetNestedType("Options", BindingFlags.Public)
            ?? throw new InvalidDataException($"{factory.FullName}: public Options type is missing.");
        var defaults = Activator.CreateInstance(optionsType)!;
        var fields = new List<object>();
        foreach (var property in optionsType.GetProperties().OrderBy(p => p.GetCustomAttribute<EditorFieldAttribute>()?.Order ?? 0).ThenBy(p => p.Name))
        {
            var field = property.GetCustomAttribute<EditorFieldAttribute>();
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
                ["key"] = JsonNamingPolicy.CamelCase.ConvertName(property.Name), ["label"] = field.Name,
                ["description"] = field.Description, ["kind"] = fieldType, ["default"] = value,
                ["min"] = double.IsNaN(field.Min) ? null : field.Min,
                ["max"] = double.IsNaN(field.Max) ? null : field.Max, ["unit"] = field.Unit,
                ["choices"] = kind.IsEnum ? Enum.GetNames(kind) : []
            });
        }
        components.Add(new { type = type.FullName, label = component.Name, category = component.Category,
            description = component.Description, key = component.Key.Length > 0 ? component.Key : type.Name, fields });
    }
}
var json = JsonSerializer.Serialize(new { format = 1, assembly = Path.GetFileName(assemblyPath), components }, new JsonSerializerOptions { WriteIndented = true });
var output = Path.GetFullPath(args[1]);
Directory.CreateDirectory(Path.GetDirectoryName(output)!);
if (!File.Exists(output) || File.ReadAllText(output) != json + "\n") File.WriteAllText(output, json + "\n");
Console.WriteLine($"Vecxy components: {components.Count} -> {output}");
