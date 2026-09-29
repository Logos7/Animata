using Animata.Core.Bodies;
using Animata.Core.Brains;
using Animata.Core.Training;

namespace Animata.Core.Entities;

/// <summary>
/// Encja z mózgiem. Mózg steruje ciałem przez aktuatory.
/// Stwór definiuje się sam: klasa buduje plan ciała (konstruktor), zmysły i napędy (<see cref="Equip"/>), ma ustawienia
/// (<see cref="SettingAttribute"/>), gotowe mózgi (<see cref="BrainPresets"/>), rig nauki (<see cref="TrainingRig"/>) i opis.
/// Żeby był dostępny w Studiu i w zapisie świata, wystarczy dopisać go do <see cref="WorldObjects.EntityTypes"/>.
/// </summary>
public abstract class ActiveEntity : Entity
{
    protected ActiveEntity(Body aBody, Brain? aBrain = null) : base(aBody)
    {
        Brain = aBrain;
        if (aBrain is not null)
            aBrain.Body = aBody;
    }

    public Brain? Brain { get; }

    /// <summary>
    /// Wyposaża ciało w zmysły i napędy (sloty) i odświeża węzły ciała w mózgu. Woła się raz, po konstruktorze —
    /// goły konstruktor daje samo ciało (np. do testów fizyki z własnym napędem).
    /// </summary>
    public virtual void Equip() => Brain?.SyncBody();

    /// <summary>Gotowe mózgi dla tego ciała; pierwszy dostaje nowy stwór wstawiony w Studiu.</summary>
    public virtual IReadOnlyList<BrainPreset> BrainPresets => [];

    /// <summary>Warunki nauki tego stwora; null — ogólny rig z rejestru (<see cref="SeekRigs.For"/>).</summary>
    public virtual SeekRig? TrainingRig => null;

    /// <summary>Jednolinijkowy opis ciała do UI.</summary>
    public virtual string Describe() => GetType().Name;
}
