using System.Numerics;
using Animata.Core.Bodies;
using Animata.Core.Physics;
using BepuPhysics;
using BepuPhysics.Collidables;

namespace Animata.Core.Entities;

/// <summary>
/// Encja bez mózgu, która w fizyce jest statyczną bryłą (podłoga, płyta, słupek, drzewo). Sama fizyki nie tworzy;
/// gdy świat ją ma, bryła powstaje z <see cref="Build"/>. Po ręcznej zmianie (przesunięcie, rozmiar) — gdy
/// <see cref="Shape"/> różni się od tego z chwili budowy — bryła jest odtwarzana przed następnym krokiem.
/// </summary>
public abstract class PhysicalStaticEntity(Body aBody) : StaticEntity(aBody), IPhysicalEntity
{
    private StaticHandle? _static;
    private object? _built;

    /// <summary>Wszystko, od czego zależy bryła (np. krotka pozycji i wymiarów) — porównywane przez Equals.</summary>
    protected abstract object Shape { get; }

    /// <summary>Dodaje bryłę do fizyki i zwraca jej uchwyt.</summary>
    protected abstract StaticHandle Build(PhysicsWorld aPhysics);

    /// <summary>Pionowy walec stojący podstawą na <paramref name="aFoot"/> (walec Bepu leży wzdłuż Y — obrót o 90° wokół X).</summary>
    protected static StaticHandle AddUprightCylinder(PhysicsWorld aPhysics, Vector3 aFoot, float aRadius, float aHeight, float? aGrip = null) =>
        aPhysics.AddStatic(new Cylinder(aRadius, aHeight),
            new RigidPose(aFoot + new Vector3(0, 0, aHeight / 2), Quaternion.CreateFromAxisAngle(Vector3.UnitX, MathF.PI / 2)), aGrip);

    bool IPhysicalEntity.IsDynamic => false;

    void IPhysicalEntity.AttachPhysics(PhysicsWorld aPhysics)
    {
        _static = Build(aPhysics);
        _built = Shape;
    }

    void IPhysicalEntity.DetachPhysics(PhysicsWorld aPhysics)
    {
        if (_static is { } handle)
            aPhysics.RemoveStatic(handle);
        _static = null;
    }

    void IPhysicalEntity.BeforePhysicsStep(PhysicsWorld aPhysics, float aDelta)
    {
        if (Equals(_built, Shape))
            return;
        ((IPhysicalEntity)this).DetachPhysics(aPhysics);
        ((IPhysicalEntity)this).AttachPhysics(aPhysics);
    }

    void IPhysicalEntity.AfterPhysicsStep(PhysicsWorld aPhysics)
    {
    }
}
