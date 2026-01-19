using System.Reflection;
using Vecxy.Diagnostics;

namespace Vecxy.CLI;

[AttributeUsage(AttributeTargets.Field | AttributeTargets.Property)]
public class CLIParameterAttribute : Attribute
{
    public string Name { get; set; }
    public char Alias { get; set; }
    public object Default { get; set; }
}

public interface ICLICommand
{
    public string Name { get; }
}

public abstract class CLICommandBase<TParameters> : ICLICommand
{
    public abstract string Name { get; }

    public abstract void Execute(TParameters parameters);
}

public class NotFoundArguments() : Exception("Not found program arguments");
public class NotFoundCommand(string cmd) : Exception($"Nor found command for name {cmd}");
public class NotDeriveBaseCommand(string cmd, Type type) : Exception($"Command: {cmd}, {type} does not derive from CLICommandBase<TParameters>");
public class NotDefaultValueByParameter(string cmd, string parameter) : Exception($"Command: {cmd}, missing default value for parameter: {parameter}");

public static class CLIParser
{
    public static void Parse(string[] args, ICLICommand[] commands)
    {
        if (args.Length == 0)
        {
            throw new NotFoundArguments();
        }

        var argsMap = new Dictionary<string, string>();

        for (int index = 1, count = args.Length; index < count; index += 2)
        {
            var key = args[index];

            if (index + 1 >= args.Length)
            {
                Logger.Warning($"Argument '{key}' has no value. Skipping.");
                break;
            }

            var value = args[index + 1];

            if (argsMap.TryAdd(key, value))
            {
                continue;
            }

            Logger.Warning("Command arguments duplicate key: " + key);
        }

        var commandName = args[0].ToLower();

        var command = commands.FirstOrDefault(c => c.Name.Equals(commandName));

        if (command == null)
        {
            throw new NotFoundCommand(commandName);
        }

        var type = command.GetType();

        var baseType = type;

        while (baseType != null && (!baseType.IsGenericType || baseType.GetGenericTypeDefinition() != typeof(CLICommandBase<>)))
        {
            baseType = baseType.BaseType;
        }

        if (baseType == null)
        {
            throw new NotDeriveBaseCommand(command.Name, type);
        }

        var parameterElementType = baseType.GetGenericArguments().First();

        var parameterInstance = Activator.CreateInstance(parameterElementType);

        const BindingFlags FLAGS = BindingFlags.Public | BindingFlags.Instance | BindingFlags.GetField | BindingFlags.GetProperty;

        var members = parameterElementType.GetMembers(FLAGS);

        foreach (var member in members)
        {
            var attribute = member.GetCustomAttribute<CLIParameterAttribute>();

            if (attribute == null)
            {
                continue;
            }

            if (!argsMap.TryGetValue("--" + attribute.Name, out var value) && (!argsMap.TryGetValue("-" + attribute.Alias, out value)))
            {
                if (attribute.Default == null)
                {
                    throw new NotDefaultValueByParameter(command.Name, attribute.Name);
                }
                
                value = attribute.Default.ToString();
            }

            try
            {
                var memberType = member switch
                {
                    FieldInfo f => f.FieldType,
                    PropertyInfo p => p.PropertyType,
                    _ => null
                };

                if (memberType == null)
                {
                    continue;
                }
                
                var targetType = Nullable.GetUnderlyingType(memberType) ?? memberType;

                var convertedValue = targetType.IsEnum ? 
                    Enum.Parse(targetType, value, true) : 
                    Convert.ChangeType(value, targetType);
                
                switch (member)
                {
                    case FieldInfo field:
                    {
                        field.SetValue(parameterInstance, convertedValue);
                        break;
                    }

                    case PropertyInfo property:
                    {
                        property.SetValue(parameterInstance, convertedValue);
                        break;
                    }
                }
            }
            catch (Exception ex)
            {
                Logger.Error($"Failed to set value for {member.Name}: {ex.Message}");
            }
        }

        type
            .GetMethod(nameof(CLICommandBase<>.Execute))!
            .Invoke(command, [parameterInstance]);
    }

    public static ICLICommand Command<TParameters>(string name, Action<TParameters> execute) => new TempCommand<TParameters>(name, execute);

    private class TempCommand<TParameters>(string name, Action<TParameters> execute) : CLICommandBase<TParameters>
    {
        public override string Name => name;

        public override void Execute(TParameters parameters)
        {
            execute(parameters);
        }
    }
}