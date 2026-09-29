using System.Numerics;
using Animata.Core.Bodies;

namespace Animata.Core.Entities;

public abstract class Entity
{
    protected Entity(Body aBody)
    {
        Body = aBody;
    }

    private Guid _id = Guid.NewGuid();

    /// <summary>Domyślnie nowy GUID; można nadać przy klonowaniu lub wczytywaniu świata.</summary>
    public Guid Id
    {
        get => _id;
        init => _id = value;
    }

    /// <summary>Nadaje Id zbudowanej już encji (wczytywanie świata). Tylko przed dodaniem do świata.</summary>
    internal void AssignId(Guid aId) => _id = aId;
    public Body Body { get; }

    /// <summary>Nazwa do wyświetlania (listy, breadcrumb). Pusta = UI nazywa encję po typie.</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>Promień obrysu w płaszczyźnie ruchu, już przemnożony przez skalę ciała.</summary>
    public virtual float BoundingRadius => 0;

    /// <summary>Kategoria widziana przez zmysły (np. wąsy filtrują po niej).</summary>
    public virtual EntityCategory Category => EntityCategory.None;

    /// <summary>
    /// Zablokowana (np. klocek-podłoga): nie przesuwa jej mysz, panel, <see cref="Place"/> ani przyciąganie do terenu,
    /// UI jej nie usuwa ani nie kopiuje. Da się ją zaznaczyć i zmienić we właściwościach (tam też się ją odblokowuje).
    /// </summary>
    public bool Locked { get; set; }

    /// <summary>
    /// Stawia encję w nowym miejscu (np. przeciągnięcie myszą, początek próby w nauce). Ciało złożone z części
    /// przenosi wszystkie części i zeruje ich prędkości. Encji <see cref="Locked"/> nie rusza.
    /// </summary>
    public virtual void Place(Vector3 aPosition, Quaternion aRotation)
    {
        if (Locked)
            return;
        Body.Position = aPosition;
        Body.Rotation = aRotation;
    }
}
