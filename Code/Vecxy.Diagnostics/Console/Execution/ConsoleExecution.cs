using System.Globalization;
using System.Reflection;

namespace Vecxy.Diagnostics.Console;

internal static class ConsoleNameMatcher
{
    public static string? FindSuggestion(string input, IEnumerable<string> candidates)
    {
        var normalized = input.Trim();
        if (normalized.Length == 0)
            return null;

        string? best = null;
        var bestScore = int.MaxValue;

        foreach (var candidate in candidates)
        {
            if (candidate.StartsWith(normalized, StringComparison.OrdinalIgnoreCase))
                return candidate;

            var score = Levenshtein(normalized, candidate);
            if (score < bestScore)
            {
                bestScore = score;
                best = candidate;
            }
        }

        return bestScore <= Math.Max(2, normalized.Length / 2)
            ? best
            : null;
    }

    private static int Levenshtein(string left, string right)
    {
        var matrix = new int[left.Length + 1, right.Length + 1];
        for (var i = 0; i <= left.Length; i++)
            matrix[i, 0] = i;
        for (var j = 0; j <= right.Length; j++)
            matrix[0, j] = j;

        for (var i = 1; i <= left.Length; i++)
        {
            for (var j = 1; j <= right.Length; j++)
            {
                var cost = char.ToUpperInvariant(left[i - 1]) == char.ToUpperInvariant(right[j - 1]) ? 0 : 1;
                matrix[i, j] = Math.Min(
                    Math.Min(matrix[i - 1, j] + 1, matrix[i, j - 1] + 1),
                    matrix[i - 1, j - 1] + cost);
            }
        }

        return matrix[left.Length, right.Length];
    }
}

public sealed class ConsoleValueConverter : IConsoleValueConverter
{
    public bool TryConvert(
        ConsoleValueNode source,
        Type targetType,
        out object? value,
        out string? error)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(targetType);

        var nullableType = Nullable.GetUnderlyingType(targetType);
        var effectiveType = nullableType ?? targetType;

        if (source.TokenType == ConsoleTokenType.Null)
        {
            if (nullableType is not null || !effectiveType.IsValueType)
            {
                value = null;
                error = null;
                return true;
            }

            value = null;
            error = $"Cannot assign null to {ConsoleTypeDisplay.Format(targetType)}.";
            return false;
        }

        if (effectiveType == typeof(string))
        {
            if (source.TokenType == ConsoleTokenType.String)
            {
                value = source.Text;
                error = null;
                return true;
            }

            value = null;
            error = "String values must be wrapped in quotes.";
            return false;
        }

        if (effectiveType == typeof(bool))
        {
            if (source.TokenType is ConsoleTokenType.True or ConsoleTokenType.False)
            {
                value = source.TokenType == ConsoleTokenType.True;
                error = null;
                return true;
            }

            if (bool.TryParse(source.Text, out var boolValue))
            {
                value = boolValue;
                error = null;
                return true;
            }

            value = null;
            error = $"Cannot convert \"{source.Text}\" to bool.";
            return false;
        }

        if (effectiveType.IsEnum)
        {
            var enumText = source.Text.Contains('.', StringComparison.Ordinal)
                ? source.Text[(source.Text.LastIndexOf('.') + 1)..]
                : source.Text;

            if (Enum.TryParse(effectiveType, enumText, true, out value))
            {
                error = null;
                return true;
            }

            value = null;
            error = $"Cannot convert \"{source.Text}\" to {ConsoleTypeDisplay.Format(targetType)}.";
            return false;
        }

        if (TryConvertNumber(source.Text, effectiveType, out value))
        {
            error = null;
            return true;
        }

        if (effectiveType == typeof(Guid) &&
            Guid.TryParse(source.Text, out var guidValue))
        {
            value = guidValue;
            error = null;
            return true;
        }

        value = null;
        error = $"Cannot convert \"{source.Text}\" to {ConsoleTypeDisplay.Format(targetType)}.";
        return false;
    }

    private static bool TryConvertNumber(
        string source,
        Type targetType,
        out object? value)
    {
        value = null;

        if (!double.TryParse(
                source,
                NumberStyles.Float | NumberStyles.AllowLeadingSign,
                CultureInfo.InvariantCulture,
                out _))
        {
            return false;
        }

        try
        {
            value = targetType switch
            {
                _ when targetType == typeof(byte) => byte.Parse(source, NumberStyles.Integer, CultureInfo.InvariantCulture),
                _ when targetType == typeof(sbyte) => sbyte.Parse(source, NumberStyles.Integer, CultureInfo.InvariantCulture),
                _ when targetType == typeof(short) => short.Parse(source, NumberStyles.Integer, CultureInfo.InvariantCulture),
                _ when targetType == typeof(ushort) => ushort.Parse(source, NumberStyles.Integer, CultureInfo.InvariantCulture),
                _ when targetType == typeof(int) => int.Parse(source, NumberStyles.Integer, CultureInfo.InvariantCulture),
                _ when targetType == typeof(uint) => uint.Parse(source, NumberStyles.Integer, CultureInfo.InvariantCulture),
                _ when targetType == typeof(long) => long.Parse(source, NumberStyles.Integer, CultureInfo.InvariantCulture),
                _ when targetType == typeof(ulong) => ulong.Parse(source, NumberStyles.Integer, CultureInfo.InvariantCulture),
                _ when targetType == typeof(float) => float.Parse(source, NumberStyles.Float | NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture),
                _ when targetType == typeof(double) => double.Parse(source, NumberStyles.Float | NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture),
                _ when targetType == typeof(decimal) => decimal.Parse(source, NumberStyles.Float | NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture),
                _ => null
            };
            return value is not null;
        }
        catch
        {
            value = null;
            return false;
        }
    }
}

public sealed class ConsoleCommandExecutor(
    IConsoleRegistry registry,
    IConsoleValueConverter valueConverter) : IConsoleCommandExecutor
{
    private static readonly string[] AliasNames = ["clear", "help", "objects"];

    public ConsoleExecutionResult Execute(ConsoleExpression expression)
    {
        ArgumentNullException.ThrowIfNull(expression);

        try
        {
            return expression switch
            {
                ConsoleAliasExpression aliasExpression => ExecuteAlias(aliasExpression),
                ConsoleGetExpression getExpression => ExecuteGet(getExpression),
                ConsoleAssignmentExpression assignmentExpression => ExecuteAssignment(assignmentExpression),
                ConsoleInvocationExpression invocationExpression => ExecuteInvocation(invocationExpression),
                _ => new ConsoleExecutionResult(false, "Unknown console expression.", null, ConsoleLogLevel.Error)
            };
        }
        catch (TargetInvocationException exception) when (exception.InnerException is not null)
        {
            return new ConsoleExecutionResult(
                false,
                exception.InnerException.Message,
                null,
                ConsoleLogLevel.Error);
        }
        catch (Exception exception)
        {
            return new ConsoleExecutionResult(
                false,
                exception.Message,
                null,
                ConsoleLogLevel.Error);
        }
    }

    private ConsoleExecutionResult ExecuteAlias(ConsoleAliasExpression expression)
    {
        var consoleDescriptor = ResolveObject("console");
        if (!consoleDescriptor.Success)
            return consoleDescriptor.Failure!;

        var descriptor = consoleDescriptor.Value!;
        if (!descriptor.TryGetMember(expression.Alias, out var member))
        {
            var suggestion = ConsoleNameMatcher.FindSuggestion(expression.Alias, AliasNames);
            var message = suggestion is null
                ? $"Console command \"{expression.Alias}\" was not found."
                : $"Console command \"{expression.Alias}\" was not found.{Environment.NewLine}Did you mean \"{suggestion}\"?";
            return new ConsoleExecutionResult(false, message, null, ConsoleLogLevel.Error);
        }

        return InvokeMember(descriptor, member, expression.Arguments);
    }

    private ConsoleExecutionResult ExecuteGet(ConsoleGetExpression expression)
    {
        var memberResolution = ResolveMember(expression.ObjectName, expression.MemberName);
        if (!memberResolution.Success)
            return memberResolution.Failure!;

        var (descriptor, member, target) = memberResolution.Value!.Value;
        if (!member.IsReadable)
        {
            return new ConsoleExecutionResult(
                false,
                $"Cannot read \"{member.QualifiedName}\": member is write-only.",
                null,
                ConsoleLogLevel.Error);
        }

        var value = member.Read(target);
        return new ConsoleExecutionResult(
            true,
            $"{member.QualifiedName} = {ConsoleTypeDisplay.FormatValue(value)}",
            value);
    }

    private ConsoleExecutionResult ExecuteAssignment(ConsoleAssignmentExpression expression)
    {
        var memberResolution = ResolveMember(expression.ObjectName, expression.MemberName);
        if (!memberResolution.Success)
            return memberResolution.Failure!;

        var (_, member, target) = memberResolution.Value!.Value;
        if (!member.IsWritable)
        {
            return new ConsoleExecutionResult(
                false,
                $"Cannot write to \"{member.QualifiedName}\": member is read-only.",
                null,
                ConsoleLogLevel.Error);
        }

        if (!valueConverter.TryConvert(
                expression.Value,
                member.ValueType,
                out var convertedValue,
                out var error))
        {
            return new ConsoleExecutionResult(false, error!, null, ConsoleLogLevel.Error);
        }

        if (expression.Operator != ConsoleAssignmentOperator.Assign)
        {
            if (!member.IsReadable)
            {
                return new ConsoleExecutionResult(
                    false,
                    $"Cannot apply \"{FormatOperator(expression.Operator)}\" to \"{member.QualifiedName}\": current value is not readable.",
                    null,
                    ConsoleLogLevel.Error);
            }

            var currentValue = member.Read(target);
            if (!TryApplyOperation(
                    expression.Operator,
                    member.ValueType,
                    currentValue,
                    convertedValue,
                    out convertedValue,
                    out error))
            {
                return new ConsoleExecutionResult(false, error!, null, ConsoleLogLevel.Error);
            }
        }

        member.Write(target, convertedValue);
        return new ConsoleExecutionResult(
            true,
            $"{member.QualifiedName} = {ConsoleTypeDisplay.FormatValue(convertedValue)}",
            convertedValue);
    }

    private ConsoleExecutionResult ExecuteInvocation(ConsoleInvocationExpression expression)
    {
        var memberResolution = ResolveMember(expression.ObjectName, expression.MemberName);
        if (!memberResolution.Success)
            return memberResolution.Failure!;

        var (descriptor, member, _) = memberResolution.Value!.Value;
        return InvokeMember(descriptor, member, expression.Arguments);
    }

    private ConsoleExecutionResult InvokeMember(
        IConsoleObjectDescriptor descriptor,
        IConsoleMemberDescriptor member,
        IReadOnlyList<ConsoleValueNode> arguments)
    {
        if (!member.IsExecutable)
        {
            return new ConsoleExecutionResult(
                false,
                $"Member \"{member.QualifiedName}\" is not a method.",
                null,
                ConsoleLogLevel.Error);
        }

        if (!descriptor.TryResolveTarget(out var target, out var error))
            return new ConsoleExecutionResult(false, error!, null, ConsoleLogLevel.Error);

        var parameters = member.Parameters;
        var requiredParameters = parameters.Count(parameter => !parameter.IsOptional);
        if (arguments.Count < requiredParameters || arguments.Count > parameters.Count)
        {
            return new ConsoleExecutionResult(
                false,
                $"Method \"{member.QualifiedName}\" expects {parameters.Count} arguments, but {arguments.Count} were provided.",
                null,
                ConsoleLogLevel.Error);
        }

        var convertedArguments = new object?[parameters.Count];
        for (var index = 0; index < parameters.Count; index++)
        {
            if (index >= arguments.Count)
            {
                convertedArguments[index] = parameters[index].DefaultValue is DBNull
                    ? null
                    : parameters[index].DefaultValue;
                continue;
            }

            if (!valueConverter.TryConvert(
                    arguments[index],
                    parameters[index].ParameterType,
                    out var converted,
                    out error))
            {
                return new ConsoleExecutionResult(
                    false,
                    $"Argument \"{parameters[index].Name}\" expects {ConsoleTypeDisplay.Format(parameters[index].ParameterType)}, but received \"{arguments[index].Text}\".",
                    null,
                    ConsoleLogLevel.Error);
            }

            convertedArguments[index] = converted;
        }

        var value = member.Invoke(target, convertedArguments);
        var message = member.ValueType == typeof(void)
            ? $"{member.QualifiedName} executed."
            : $"{member.QualifiedName} => {ConsoleTypeDisplay.FormatValue(value)}";
        return new ConsoleExecutionResult(true, message, value);
    }

    private (bool Success, IConsoleObjectDescriptor? Value, ConsoleExecutionResult? Failure) ResolveObject(string objectName)
    {
        var descriptor = registry.Objects.FirstOrDefault(
            current => string.Equals(current.Name, objectName, StringComparison.OrdinalIgnoreCase));

        if (descriptor is not null)
            return (true, descriptor, null);

        var suggestion = ConsoleNameMatcher.FindSuggestion(objectName, registry.Objects.Select(item => item.Name));
        var message = suggestion is null
            ? $"Console object \"{objectName}\" was not found."
            : $"Console object \"{objectName}\" was not found.{Environment.NewLine}Did you mean \"{suggestion}\"?";
        return (false, null, new ConsoleExecutionResult(false, message, null, ConsoleLogLevel.Error));
    }

    private (bool Success, (IConsoleObjectDescriptor Descriptor, IConsoleMemberDescriptor Member, object? Target)? Value, ConsoleExecutionResult? Failure)
        ResolveMember(
            string objectName,
            string memberName)
    {
        var objectResolution = ResolveObject(objectName);
        if (!objectResolution.Success)
            return (false, null, objectResolution.Failure);

        var descriptor = objectResolution.Value!;
        if (!descriptor.TryGetMember(memberName, out var member))
        {
            var suggestion = ConsoleNameMatcher.FindSuggestion(memberName, descriptor.Members.Keys);
            var message = suggestion is null
                ? $"Member \"{memberName}\" was not found on \"{descriptor.Name}\"."
                : $"Member \"{memberName}\" was not found on \"{descriptor.Name}\".{Environment.NewLine}Did you mean \"{suggestion}\"?";
            return (false, null, new ConsoleExecutionResult(false, message, null, ConsoleLogLevel.Error));
        }

        if (!descriptor.TryResolveTarget(out var target, out var error))
            return (false, null, new ConsoleExecutionResult(false, error!, null, ConsoleLogLevel.Error));

        return (true, (descriptor, member, target), null);
    }

    private static string FormatOperator(ConsoleAssignmentOperator value) =>
        value switch
        {
            ConsoleAssignmentOperator.Assign => "=",
            ConsoleAssignmentOperator.AddAssign => "+=",
            ConsoleAssignmentOperator.SubtractAssign => "-=",
            ConsoleAssignmentOperator.MultiplyAssign => "*=",
            ConsoleAssignmentOperator.DivideAssign => "/=",
            _ => "="
        };

    private static bool TryApplyOperation(
        ConsoleAssignmentOperator operation,
        Type targetType,
        object? left,
        object? right,
        out object? result,
        out string? error)
    {
        result = null;
        error = null;

        if (operation == ConsoleAssignmentOperator.AddAssign &&
            targetType == typeof(string))
        {
            result = $"{left}{right}";
            return true;
        }

        var effectiveType = Nullable.GetUnderlyingType(targetType) ?? targetType;
        if (!TryConvertToDecimal(left, effectiveType, out var leftDecimal) ||
            !TryConvertToDecimal(right, effectiveType, out var rightDecimal))
        {
            error = $"Operator \"{FormatOperator(operation)}\" is not supported for {ConsoleTypeDisplay.Format(targetType)}.";
            return false;
        }

        if (operation == ConsoleAssignmentOperator.DivideAssign &&
            rightDecimal == decimal.Zero)
        {
            error = "Division by zero is not allowed.";
            return false;
        }

        var computed = operation switch
        {
            ConsoleAssignmentOperator.AddAssign => leftDecimal + rightDecimal,
            ConsoleAssignmentOperator.SubtractAssign => leftDecimal - rightDecimal,
            ConsoleAssignmentOperator.MultiplyAssign => leftDecimal * rightDecimal,
            ConsoleAssignmentOperator.DivideAssign => leftDecimal / rightDecimal,
            _ => rightDecimal
        };

        try
        {
            result = effectiveType switch
            {
                _ when effectiveType == typeof(float) => (float)computed,
                _ when effectiveType == typeof(double) => (double)computed,
                _ when effectiveType == typeof(decimal) => computed,
                _ => Convert.ChangeType(computed, effectiveType, CultureInfo.InvariantCulture)
            };
            return true;
        }
        catch
        {
            error = $"Cannot apply \"{FormatOperator(operation)}\" to {ConsoleTypeDisplay.Format(targetType)}.";
            result = null;
            return false;
        }
    }

    private static bool TryConvertToDecimal(object? value, Type targetType, out decimal result)
    {
        try
        {
            result = targetType == typeof(float) || targetType == typeof(double)
                ? Convert.ToDecimal(value, CultureInfo.InvariantCulture)
                : Convert.ToDecimal(value, CultureInfo.InvariantCulture);
            return true;
        }
        catch
        {
            result = decimal.Zero;
            return false;
        }
    }
}
