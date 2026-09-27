using Animata.Core.Bodies;

namespace Animata.Core.Entities;

/// <summary>Encja bez mózgu. Kolizje jej nie przesuwają (można ją przesunąć ręcznie).</summary>
public class StaticEntity : Entity
{
    public StaticEntity(Body aBody) : base(aBody)
    {
    }

    public override bool IsMovable => false;
}
