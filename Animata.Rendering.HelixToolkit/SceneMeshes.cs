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
    /// <summary>Encje z jedną siatką. Stwory z części (<see cref="ArticulatedCreature"/>) mają siatkę na część — <see cref="CreatePartGeometry"/>.</summary>
    public static bool CanDraw(Entity aEntity) => aEntity is Sphere or Cylinder or Box;

    public static global::HelixToolkit.SharpDX.MeshGeometry3D CreateGeometry(Entity aEntity)
    {
        var mesh = new MeshBuilder();
        switch (aEntity)
        {
            case Sphere sphere:
                mesh.AddSphere(new Vector3(0, 0, sphere.Radius), sphere.Radius);
                break;
            case Cylinder cylinder:
                mesh.AddCylinder(Vector3.Zero, new Vector3(0, 0, cylinder.Height), cylinder.Radius, 32, true, true);
                break;
            case Box box:
                mesh.AddBox(new Vector3(0, 0, box.Size.Z / 2), box.Size.X, box.Size.Y, box.Size.Z);
                break;
        }

        return mesh.ToMeshGeometry3D();
    }

    /// <summary>Wymiary wpływające na siatkę — zmiana oznacza, że trzeba ją przebudować.</summary>
    public static Vector3 ShapeOf(Entity aEntity) => aEntity switch
    {
        Sphere sphere => new Vector3(sphere.Radius, 0, 0),
        Cylinder cylinder => new Vector3(cylinder.Radius, cylinder.Height, 0),
        Box box => box.Size,
        _ => Vector3.Zero
    };

    /// <summary>Kolor bryły (zmiana — przemalować); kula ma stały kolor celu.</summary>
    public static Vector3 ColorOf(Entity aEntity) => aEntity switch
    {
        Cylinder cylinder => cylinder.Color,
        Box box => box.Color,
        _ => Vector3.Zero
    };

    public static PhongMaterial MaterialFor(Entity aEntity, bool aSelected) => aEntity switch
    {
        Sphere => aSelected ? Material(1f, 1f, 0.4f) : Material(1f, 0.67f, 0.2f),
        ArticulatedCreature body => BodyMaterial(body.Color, aSelected),
        Cylinder or Box => BodyMaterial(ColorOf(aEntity), aSelected),
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
}
