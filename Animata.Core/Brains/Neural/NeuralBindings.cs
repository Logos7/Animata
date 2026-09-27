namespace Animata.Core.Brains.Neural;

/// <summary>
/// Jedno wejście sieci: wyrażenie nad portami wejściowymi modułu, np. "Found * DirectionY".
/// Poprawność nazw zmiennych sprawdza moduł przy kompilacji grafu.
/// </summary>
public sealed class NeuralInput
{
    private CompiledExpression _compiled;
    private string _expression;

    public NeuralInput(string aExpression = "0")
    {
        _compiled = SensorExpression.Compile(aExpression);
        _expression = aExpression;
    }

    /// <summary>Ustawienie kompiluje wyrażenie; przy błędzie składni rzuca FormatException i nic nie zmienia.</summary>
    public string Expression
    {
        get => _expression;
        set
        {
            _compiled = SensorExpression.Compile(value);
            _expression = value;
        }
    }

    public IReadOnlySet<string> Variables => _compiled.Variables;

    public float Evaluate(IReadOnlyDictionary<string, float> aVariables) => _compiled.Evaluate(aVariables);
}

/// <summary>Jedno wyjście sieci: port modułu o wartości signal * Scale + Offset.</summary>
public sealed record NeuralOutput(string Port, float Scale = 1, float Offset = 0);
