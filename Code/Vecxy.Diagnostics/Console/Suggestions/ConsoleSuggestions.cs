namespace Vecxy.Diagnostics.Console;

public sealed class ConsoleSuggestionProvider(
    IConsoleRegistry registry) : IConsoleSuggestionProvider
{
    private static readonly string[] Aliases = ["clear", "help", "objects"];

    public IReadOnlyList<ConsoleSuggestion> GetSuggestions(
        string input,
        int cursorPosition)
    {
        input ??= string.Empty;
        cursorPosition = Math.Clamp(cursorPosition, 0, input.Length);
        var text = input[..cursorPosition];
        var trimmed = text.TrimStart();
        var suggestions = new List<ConsoleSuggestion>();

        if (TryBuildValueSuggestions(text, out var valueSuggestions))
            return valueSuggestions;

        if (TryBuildMethodSuggestions(text, out var methodSuggestions))
            return methodSuggestions;

        var dotIndex = text.LastIndexOf('.');
        if (dotIndex >= 0)
        {
            var objectName = text[..dotIndex].Trim();
            var memberPrefix = text[(dotIndex + 1)..].Trim();
            var descriptor = registry.Objects.FirstOrDefault(
                item => string.Equals(item.Name, objectName, StringComparison.OrdinalIgnoreCase));
            if (descriptor is null)
                return suggestions;

            foreach (var member in descriptor.Members.Values
                         .OrderBy(item => item.Name, StringComparer.OrdinalIgnoreCase))
            {
                if (!member.Name.StartsWith(memberPrefix, StringComparison.OrdinalIgnoreCase))
                    continue;

                suggestions.Add(
                    new ConsoleSuggestion(
                        member.IsExecutable ? ConsoleSuggestionKind.Method : ConsoleSuggestionKind.Member,
                        member.DisplaySignature,
                        member.IsExecutable
                            ? $"{member.Name}("
                            : member.Name,
                        member.Description,
                        dotIndex + 1,
                        memberPrefix.Length));
            }

            return suggestions;
        }

        foreach (var descriptor in registry.Objects.OrderBy(item => item.Name, StringComparer.OrdinalIgnoreCase))
        {
            if (!descriptor.Name.StartsWith(trimmed, StringComparison.OrdinalIgnoreCase))
                continue;

            suggestions.Add(
                new ConsoleSuggestion(
                    ConsoleSuggestionKind.Object,
                    descriptor.Name,
                    descriptor.Name,
                    descriptor.Description,
                    0,
                    trimmed.Length));
        }

        foreach (var alias in Aliases.Where(alias => alias.StartsWith(trimmed, StringComparison.OrdinalIgnoreCase)))
        {
            suggestions.Add(
                new ConsoleSuggestion(
                    ConsoleSuggestionKind.Alias,
                    alias,
                    alias,
                    "Built-in console command",
                    0,
                    trimmed.Length));
        }

        return suggestions;
    }

    private bool TryBuildValueSuggestions(
        string text,
        out IReadOnlyList<ConsoleSuggestion> suggestions)
    {
        var operators = new[] { "+=", "-=", "*=", "/=", "=" };
        var operatorIndex = -1;
        var operatorText = string.Empty;

        foreach (var candidate in operators)
        {
            var index = text.LastIndexOf(candidate, StringComparison.Ordinal);
            if (index <= operatorIndex)
                continue;

            operatorIndex = index;
            operatorText = candidate;
        }

        if (operatorIndex < 0)
        {
            suggestions = [];
            return false;
        }

        var left = text[..operatorIndex].Trim();
        var valuePrefix = text[(operatorIndex + operatorText.Length)..].Trim();
        var memberResolution = ResolveMember(left);
        if (memberResolution is null)
        {
            suggestions = [];
            return false;
        }

        var member = memberResolution.Value.Member;
        var replaceStart = operatorIndex + operatorText.Length;
        while (replaceStart < text.Length && char.IsWhiteSpace(text[replaceStart]))
            replaceStart++;

        suggestions = BuildValueSuggestions(
            member.ValueType,
            replaceStart,
            valuePrefix.Length);
        return true;
    }

    private bool TryBuildMethodSuggestions(
        string text,
        out IReadOnlyList<ConsoleSuggestion> suggestions)
    {
        var openParenIndex = text.LastIndexOf('(');
        if (openParenIndex < 0)
        {
            suggestions = [];
            return false;
        }

        var closeParenIndex = text.LastIndexOf(')');
        if (closeParenIndex > openParenIndex)
        {
            suggestions = [];
            return false;
        }

        var target = text[..openParenIndex].Trim();
        var memberResolution = ResolveMember(target);
        if (memberResolution is null || !memberResolution.Value.Member.IsExecutable)
        {
            suggestions = [];
            return false;
        }

        var descriptor = memberResolution.Value.Member;
        var currentArguments = text[(openParenIndex + 1)..]
            .Split(',', StringSplitOptions.TrimEntries);
        var argumentIndex = currentArguments.Length - 1;

        var highlighted = string.Join(
            ", ",
            descriptor.Parameters.Select((parameter, index) =>
            {
                var current = $"{ConsoleTypeDisplay.Format(parameter.ParameterType)} {parameter.Name}";
                if (parameter.IsOptional)
                {
                    var defaultValue = parameter.DefaultValue is DBNull
                        ? "null"
                        : ConsoleTypeDisplay.FormatValue(parameter.DefaultValue);
                    current += $" = {defaultValue}";
                }

                return index == argumentIndex
                    ? $"[{current}]"
                    : current;
            }));

        suggestions =
        [
            new ConsoleSuggestion(
                ConsoleSuggestionKind.Signature,
                $"{descriptor.Name}({highlighted})",
                string.Empty,
                descriptor.Description,
                openParenIndex + 1,
                0)
        ];
        return true;
    }

    private IReadOnlyList<ConsoleSuggestion> BuildValueSuggestions(
        Type targetType,
        int replaceStart,
        int replaceLength)
    {
        var effectiveType = Nullable.GetUnderlyingType(targetType) ?? targetType;
        var suggestions = new List<ConsoleSuggestion>();

        if (effectiveType == typeof(bool))
        {
            suggestions.Add(CreateValueSuggestion("true"));
            suggestions.Add(CreateValueSuggestion("false"));
        }
        else if (effectiveType.IsEnum)
        {
            suggestions.AddRange(
                Enum.GetNames(effectiveType)
                    .Select(value => CreateValueSuggestion(value)));
        }
        else if (effectiveType == typeof(string))
        {
            suggestions.Add(CreateValueSuggestion("\"text\"", "Expected value: string"));
        }
        else if (effectiveType == typeof(float) ||
                 effectiveType == typeof(double) ||
                 effectiveType == typeof(decimal) ||
                 effectiveType == typeof(int) ||
                 effectiveType == typeof(long) ||
                 effectiveType == typeof(short))
        {
            suggestions.Add(CreateValueSuggestion("0", $"Expected value: {ConsoleTypeDisplay.Format(targetType)}"));
        }

        if (Nullable.GetUnderlyingType(targetType) is not null)
            suggestions.Add(CreateValueSuggestion("null"));

        return suggestions;

        ConsoleSuggestion CreateValueSuggestion(string value, string? description = null) =>
            new(
                ConsoleSuggestionKind.Value,
                description is null ? value : $"{value} ({description})",
                value,
                description,
                replaceStart,
                replaceLength);
    }

    private (IConsoleObjectDescriptor Descriptor, IConsoleMemberDescriptor Member)? ResolveMember(string input)
    {
        var dotIndex = input.LastIndexOf('.');
        if (dotIndex < 0)
            return null;

        var objectName = input[..dotIndex].Trim();
        var memberName = input[(dotIndex + 1)..].Trim();
        if (objectName.Length == 0 || memberName.Length == 0)
            return null;

        var descriptor = registry.Objects.FirstOrDefault(
            item => string.Equals(item.Name, objectName, StringComparison.OrdinalIgnoreCase));
        if (descriptor is null || !descriptor.TryGetMember(memberName, out var member))
            return null;

        return (descriptor, member);
    }
}
