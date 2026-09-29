using Animata.Core.Bodies;

namespace Animata.Core.Entities;

/// <summary>Encja bez mózgu (kula, cylinder, klocek).</summary>
public class StaticEntity : Entity
{
    public StaticEntity(Body aBody) : base(aBody)
    {
    }
}
