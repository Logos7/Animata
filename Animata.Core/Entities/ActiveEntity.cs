using Animata.Core.Bodies;
using Animata.Core.Brains;

namespace Animata.Core.Entities;

/// <summary>Encja z mózgiem. Mózg steruje ciałem przez aktuatory.</summary>
public abstract class ActiveEntity : Entity
{
    protected ActiveEntity(Body aBody, Brain? aBrain = null) : base(aBody)
    {
        Brain = aBrain;
    }

    public Brain? Brain { get; }
}
