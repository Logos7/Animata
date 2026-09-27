using Animata.Core.Bodies;
using Animata.Core.Brains;

namespace Animata.Core.Entities;

/// <summary>
/// Encja z mózgiem. Mózg steruje ciałem przez aktuatory;
/// <see cref="Update"/> to miejsce na procesy samego ciała (fizyka, metabolizm), niezależne od mózgu.
/// </summary>
public abstract class ActiveEntity : Entity
{
    protected ActiveEntity(Body aBody, Brain? aBrain = null) : base(aBody)
    {
        Brain = aBrain;
    }

    public Brain? Brain { get; }

    public virtual void Update(float aDelta)
    {
    }
}
