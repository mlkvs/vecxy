using System.Globalization;
using System.Text;

namespace Vecxy.Diagnostics.Console;

public enum ConsoleTokenType
{
    Identifier,
    Number,
    String,
    True,
    False,
    Null,
    Dot,
    Comma,
    OpenParen,
    CloseParen,
    Assign,
    PlusAssign,
    MinusAssign,
    MultiplyAssign,
    DivideAssign,
    End
}

public sealed record ConsoleToken(
    ConsoleTokenType Type,
    string Text,
    int Start,
    int Length);

public abstract record ConsoleExpression;

public sealed record ConsoleAliasExpression(
    string Alias,
    IReadOnlyList<ConsoleValueNode> Arguments) : ConsoleExpression;

public sealed record ConsoleGetExpression(
    string ObjectName,
    string MemberName) : ConsoleExpression;

public sealed record ConsoleAssignmentExpression(
    string ObjectName,
    string MemberName,
    ConsoleAssignmentOperator Operator,
    ConsoleValueNode Value) : ConsoleExpression;

public sealed record ConsoleInvocationExpression(
    string ObjectName,
    string MemberName,
    IReadOnlyList<ConsoleValueNode> Arguments) : ConsoleExpression;

public sealed record ConsoleValueNode(
    string Text,
    ConsoleTokenType TokenType);

public sealed class ConsoleTokenizer
{
    public IReadOnlyList<ConsoleToken> Tokenize(string input)
    {
        ArgumentNullException.ThrowIfNull(input);

        var tokens = new List<ConsoleToken>();
        var index = 0;

        while (index < input.Length)
        {
            var current = input[index];
            if (char.IsWhiteSpace(current))
            {
                index++;
                continue;
            }

            var start = index;
            switch (current)
            {
                case '.':
                    tokens.Add(new ConsoleToken(ConsoleTokenType.Dot, ".", start, 1));
                    index++;
                    continue;
                case ',':
                    tokens.Add(new ConsoleToken(ConsoleTokenType.Comma, ",", start, 1));
                    index++;
                    continue;
                case '(':
                    tokens.Add(new ConsoleToken(ConsoleTokenType.OpenParen, "(", start, 1));
                    index++;
                    continue;
                case ')':
                    tokens.Add(new ConsoleToken(ConsoleTokenType.CloseParen, ")", start, 1));
                    index++;
                    continue;
                case '=':
                    tokens.Add(new ConsoleToken(ConsoleTokenType.Assign, "=", start, 1));
                    index++;
                    continue;
                case '+':
                    if (Peek(input, index + 1) == '=')
                    {
                        tokens.Add(new ConsoleToken(ConsoleTokenType.PlusAssign, "+=", start, 2));
                        index += 2;
                        continue;
                    }

                    break;
                case '-':
                    if (Peek(input, index + 1) == '=')
                    {
                        tokens.Add(new ConsoleToken(ConsoleTokenType.MinusAssign, "-=", start, 2));
                        index += 2;
                        continue;
                    }

                    if (char.IsDigit(Peek(input, index + 1)))
                    {
                        tokens.Add(ReadNumber(input, ref index));
                        continue;
                    }

                    break;
                case '*':
                    if (Peek(input, index + 1) == '=')
                    {
                        tokens.Add(new ConsoleToken(ConsoleTokenType.MultiplyAssign, "*=", start, 2));
                        index += 2;
                        continue;
                    }

                    break;
                case '/':
                    if (Peek(input, index + 1) == '=')
                    {
                        tokens.Add(new ConsoleToken(ConsoleTokenType.DivideAssign, "/=", start, 2));
                        index += 2;
                        continue;
                    }

                    break;
                case '"':
                    tokens.Add(ReadString(input, ref index));
                    continue;
            }

            if (char.IsDigit(current))
            {
                tokens.Add(ReadNumber(input, ref index));
                continue;
            }

            if (IsIdentifierStart(current))
            {
                tokens.Add(ReadIdentifier(input, ref index));
                continue;
            }

            throw new InvalidOperationException(
                $"Unexpected character '{current}' at position {start}.");
        }

        tokens.Add(new ConsoleToken(ConsoleTokenType.End, string.Empty, input.Length, 0));
        return tokens;
    }

    private static ConsoleToken ReadIdentifier(string input, ref int index)
    {
        var start = index;
        index++;

        while (index < input.Length && IsIdentifierPart(input[index]))
            index++;

        var text = input[start..index];
        var type = text switch
        {
            "true" => ConsoleTokenType.True,
            "false" => ConsoleTokenType.False,
            "null" => ConsoleTokenType.Null,
            _ => ConsoleTokenType.Identifier
        };

        return new ConsoleToken(type, text, start, index - start);
    }

    private static ConsoleToken ReadNumber(string input, ref int index)
    {
        var start = index;
        if (input[index] == '-')
            index++;

        while (index < input.Length && char.IsDigit(input[index]))
            index++;

        if (index < input.Length && input[index] == '.')
        {
            index++;
            while (index < input.Length && char.IsDigit(input[index]))
                index++;
        }

        if (index < input.Length &&
            (input[index] == 'e' || input[index] == 'E'))
        {
            index++;
            if (index < input.Length &&
                (input[index] == '+' || input[index] == '-'))
            {
                index++;
            }

            while (index < input.Length && char.IsDigit(input[index]))
                index++;
        }

        return new ConsoleToken(
            ConsoleTokenType.Number,
            input[start..index],
            start,
            index - start);
    }

    private static ConsoleToken ReadString(string input, ref int index)
    {
        var start = index;
        index++;
        var builder = new StringBuilder();

        while (index < input.Length)
        {
            var current = input[index++];
            if (current == '"')
            {
                return new ConsoleToken(
                    ConsoleTokenType.String,
                    builder.ToString(),
                    start,
                    index - start);
            }

            if (current == '\\')
            {
                if (index >= input.Length)
                    throw new InvalidOperationException("String literal is not terminated.");

                current = input[index++];
                builder.Append(current switch
                {
                    '\\' => '\\',
                    '"' => '"',
                    'n' => '\n',
                    'r' => '\r',
                    't' => '\t',
                    _ => current
                });
                continue;
            }

            builder.Append(current);
        }

        throw new InvalidOperationException("String literal is not terminated.");
    }

    private static bool IsIdentifierStart(char value) =>
        char.IsLetter(value) || value is '_' or '$';

    private static bool IsIdentifierPart(char value) =>
        char.IsLetterOrDigit(value) || value is '_' or '$';

    private static char Peek(string input, int index) =>
        index >= 0 && index < input.Length ? input[index] : '\0';
}

public sealed class ConsoleCommandParser(ConsoleTokenizer tokenizer) : IConsoleCommandParser
{
    private IReadOnlyList<ConsoleToken> _tokens = [];
    private int _index;

    public ConsoleParseResult Parse(string input)
    {
        if (string.IsNullOrWhiteSpace(input))
            return new ConsoleParseResult(false, null, "Command is empty.");

        try
        {
            _tokens = tokenizer.Tokenize(input);
            _index = 0;
            var expression = ParseExpression();
            Expect(ConsoleTokenType.End, "Unexpected trailing input.");
            return new ConsoleParseResult(true, expression, null);
        }
        catch (Exception exception)
        {
            return new ConsoleParseResult(false, null, exception.Message);
        }
    }

    private ConsoleExpression ParseExpression()
    {
        var first = Expect(ConsoleTokenType.Identifier, "Expected command or object name.");
        if (Match(ConsoleTokenType.Dot))
        {
            var member = Expect(ConsoleTokenType.Identifier, "Expected member name after '.'.").Text;
            if (Match(ConsoleTokenType.OpenParen))
                return new ConsoleInvocationExpression(first.Text, member, ParseArguments());

            if (TryParseAssignment(out var assignmentOperator))
            {
                var value = ParseValue();
                return new ConsoleAssignmentExpression(first.Text, member, assignmentOperator, value);
            }

            return new ConsoleGetExpression(first.Text, member);
        }

        if (Match(ConsoleTokenType.OpenParen))
            return new ConsoleAliasExpression(first.Text, ParseArguments());

        return new ConsoleAliasExpression(first.Text, []);
    }

    private IReadOnlyList<ConsoleValueNode> ParseArguments()
    {
        var arguments = new List<ConsoleValueNode>();
        if (Match(ConsoleTokenType.CloseParen))
            return arguments;

        do
        {
            arguments.Add(ParseValue());
        }
        while (Match(ConsoleTokenType.Comma));

        Expect(ConsoleTokenType.CloseParen, "Expected ')' after arguments.");
        return arguments;
    }

    private ConsoleValueNode ParseValue()
    {
        var token = Current;
        switch (token.Type)
        {
            case ConsoleTokenType.String:
            case ConsoleTokenType.Number:
            case ConsoleTokenType.True:
            case ConsoleTokenType.False:
            case ConsoleTokenType.Null:
            case ConsoleTokenType.Identifier:
                _index++;
                return new ConsoleValueNode(token.Text, token.Type);
            default:
                throw new InvalidOperationException("Expected a value.");
        }
    }

    private bool TryParseAssignment(out ConsoleAssignmentOperator assignmentOperator)
    {
        if (Current.Type is not (
                ConsoleTokenType.Assign or
                ConsoleTokenType.PlusAssign or
                ConsoleTokenType.MinusAssign or
                ConsoleTokenType.MultiplyAssign or
                ConsoleTokenType.DivideAssign))
        {
            assignmentOperator = ConsoleAssignmentOperator.Assign;
            return false;
        }

        assignmentOperator = Current.Type switch
        {
            ConsoleTokenType.Assign => ConsoleAssignmentOperator.Assign,
            ConsoleTokenType.PlusAssign => ConsoleAssignmentOperator.AddAssign,
            ConsoleTokenType.MinusAssign => ConsoleAssignmentOperator.SubtractAssign,
            ConsoleTokenType.MultiplyAssign => ConsoleAssignmentOperator.MultiplyAssign,
            ConsoleTokenType.DivideAssign => ConsoleAssignmentOperator.DivideAssign,
            _ => ConsoleAssignmentOperator.Assign
        };

        _index++;
        return true;
    }

    private ConsoleToken Current => _tokens[_index];

    private bool Match(ConsoleTokenType type)
    {
        if (Current.Type != type)
            return false;

        _index++;
        return true;
    }

    private ConsoleToken Expect(ConsoleTokenType type, string error)
    {
        if (Current.Type != type)
            throw new InvalidOperationException(error);

        return _tokens[_index++];
    }
}
