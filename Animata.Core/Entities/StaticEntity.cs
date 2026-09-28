using Animata.Core.Bodies;

namespace Animata.Core.Entities;

/// <summary>Encja bez mózgu (kulka, słupek, płyta, podłoga).</summary>
public class StaticEntity : Entity
{
    public StaticEntity(Body aBody) : base(aBody)
    {
    }
}
