namespace Vecxy.Diagnostics.Console;

[ConsoleObject("console", Description = "Built-in debug console commands")]
public sealed class ConsoleCommands(
    IDebugConsole console,
    IConsoleRegistry registry)
{
    [ConsoleMember("clear", Description = "Clears the console buffer", Access = ConsoleAccess.Execute)]
    public string Clear()
    {
        console.Clear();
        return "Console cleared.";
    }

    [ConsoleMember("help", Description = "Shows help for an object", Access = ConsoleAccess.Execute)]
    public string Help(string? objectName = null)
    {
        if (string.IsNullOrWhiteSpace(objectName))
        {
            return string.Join(
                Environment.NewLine,
                [
                    "Commands:",
                    "  console.objects()",
                    "  console.help(\"object\")",
                    "  player.health",
                    "  player.health = 120",
                    "  player.move(1, 2, 3)"
                ]);
        }

        var descriptor = registry.Objects.FirstOrDefault(
            item => string.Equals(item.Name, objectName, StringComparison.OrdinalIgnoreCase));

        if (descriptor is null)
            return $"Console object \"{objectName}\" was not found.";

        var lines = new List<string>
        {
            $"{descriptor.Name} : {descriptor.ObjectType.Name}"
        };

        if (!string.IsNullOrWhiteSpace(descriptor.Description))
            lines.Add(descriptor.Description);

        foreach (var member in descriptor.Members.Values.OrderBy(item => item.Name, StringComparer.OrdinalIgnoreCase))
        {
            var description = string.IsNullOrWhiteSpace(member.Description)
                ? string.Empty
                : $" - {member.Description}";
            lines.Add($"{member.DisplaySignature}{description}");
        }

        return string.Join(Environment.NewLine, lines);
    }

    [ConsoleMember("objects", Description = "Lists all registered console objects", Access = ConsoleAccess.Execute)]
    public string Objects()
    {
        var names = registry.Objects
            .OrderBy(item => item.Name, StringComparer.OrdinalIgnoreCase)
            .Select(item => item.Name)
            .ToArray();

        if (names.Length == 0)
            return "No console objects are registered.";

        return string.Join(Environment.NewLine, names);
    }
}
