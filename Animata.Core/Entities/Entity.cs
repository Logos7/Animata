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

    /// <summary>Czy kolizje mogą przesuwać tę encję.</summary>
    public virtual bool IsMovable => true;

    /// <summary>Kategoria widziana przez zmysły (np. wąsy filtrują po niej).</summary>
    public virtual EntityCategory Category => EntityCategory.None;

    /// <summary>
    /// Nieruszalna: ani myszą, ani z panelu, ani kolizjami, ani przez <see cref="Place"/> (np. podłoga).
    /// UI jej nie przesuwa i nie usuwa, picking w 3D jej nie łapie.
    /// </summary>
    public virtual bool IsFixed => false;

    /// <summary>Ruch encji liczy silnik fizyki (Bepu), więc prosty system kolizji okręgów ją pomija.</summary>
    public virtual bool UsesPhysics => false;

    /// <summary>
    /// Stawia encję w nowym miejscu (np. przeciągnięcie myszą, początek próby w nauce). Ciało złożone z części
    /// przenosi wszystkie części i zeruje ich prędkości. Encji <see cref="IsFixed"/> nie rusza.
    /// </summary>
    public virtual void Place(Vector3 aPosition, Quaternion aRotation)
    {
        if (IsFixed)
            return;
        Body.Position = aPosition;
        Body.Rotation = aRotation;
    }
}
