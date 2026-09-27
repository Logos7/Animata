using Animata.Core.Bodies;

namespace Animata.Core.Entities;

public abstract class Entity
{
    protected Entity(Body aBody)
    {
        Body = aBody;
    }

    /// <summary>Domyślnie nowy GUID; można nadać przy klonowaniu lub wczytywaniu świata.</summary>
    public Guid Id { get; init; } = Guid.NewGuid();
    public Body Body { get; }

    /// <summary>Nazwa do wyświetlania (listy, breadcrumb). Pusta = UI nazywa encję po typie.</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>Promień obrysu w płaszczyźnie ruchu, już przemnożony przez skalę ciała.</summary>
    public virtual float BoundingRadius => 0;

    /// <summary>Czy kolizje mogą przesuwać tę encję.</summary>
    public virtual bool IsMovable => true;

    /// <summary>Kategoria widziana przez zmysły (np. wąsy filtrują po niej).</summary>
    public virtual EntityCategory Category => EntityCategory.None;
}
