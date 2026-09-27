using System.Numerics;
using Animata.Core.Sensors;
using Animata.Core.Worlds;
using HelixToolkit.Avalonia.SharpDX;
using HelixToolkit.Geometry;
using HelixToolkit.SharpDX;

namespace Animata.Rendering.HelixToolkit;

/// <summary>
/// Wąsy (<see cref="RaySensor"/>) rysowane z ostatniego odczytu: blade — wolne, czerwone — trafione (ucięte w miejscu trafienia).
/// Siatka sensora jest przebudowywana tylko wtedy, gdy zmieniła się poza ciała albo odległości trafień.
/// </summary>
internal sealed class WhiskerRenderer(Viewport3DX aViewport)
{
    private const float Lift = 0.2f;
    private const float FreeRadius = 0.012f;
    private const float HitRadius = 0.025f;

    /// <summary>Dwa modele na sensor: promienie wolne i trafione, plus stan, z którego zbudowano siatki.</summary>
    private sealed class RayModels(int aRays)
    {
        public MeshGeometryModel3D Free { get; } = new() { Material = SceneMeshes.Material(0.75f, 0.75f, 0.55f) };
        public MeshGeometryModel3D Hit { get; } = new() { Material = SceneMeshes.Material(1f, 0.3f, 0.25f) };
        public bool FreeShown { get; set; }
        public bool HitShown { get; set; }
        public bool Built { get; set; }
        public Vector3 Position { get; set; }
        public Quaternion Rotation { get; set; }
        public float Skin { get; set; }
        public float[] Distances { get; } = new float[aRays];
    }

    private readonly Dictionary<Guid, RayModels> _rays = [];
    private readonly HashSet<Guid> _seen = [];

    public void Sync(World aWorld)
    {
        _seen.Clear();
        foreach (var entity in aWorld.Entities)
            foreach (var sensor in entity.Body.Sensors.OfType<RaySensor>())
            {
                _seen.Add(sensor.Id);
                if (!_rays.TryGetValue(sensor.Id, out var models) || models.Distances.Length != sensor.Angles.Count)
                {
                    if (models is not null)
                        Hide(models);
                    models = new RayModels(sensor.Angles.Count);
                    _rays[sensor.Id] = models;
                }

                var body = entity.Body;
                var skin = entity.BoundingRadius;
                if (models.Built && models.Position == body.Position && models.Rotation == body.Rotation &&
                    models.Skin == skin && sensor.LastDistances.SequenceEqual(models.Distances))
                    continue;

                Rebuild(models, sensor, body.Position, body.Rotation, skin);
            }

        foreach (var id in _rays.Keys.Where(aId => !_seen.Contains(aId)).ToArray())
        {
            Hide(_rays[id]);
            _rays.Remove(id);
        }
    }

    private void Rebuild(RayModels aModels, RaySensor aSensor, Vector3 aPosition, Quaternion aRotation, float aSkin)
    {
        var free = new MeshBuilder();
        var hit = new MeshBuilder();
        var freeCount = 0;
        var hitCount = 0;
        var heading = Vector3.Transform(Vector3.UnitX, aRotation);
        var yaw = MathF.Atan2(heading.Y, heading.X);
        var center = aPosition + new Vector3(0, 0, Lift);
        for (var ray = 0; ray < aSensor.Angles.Count; ray++)
        {
            var angle = yaw + aSensor.Angles[ray];
            var direction = new Vector3(MathF.Cos(angle), MathF.Sin(angle), 0);
            var start = center + direction * aSkin;
            var distance = aSensor.LastDistances[ray];
            var isHit = distance < aSensor.Range - 1e-4f;
            var end = start + direction * MathF.Max(distance, 0.02f);
            (isHit ? hit : free).AddCylinder(start, end, isHit ? HitRadius : FreeRadius, 6, false, false);
            if (isHit) hitCount++; else freeCount++;
            aModels.Distances[ray] = distance;
        }

        aModels.FreeShown = Show(aModels.Free, aModels.FreeShown, freeCount > 0 ? free : null);
        aModels.HitShown = Show(aModels.Hit, aModels.HitShown, hitCount > 0 ? hit : null);
        aModels.Position = aPosition;
        aModels.Rotation = aRotation;
        aModels.Skin = aSkin;
        aModels.Built = true;
    }

    private void Hide(RayModels aModels)
    {
        aModels.FreeShown = Show(aModels.Free, aModels.FreeShown, null);
        aModels.HitShown = Show(aModels.Hit, aModels.HitShown, null);
    }

    /// <summary>Pusty model nie trafia do sceny (nie wiadomo, jak Helix zniesie pustą siatkę).</summary>
    private bool Show(MeshGeometryModel3D aModel, bool aShown, MeshBuilder? aMesh)
    {
        if (aMesh is null)
        {
            if (aShown)
                aViewport.Items.Remove(aModel);
            return false;
        }

        aModel.Geometry = aMesh.ToMeshGeometry3D();
        if (!aShown)
            aViewport.Items.Add(aModel);
        return true;
    }
}
