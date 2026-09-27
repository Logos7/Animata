using System.Globalization;

namespace Animata.Core.Brains.Neural;

/// <summary>Skompilowane wyrażenie wraz z listą zmiennych, do których się odwołuje.</summary>
public sealed record CompiledExpression(
    Func<IReadOnlyDictionary<string, float>, float> Evaluate,
    IReadOnlySet<string> Variables);

/// <summary>
/// Parser prostych wyrażeń: + - * /, unarne +/-, nawiasy, liczby (kropka dziesiętna) i zmienne
/// ([A-Za-z_][A-Za-z0-9_]*). Zmienne rozwiązywane są ze słownika przy ewaluacji.
/// </summary>
public static class SensorExpression
{
    public static CompiledExpression Compile(string aExpression)
    {
        ArgumentNullException.ThrowIfNull(aExpression);
        var parser = new Parser(aExpression);
        var result = parser.ParseExpression();
        parser.SkipSpaces();
        if (!parser.End)
            throw new FormatException($"Unexpected character at position {parser.Position}.");
        return new CompiledExpression(result, parser.Variables);
    }

    private sealed class Parser(string aSource)
    {
        public int Position { get; private set; }
        public bool End => Position >= aSource.Length;
        public HashSet<string> Variables { get; } = [];

        public void SkipSpaces()
        {
            while (!End && char.IsWhiteSpace(aSource[Position])) Position++;
        }

        public Func<IReadOnlyDictionary<string, float>, float> ParseExpression()
        {
            var left = ParseTerm();
            while (true)
            {
                SkipSpaces();
                if (End || (aSource[Position] != '+' && aSource[Position] != '-')) return left;
                var op = aSource[Position++];
                var previous = left;
                var right = ParseTerm();
                if (op == '+')
                    left = aVars => previous(aVars) + right(aVars);
                else
                    left = aVars => previous(aVars) - right(aVars);
            }
        }

        private Func<IReadOnlyDictionary<string, float>, float> ParseTerm()
        {
            var left = ParseFactor();
            while (true)
            {
                SkipSpaces();
                if (End || (aSource[Position] != '*' && aSource[Position] != '/')) return left;
                var op = aSource[Position++];
                var previous = left;
                var right = ParseFactor();
                if (op == '*')
                    left = aVars => previous(aVars) * right(aVars);
                else
                    left = aVars => previous(aVars) / right(aVars);
            }
        }

        private Func<IReadOnlyDictionary<string, float>, float> ParseFactor()
        {
            SkipSpaces();
            if (End) throw new FormatException("Expression ended unexpectedly.");
            if (aSource[Position] == '-')
            {
                Position++;
                var inner = ParseFactor();
                return aVars => -inner(aVars);
            }
            if (aSource[Position] == '+')
            {
                Position++;
                return ParseFactor();
            }
            if (aSource[Position] == '(')
            {
                Position++;
                var inner = ParseExpression();
                SkipSpaces();
                if (End || aSource[Position++] != ')') throw new FormatException("Missing closing parenthesis.");
                return inner;
            }
            if (char.IsAsciiLetter(aSource[Position]) || aSource[Position] == '_')
            {
                var start = Position++;
                while (!End && (char.IsAsciiLetterOrDigit(aSource[Position]) || aSource[Position] == '_')) Position++;
                var name = aSource[start..Position];
                Variables.Add(name);
                return aVars => aVars.TryGetValue(name, out var value) ? value
                    : throw new InvalidOperationException($"Variable {name} is not bound.");
            }
            var numberStart = Position;
            while (!End && (char.IsAsciiDigit(aSource[Position]) || aSource[Position] == '.')) Position++;
            if (numberStart == Position || !float.TryParse(aSource[numberStart..Position],
                    NumberStyles.Float, CultureInfo.InvariantCulture, out var number) || !float.IsFinite(number))
                throw new FormatException($"Invalid number at position {numberStart}.");
            return _ => number;
        }
    }
}
