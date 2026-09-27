# Animata

**A living world you can stop, open up and rewire at any moment.**

Animata is an artificial-life simulator in the making. The goal is a whole ecosystem of creatures with real bodies,
real senses and brains that learn: snakes that climb trees in full 3D, five-legged critters that work out how to walk,
and whatever else evolution and curiosity come up with. The world runs, the creatures live and learn, and you can
at any time pause it, step inside any creature, look at what it sees and thinks, change its body or its brain,
and let it run on.

> **Status: early and experimental.** Animata grows step by step. Today's creatures are simple wheeled bodies moving
> on a flat ground: the first, smallest testbed for the ideas below, not the limit of the project. APIs and formats
> change as experiments are tried, measured and kept or thrown away.

---

## The vision

- **A world, not a demo.** Many kinds of creatures sharing one space: terrain, plants, trees to climb, food to find,
  obstacles and each other. Physics grows with the creatures: from sliding discs today to articulated bodies with
  joints, limbs and full 3D movement.
- **Bodies are hardware.** A creature is built from parts: sensors (eyes, whiskers, touch, balance), actuators
  (wheels today; muscles, joints and grippers tomorrow) and the body that carries them. Any body plan should be
  expressible, whether it is a snake, a five-legged walker or something without a name.
- **Brains are software.** A brain is a graph of modules wired port to port: hand-written reflexes, neural networks,
  routers, memories, and subgraphs that contain whole graphs of their own. The brain only talks to the body through
  sensor and actuator ports, so the same brain can be moved to another body, copied, compared or evolved.
- **Learning never stops the world.** Evolution runs in the background on hidden copies of the world, and the living
  creatures pick up improvements while the simulation keeps going.
- **You are always in control.** Pause at any moment. Zoom into a creature, then into its brain, then into a subgraph
  of its brain, and see live values flowing through every wire. Edit anything: move things around, change parameters,
  rewire the brain, snapshot it and roll back. Then press play.

## What works today

- **World and bodies.** Entities on a plane with circle collisions. Two body types: a disc with differential drive and
  a car with front-wheel steering and a real turning radius.
- **Senses.** An eye that tracks a target (distance, gap, direction in the creature's own frame) and whiskers: rays
  that report how close obstacles are. A car can have any odd number of whiskers from 1 to 25, spread over 120°;
  its controller, its network and the hidden training copies all follow that number.
- **Brains as graphs.** Sensor → logic → actuator graphs with validation (unknown ports, cycles, double-driven
  actuators and bad configuration are errors, not silent zeros). Logic can be a hand-written controller, a neural
  network whose inputs are small expressions over sensor ports, a router, a constant, or a **subgraph** (composite
  pattern, any depth) with group/ungroup that keeps the wiring intact.
- **Background evolution.** A genetic algorithm trains network weights on its own thread. The scene only receives the
  *champion*, the best candidate on a fixed validation set, so creatures change rarely and only for the better.
- **Snapshots.** Any brain's parameters can be captured, compared and restored. Training writes its own snapshots and
  undo walks back through them.
- **Studio.** A desktop app you *zoom into*: double-click a creature and the window turns into its panel (senses, body,
  actuators, brain). Click the brain and you are in a live graph editor; double-click a subgraph and you go one level
  deeper. Pause, step, change speed and edit while it runs.

## The experimental approach

Animata is built as a chain of small experiments rather than from a fixed design. Each step follows the same loop:

1. **Ask a question.** *Can a network learn to avoid posts with five whiskers? Is the champion really better, or just
   lucky? Does grouping part of a brain into a subgraph change its behaviour?*
2. **Build the smallest thing that answers it**, in the core library, without UI.
3. **Measure against a baseline.** Learned behaviour is compared with a hand-written controller on the same tracks.
   For example, the car controller reaches the target on 64/64 random tracks with 1–3 posts, and a 90-parameter
   network trained from random weights matches that after about 50 generations.
4. **Use fixed validation sets.** Training tracks are random per generation (noisy); champions are judged on a
   separate, seeded set, so scores from different sessions can be compared.
5. **Write the finding down.** Numbers, weaknesses and dead ends go into the architecture notes. Known weaknesses stay
   documented instead of hidden.
6. **Keep the core testable.** Simulation, learning, snapshots and graph editing live in `Animata.Core` and are covered
   by xUnit tests. The UI only reads the model and calls its actions.

Even the fitness function is part of the experiment: each term exists because something went wrong without it.
An energy cost was added because networks learned to ram the target; a contact penalty because they learned to push
their way along the posts.

## Road ahead

Roughly in this direction, one experiment at a time:

- more senses and richer brain modules (memory, timing, learning inside a lifetime, not only across generations);
- articulated bodies: joints, limbs and muscles, starting with simple walkers;
- full 3D movement: climbing, falling, balance; terrain and trees;
- a living ecosystem: food, energy, reproduction and inheritance of body and brain;
- saving and loading whole worlds, and moving creatures between them.

## Projects

| Project | What it is |
| --- | --- |
| `Animata.Core` | Model, bodies, sensors, actuators, brains, collisions, training, snapshots, graph editing. No UI. |
| `Animata.Rendering.HelixToolkit` | 3D scene: meshes, picking, dragging, whisker rays, fly camera. |
| `Animata.Studio` | Desktop app: zoomable panels, scene, creature view, brain graph editor, themes. |
| `Animata.Tests` | xUnit tests for the core. |

## Running

Requirements: **.NET 10 SDK**. The Studio uses HelixToolkit's SharpDX renderer, so it runs on **Windows**.

```powershell
dotnet build
dotnet test Animata.Tests
dotnet run --project Animata.Studio
```

### Studio controls

| Where | Input | Action |
| --- | --- | --- |
| Everywhere | Double-click | Zoom into a creature or subgraph (cards in a creature open with a single click) |
| Everywhere | Esc · Alt+← · mouse Back | Zoom out one level |
| Scene, creature, graph | Space | Pause / resume the world |
| Scene | LMB drag | Move entities |
| Scene | RMB click | Context menu: insert a car (1–25 whiskers), cylinder, target or post where you clicked; on an entity: enter, change whiskers, delete |
| Scene | RMB drag · WSADQE · wheel | Look around · fly · fly forward/back (the wheel works like W/S) |
| Scene | Ins · O · T · Del | Add target · add post · aim all eyes at the selected target · delete |
| Scene | L · K | Start/stop training · random weights and retrain |
| Scene, creature | Ctrl+S · Z | Snapshot the brain · step back through snapshots |
| Brain graph | Drag output → input | Connect ports (grabbing a used input re-routes its wire) |
| Brain graph | Ctrl+G · Ctrl+Shift+G | Group selection into a subgraph · ungroup |
| Brain graph | Del · F · Ctrl+A | Delete · fit view · select all |

## Author

Robert Śmietana
