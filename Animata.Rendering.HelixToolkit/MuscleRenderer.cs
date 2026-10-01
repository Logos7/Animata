using Animata.Core.Entities;
using Animata.Core.Worlds;
using HelixToolkit.Avalonia.SharpDX;
using HelixToolkit.Geometry;
using HelixToolkit.SharpDX;

namespace Animata.Rendering.HelixToolkit;

/// <summary>
/// Mięśnie stworów (<see cref="ArticulatedCreature.MuscleCount"/>) jako cienkie walce od przyczepu do przyczepu, w trzech
/// kolorach wg aktywacji: blade — wiotki (&lt; 0.15), różowe — napięty, czerwone — mocny skurcz (&gt; 0.5). Siatki
/// budowane co klatkę (kilkadziesiąt walców na stwora).
/// </summary>
internal sealed class MuscleRenderer(Viewport3DX aViewport)
{
    private const float Radius = 0.012f;
    private static readonly float[] Thresholds = [0.15f, 0.5f];

    private sealed class Models
    {
        public MeshGeometryModel3D[] Levels { get; } =
        [
            new() { Material = SceneMeshes.Material(0.85f, 0.75f, 0.72f) },
            new() { Material = SceneMeshes.Material(0.95f, 0.5f, 0.5f) },
            new() { Material = SceneMeshes.Material(0.9f, 0.12f, 0.12f) }
        ];

        public bool[] Shown { get; } = new bool[3];
    }

    private readonly Dictionary<Guid, Models> _models = [];
    private readonly HashSet<Guid> _seen = [];

    public void Sync(World aWorld)
    {
        _seen.Clear();
        foreach (var creature in aWorld.Entities.OfType<ArticulatedCreature>())
        {
            if (creature.MuscleCount == 0)
                continue;
            _seen.Add(creature.Id);
            if (!_models.TryGetValue(creature.Id, out var models))
                _models[creature.Id] = models = new Models();
            var meshes = new[] { new MeshBuilder(), new MeshBuilder(), new MeshBuilder() };
            var counts = new int[3];
            for (var muscle = 0; muscle < creature.MuscleCount; muscle++)
            {
                var activation = creature.MuscleActivation(muscle);
                var level = activation < Thresholds[0] ? 0 : activation < Thresholds[1] ? 1 : 2;
                var (origin, insertion) = creature.MuscleEnds(muscle);
                meshes[level].AddCylinder(origin, insertion, Radius * (1 + activation), 6, false, false);
                counts[level]++;
            }
            for (var level = 0; level < 3; level++)
                models.Shown[level] = Show(models.Levels[level], models.Shown[level], counts[level] > 0 ? meshes[level] : null);
        }

        foreach (var id in _models.Keys.Where(aId => !_seen.Contains(aId)).ToArray())
        {
            var models = _models[id];
            for (var level = 0; level < 3; level++)
                models.Shown[level] = Show(models.Levels[level], models.Shown[level], null);
            _models.Remove(id);
        }
    }

    private bool Show(MeshGeometryModel3D aModel, bool aShown, MeshBuilder? aMesh) => SceneMeshes.Show(aViewport, aModel, aShown, aMesh);
}
