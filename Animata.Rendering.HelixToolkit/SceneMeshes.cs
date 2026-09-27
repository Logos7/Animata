using System.Numerics;
using Animata.Core.Bodies;
using Animata.Core.Entities;
using Animata.Core.WorldObjects;
using HelixToolkit.Avalonia.SharpDX;
using HelixToolkit.Geometry;
using HelixToolkit.Maths;
using HelixToolkit.SharpDX;

namespace Animata.Rendering.HelixToolkit;

/// <summary>Siatki i materiały encji. Wymiary tutaj muszą się zgadzać z hitboxami w <see cref="ScenePicker"/>.</summary>
internal static class SceneMeshes
{
    public const float WheelRadius = 0.13f;
    private const float WheelWidth = 0.1f;

    /// <summary>Nadwozie autka wisi na kołach: dół na wysokości promienia koła.</summary>
    public const float CarClearance = WheelRadius;

    public static bool CanDraw(Entity aEntity) => aEntity is CylinderCreature or TargetBall or CarCreature or Obstacle or Floor or Slab;

    public static global::HelixToolkit.SharpDX.MeshGeometry3D CreateGeometry(Entity aEntity)
    {
        var mesh = new MeshBuilder();
        switch (aEntity)
        {
            case CylinderCreature cylinder:
                mesh.AddCylinder(Vector3.Zero, new Vector3(0, 0, cylinder.Height), cylinder.Radius, 48, true, true);
                mesh.AddSphere(new Vector3(cylinder.Radius * 1.08f, 0, cylinder.Height * 0.5f), cylinder.Radius * 0.22f);
                break;
            case TargetBall target:
                mesh.AddSphere(new Vector3(0, 0, target.Radius), target.Radius);
                break;
            case CarCreature car:
                AddCar(mesh, car);
                break;
            case Obstacle obstacle:
                mesh.AddCylinder(Vector3.Zero, new Vector3(0, 0, obstacle.Height), obstacle.Radius, 32, true, true);
                break;
            case Floor floor:
                mesh.AddBox(Vector3.Zero, floor.Size.X, floor.Size.Y, floor.Size.Z);
                break;
            case Slab slab:
                mesh.AddBox(new Vector3(0, 0, slab.Size.Z / 2), slab.Size.X, slab.Size.Y, slab.Size.Z);
                break;
        }

        return mesh.ToMeshGeometry3D();
    }

    /// <summary>Wymiary wpływające na siatkę — zmiana oznacza, że trzeba ją przebudować.</summary>
    public static Vector3 ShapeOf(Entity aEntity) => aEntity switch
    {
        CylinderCreature cylinder => new Vector3(cylinder.Radius, cylinder.Height, 0),
        TargetBall target => new Vector3(target.Radius, 0, 0),
        CarCreature car => new Vector3(car.Length, car.Width, car.Height),
        Obstacle obstacle => new Vector3(obstacle.Radius, obstacle.Height, 0),
        Floor floor => floor.Size,
        Slab slab => slab.Size,
        _ => Vector3.Zero
    };

    public static PhongMaterial MaterialFor(Entity aEntity, bool aSelected) => aEntity switch
    {
        CylinderCreature creature => BodyMaterial(creature.Color, aSelected),
        CarCreature car => BodyMaterial(car.Color, aSelected),
        Obstacle => aSelected ? Material(0.8f, 0.8f, 0.85f) : Material(0.5f, 0.5f, 0.55f),
        TargetBall => aSelected ? Material(1f, 1f, 0.4f) : Material(1f, 0.67f, 0.2f),
        ArticulatedCreature body => BodyMaterial(body.Color, aSelected),
        Floor => Material(0.20f, 0.27f, 0.33f),
        Slab => aSelected ? Material(0.62f, 0.70f, 0.78f) : Material(0.38f, 0.45f, 0.52f),
        _ => Material(0.5f, 0.5f, 0.5f)
    };

    public static PhongMaterial Material(float aRed, float aGreen, float aBlue) => new()
    {
        DiffuseColor = new Color4(aRed, aGreen, aBlue, 1),
        AmbientColor = new Color4(aRed * 0.35f, aGreen * 0.35f, aBlue * 0.35f, 1),
        SpecularColor = new Color4(0.25f, 0.25f, 0.25f, 1)
    };

    private static PhongMaterial BodyMaterial(Vector3 aColor, bool aSelected)
    {
        var color = aSelected ? Vector3.Lerp(aColor, Vector3.One, 0.45f) : aColor;
        return Material(color.X, color.Y, color.Z);
    }

    /// <summary>
    /// Siatka części ciała w jej własnym układzie (środek w 0, długość wzdłuż X) — do stworów z części.
    /// Pierwsza część (głowa) dostaje kropkę na przodzie, żeby było widać, dokąd patrzy.
    /// </summary>
    public static global::HelixToolkit.SharpDX.MeshGeometry3D CreatePartGeometry(PartPlan aPart, bool aHead)
    {
        var mesh = new MeshBuilder();
        switch (aPart.Shape)
        {
            case PartShape.Capsule:
            {
                var half = aPart.Size.Y / 2;
                var radius = aPart.Size.X;
                mesh.AddCylinder(new Vector3(-half, 0, 0), new Vector3(half, 0, 0), radius, 20, false, false);
                mesh.AddSphere(new Vector3(-half, 0, 0), radius);
                mesh.AddSphere(new Vector3(half, 0, 0), radius);
                if (aHead)
                    mesh.AddSphere(new Vector3(half + radius * 0.8f, 0, radius * 0.45f), radius * 0.28f);
                break;
            }
            case PartShape.Sphere:
                mesh.AddSphere(Vector3.Zero, aPart.Size.X);
                break;
            case PartShape.Cylinder:
                mesh.AddCylinder(new Vector3(0, -aPart.Size.Y / 2, 0), new Vector3(0, aPart.Size.Y / 2, 0), aPart.Size.X, 32, true, true);
                break;
            default:
                mesh.AddBox(Vector3.Zero, aPart.Size.X, aPart.Size.Y, aPart.Size.Z);
                break;
        }
        return mesh.ToMeshGeometry3D();
    }

    /// <summary>Autko: nadwozie nad kołami, 4 koła, kulka na nosie pokazująca przód (+X).</summary>
    private static void AddCar(MeshBuilder aMesh, CarCreature aCar)
    {
        aMesh.AddBox(new Vector3(0, 0, CarClearance + aCar.Height / 2), aCar.Length, aCar.Width, aCar.Height);
        foreach (var x in new[] { -aCar.Length * 0.32f, aCar.Length * 0.32f })
            foreach (var side in new[] { -1f, 1f })
            {
                var inner = new Vector3(x, side * aCar.Width / 2, WheelRadius);
                aMesh.AddCylinder(inner, inner + new Vector3(0, side * WheelWidth, 0), WheelRadius, 16, true, true);
            }
        aMesh.AddSphere(new Vector3(aCar.Length / 2, 0, CarClearance + aCar.Height * 0.6f), 0.09f);
    }
}
