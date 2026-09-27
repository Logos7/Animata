namespace Animata.Core.Brains;

/// <summary>
/// Błąd mózgu: zła budowa grafu albo wyjątek w module podczas ticku.
/// Niesie Id modułu (jeśli dotyczy), żeby UI mogło wskazać winny węzeł.
/// </summary>
public sealed class BrainException : Exception
{
    public BrainException(string aMessage, Guid? aModuleId = null, Exception? aInner = null)
        : base(aMessage, aInner)
    {
        ModuleId = aModuleId;
    }

    public Guid? ModuleId { get; }
}
