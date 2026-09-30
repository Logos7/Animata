using System.Numerics;
using Animata.Core.Bodies;
using Animata.Core.Entities;
using Animata.Core.Physics;
using BepuPhysics;

namespace Animata.Core.WorldObjects;

/// <summary>
/// Cylinder: pionowy walec stojący na Body.Position. W fizyce statyczny walec, przesunięty albo zmieniony odtwarza się
/// przed następnym krokiem. Wąsy go widzą (kategoria <see cref="EntityCategory.Obstacle"/>). <see cref="Grip"/> &gt; 0 —
/// tarcie chwytne (np. pień do wspinania): kontakt z cylindrem ma takie tarcie niezależnie od tarcia ciała, więc wąż
/// owinięty i ściśnięty wokół niego się trzyma, a toczony zwój wkręca się w górę jak śruba.
/// </summary>
public sealed class Cylinder() : PhysicalStaticEntity(new Body())
{
    public static readonly Vector3 DefaultColor = new(0.5f, 0.5f, 0.55f);

    [Setting("Promień", Unit = "m", Min = 0.05, Max = 5)]
    public float Radius { get; set; } = 0.5f;

    [Setting("Wysokość bryły", Unit = "m", Min = 0.05, Max = 30)]
    public float Height { get; set; } = 0.8f;

    /// <summary>Tarcie chwytne kontaktu z cylindrem; 0 — zwykłe tarcie (mniejsze z obu ciał).</summary>
    [Setting("Tarcie chwytne", Min = 0, Max = 5, Tip = "Kontakt z cylindrem ma takie tarcie niezależnie od tarcia ciała (np. pień do wspinania węża). 0 — zwykłe tarcie.")]
    public float Grip { get; set; }

    [Setting("Kolor", Color = true)]
    public Vector3 Color { get; set; } = DefaultColor;

    public override float BoundingRadius => Radius;

    public override EntityCategory Category => EntityCategory.Obstacle;

    protected override object Shape => (Body.Position, Radius, Height, Grip);

    protected override StaticHandle Build(PhysicsWorld aPhysics) =>
        AddUprightCylinder(aPhysics, Body.Position, MathF.Max(0.01f, Radius), MathF.Max(0.01f, Height), Grip > 0 ? Grip : null);
}
