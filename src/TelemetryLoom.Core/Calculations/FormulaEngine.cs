using System.Globalization;
using TelemetryLoom.Contracts.Sensors;

namespace TelemetryLoom.Core.Calculations;

public abstract record FormulaExpression;
public sealed record ScalarExpression(double Value) : FormulaExpression;
public sealed record AliasExpression(string Key) : FormulaExpression;
public sealed record UnaryExpression(FormulaExpression Operand) : FormulaExpression;
public sealed record BinaryExpression(char Operator, FormulaExpression Left, FormulaExpression Right) : FormulaExpression;
public readonly record struct FormulaType(QuantityKind Quantity, UnitCode Unit)
{
    public bool IsScalar => Quantity == QuantityKind.Dimensionless && Unit == UnitCode.None;
}
public readonly record struct FormulaValue(double Value, FormulaType Type);

public sealed class FormulaException(string message, int? position = null) : Exception(
    position is null ? message : $"{message} at position {position}.")
{
    public int? Position { get; } = position;
}

public static class FormulaEngine
{
    private static readonly FormulaType ScalarType = new(QuantityKind.Dimensionless, UnitCode.None);

    public static FormulaExpression Parse(string formula)
    {
        if (string.IsNullOrWhiteSpace(formula))
        {
            throw new FormulaException("Formula is required", 0);
        }

        return new Parser(formula).Parse();
    }

    public static IReadOnlyList<string> GetDependencies(FormulaExpression expression)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var ordered = new List<string>();
        Visit(expression);
        return ordered;

        void Visit(FormulaExpression node)
        {
            switch (node)
            {
                case AliasExpression alias when seen.Add(alias.Key):
                    ordered.Add(alias.Key);
                    break;
                case UnaryExpression unary:
                    Visit(unary.Operand);
                    break;
                case BinaryExpression binary:
                    Visit(binary.Left);
                    Visit(binary.Right);
                    break;
            }
        }
    }

    public static FormulaType Infer(
        FormulaExpression expression,
        Func<string, FormulaType?> resolveAlias) => expression switch
    {
        ScalarExpression => ScalarType,
        AliasExpression alias => resolveAlias(alias.Key)
            ?? throw new FormulaException($"Unknown sensor alias '{alias.Key}'"),
        UnaryExpression unary => Infer(unary.Operand, resolveAlias),
        BinaryExpression binary => CombineTypes(
            binary.Operator,
            Infer(binary.Left, resolveAlias),
            Infer(binary.Right, resolveAlias)),
        _ => throw new FormulaException("Unsupported expression")
    };

    public static FormulaValue Evaluate(
        FormulaExpression expression,
        Func<string, FormulaValue> resolveAlias) => expression switch
    {
        ScalarExpression scalar => new(scalar.Value, ScalarType),
        AliasExpression alias => resolveAlias(alias.Key),
        UnaryExpression unary => Negate(Evaluate(unary.Operand, resolveAlias)),
        BinaryExpression binary => Calculate(
            binary.Operator,
            Evaluate(binary.Left, resolveAlias),
            Evaluate(binary.Right, resolveAlias)),
        _ => throw new FormulaException("Unsupported expression")
    };

    private static FormulaValue Negate(FormulaValue value) => new(-value.Value, value.Type);

    private static FormulaValue Calculate(char operation, FormulaValue left, FormulaValue right)
    {
        var type = CombineTypes(operation, left.Type, right.Type);
        if (operation == '/' && right.Value == 0)
        {
            throw new FormulaException("Division by zero");
        }

        var value = operation switch
        {
            '+' => left.Value + right.Value,
            '-' => left.Value - right.Value,
            '*' => left.Value * right.Value,
            '/' => left.Value / right.Value,
            _ => throw new FormulaException($"Unsupported operator '{operation}'")
        };
        if (!double.IsFinite(value))
        {
            throw new FormulaException("Calculation produced a non-finite value");
        }

        return new FormulaValue(value, type);
    }

    private static FormulaType CombineTypes(char operation, FormulaType left, FormulaType right) => operation switch
    {
        '+' or '-' => AddOrSubtract(operation, left, right),
        '*' => Multiply(left, right),
        '/' => Divide(left, right),
        _ => throw new FormulaException($"Unsupported operator '{operation}'")
    };

    private static FormulaType AddOrSubtract(char operation, FormulaType left, FormulaType right)
    {
        if (left.IsScalar && right.IsScalar)
        {
            return ScalarType;
        }

        if (left.IsScalar || right.IsScalar)
        {
            throw new FormulaException("A scalar cannot be added to or subtracted from a physical value");
        }

        if (IsTemperature(left) || IsTemperature(right))
        {
            return TemperatureResult(operation, left, right);
        }

        if (left != right)
        {
            throw new FormulaException("Addition and subtraction require identical units");
        }

        return left;
    }

    private static FormulaType TemperatureResult(char operation, FormulaType left, FormulaType right)
    {
        var family = TemperatureFamily(left.Unit);
        if (family is null || family != TemperatureFamily(right.Unit))
        {
            throw new FormulaException("Temperature operations require matching absolute and delta unit families");
        }

        var leftAbsolute = left.Quantity == QuantityKind.Temperature;
        var rightAbsolute = right.Quantity == QuantityKind.Temperature;
        if (operation == '+' && leftAbsolute && rightAbsolute)
        {
            throw new FormulaException("Two absolute temperatures cannot be added");
        }
        if (operation == '-' && !leftAbsolute && rightAbsolute)
        {
            throw new FormulaException("An absolute temperature cannot be subtracted from a temperature delta");
        }

        if (operation == '-' && leftAbsolute && rightAbsolute)
        {
            return family == 'C'
                ? new FormulaType(QuantityKind.TemperatureDelta, UnitCode.DeltaCelsius)
                : new FormulaType(QuantityKind.TemperatureDelta, UnitCode.DeltaFahrenheit);
        }

        if (leftAbsolute || rightAbsolute)
        {
            return family == 'C'
                ? new FormulaType(QuantityKind.Temperature, UnitCode.Celsius)
                : new FormulaType(QuantityKind.Temperature, UnitCode.Fahrenheit);
        }

        return left;
    }

    private static FormulaType Multiply(FormulaType left, FormulaType right)
    {
        if (left.IsScalar) return right;
        if (right.IsScalar) return left;
        throw new FormulaException("Multiplication of two physical values is not supported");
    }

    private static FormulaType Divide(FormulaType left, FormulaType right)
    {
        if (right.IsScalar) return left;
        throw new FormulaException("Division by a physical value is not supported");
    }

    private static bool IsTemperature(FormulaType type) =>
        type.Quantity is QuantityKind.Temperature or QuantityKind.TemperatureDelta;

    private static char? TemperatureFamily(UnitCode unit) => unit switch
    {
        UnitCode.Celsius or UnitCode.DeltaCelsius => 'C',
        UnitCode.Fahrenheit or UnitCode.DeltaFahrenheit => 'F',
        _ => null
    };

    private enum TokenKind { End, Number, Alias, Plus, Minus, Star, Slash, LeftParenthesis, RightParenthesis }
    private readonly record struct Token(TokenKind Kind, string Text, int Position);

    private sealed class Parser(string text)
    {
        private int _offset;
        private Token _current;

        public FormulaExpression Parse()
        {
            _current = NextToken();
            var expression = ParseAdditive();
            if (_current.Kind != TokenKind.End)
            {
                throw new FormulaException($"Unexpected token '{_current.Text}'", _current.Position);
            }
            return expression;
        }

        private FormulaExpression ParseAdditive()
        {
            var expression = ParseMultiplicative();
            while (_current.Kind is TokenKind.Plus or TokenKind.Minus)
            {
                var operation = _current.Text[0];
                Advance();
                expression = new BinaryExpression(operation, expression, ParseMultiplicative());
            }
            return expression;
        }

        private FormulaExpression ParseMultiplicative()
        {
            var expression = ParseUnary();
            while (_current.Kind is TokenKind.Star or TokenKind.Slash)
            {
                var operation = _current.Text[0];
                Advance();
                expression = new BinaryExpression(operation, expression, ParseUnary());
            }
            return expression;
        }

        private FormulaExpression ParseUnary()
        {
            if (_current.Kind != TokenKind.Minus) return ParsePrimary();
            Advance();
            return new UnaryExpression(ParseUnary());
        }

        private FormulaExpression ParsePrimary()
        {
            if (_current.Kind == TokenKind.Number)
            {
                var token = _current;
                Advance();
                return new ScalarExpression(double.Parse(token.Text, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture));
            }
            if (_current.Kind == TokenKind.Alias)
            {
                var key = _current.Text;
                Advance();
                return new AliasExpression(key);
            }
            if (_current.Kind == TokenKind.LeftParenthesis)
            {
                Advance();
                var expression = ParseAdditive();
                if (_current.Kind != TokenKind.RightParenthesis)
                {
                    throw new FormulaException("Expected ')'", _current.Position);
                }
                Advance();
                return expression;
            }
            throw new FormulaException("Expected a number, alias, unary '-', or '('", _current.Position);
        }

        private void Advance() => _current = NextToken();

        private Token NextToken()
        {
            while (_offset < text.Length && char.IsWhiteSpace(text[_offset])) _offset++;
            if (_offset == text.Length) return new Token(TokenKind.End, string.Empty, _offset);

            var start = _offset;
            var character = text[_offset++];
            var punctuation = character switch
            {
                '+' => TokenKind.Plus, '-' => TokenKind.Minus, '*' => TokenKind.Star,
                '/' => TokenKind.Slash, '(' => TokenKind.LeftParenthesis, ')' => TokenKind.RightParenthesis,
                _ => (TokenKind?)null
            };
            if (punctuation is not null) return new Token(punctuation.Value, character.ToString(), start);

            if (char.IsDigit(character))
            {
                while (_offset < text.Length && char.IsDigit(text[_offset])) _offset++;
                if (_offset < text.Length && text[_offset] == '.')
                {
                    _offset++;
                    if (_offset == text.Length || !char.IsDigit(text[_offset]))
                        throw new FormulaException("A decimal point must be followed by a digit", _offset - 1);
                    while (_offset < text.Length && char.IsDigit(text[_offset])) _offset++;
                }
                return new Token(TokenKind.Number, text[start.._offset], start);
            }

            if (character is >= 'a' and <= 'z')
            {
                while (_offset < text.Length &&
                       (text[_offset] is >= 'a' and <= 'z' or >= '0' and <= '9' or '.' or '_')) _offset++;
                var alias = text[start.._offset];
                if (alias.EndsWith('.') || alias.EndsWith('_') || alias.Contains("..") || alias.Contains("__") || alias.Contains("._") || alias.Contains("_."))
                    throw new FormulaException("Invalid alias token", start);
                return new Token(TokenKind.Alias, alias, start);
            }

            throw new FormulaException($"Unexpected character '{character}'", start);
        }
    }
}
