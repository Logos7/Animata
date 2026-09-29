namespace Animata.Core.Entities;

/// <summary>
/// Czym encja jest dla zmysłów. Flagi, żeby sensor mógł reagować na kilka kategorii naraz
/// (np. wąsy czują przeszkody i inne stwory, ale nie jedzenie/cel).
/// </summary>
[Flags]
public enum EntityCategory
{
    None = 0,
    Creature = 1,
    Obstacle = 2,
    Target = 4,
    /// <summary>Podłoże (podłoga). Zmysły go nie widzą, chyba że o to poproszą.</summary>
    Ground = 8
}
