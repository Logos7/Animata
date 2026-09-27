using Animata.Core.Entities;
using Animata.Core.Worlds;

namespace Animata.Core.Brains;

public readonly record struct BrainContext(ActiveEntity Owner, World World, float Delta);
