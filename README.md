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

- **World and bodies.** Simple shapes are geometric: `Box`, `Cylinder` and `Sphere` (the target). The floor is just a
  locked box — selectable, but it cannot be moved or deleted until unlocked. Every creature is built from blocks
  (parts and joints, a `BodyPlan`) and lives in real 3D rigid-body physics (BepuPhysics 2). Three creatures:
  a **car** (box body on four wheel bodies with suspension, front-wheel steering, rear-wheel drive), a **cylinder**
  (disc on two driven side wheels and two support balls, turns in place) and a **snake**. Drive settings — top speed,
  reverse speed, maximum steering angle, turn rate, wheel torque — are editable per creature.
- **A snake that learns to crawl.** Any number of capsule segments (2–24, changeable on a living snake) linked by
  ball joints with servos (yaw and pitch — full 3D). Scales give it more grip sideways than forwards, so a wave
  running from head to tail pushes it along. A CPG module (a travelling wave with six learnable parameters) drives
  the joints; evolution teaches a random CPG to crawl to the target in a few generations. A second snake has **its
  own neural network** instead of a CPG: a clock sense (`Sin`, `Cos` of a 1.2 Hz rhythm) and the eye go in, a command
  for every joint comes out, and evolution has to discover the travelling wave and the steering by itself. It also
  feels the ground (`Feel`: height of the step 30 cm ahead of the head, head pitch, how much of the body touches
  something), so it can learn to lift its head at an edge.
- **A spider.** A four-legged walker built from the same blocks: a flat trunk and four legs (hip: swing and lift,
  knee: bend). A trot generator (diagonal legs in step, six learnable parameters: stride, lift, knee bend, knee swing,
  frequency, turning) walks it to the target — the hand-tuned gait reaches 8/8 targets on terrain with low boxes, and
  evolution teaches a random gait the same in about ten generations. A second spider has a neural network (clock and
  eye in, sixteen joint commands out) and has to find a gait on its own. Spiders feel **touch** (each foot and the
  belly) and **balance** (trunk pitch and roll); training punishes lying on the belly or falling over, which removed the
  belly-crawling "gaits" evolution used to find (bad posture 30–38 % of the time → about 1–10 %).
- **Bodies that don't pass through themselves.** Parts of one creature collide with each other unless they are
  neighbours in the joint tree (one or two joints apart), so a coiled snake stays coiled and legs don't cross.
  Joint angles are measured around the child part's own axes, so a leg pointing sideways bends like one pointing forward.
- **Climbing.** A trunk is a cylinder with grip friction. A snake can be wrapped around a cylinder (context menu
  "Owiń wokół cylindra"): its joints get the constant bend of a helix slightly tighter than the trunk, so the coil
  squeezes and friction holds it. Rolling the coil — every joint's bend vector turning in time — screws it up the
  trunk: the hand-tuned rolling CPG climbs about 0.35 m/s and reaches the ball on top of a 2–3.5 m trunk in 5 of 6
  trials. A snake marked as a climber trains on climbing (wrapped at the base, target on top). A neural snake does
  not learn to climb from random weights yet (150 generations: no trial reached the top). Snake joints bend ±69° in
  both axes with 8 N·m servos.
- **Terrain.** Flat boxes (a few centimetres high, any size and rotation) lie on the floor; snakes climb over them in
  physics, and training episodes for snakes scatter 0–3 boxes on the way, sometimes with the target on top of one.
  Spheres, cylinders and boxes snap to the height of the ground under them (a toggle in the scene).
- **Senses.** An eye that tracks a target (distance, gap, direction in the creature's own frame) and whiskers: rays
  that report how close obstacles are. A car can have any odd number of whiskers from 1 to 25, spread over 120°;
  the number can be changed on a living car (scene, creature view or graph inspector): the same brain is rewired in
  place, a network gets weights for the new whiskers derived from the nearest old ones instead of starting over,
  and training continues in bodies with the new whisker count.
- **Brains as graphs.** Sensor → logic → actuator graphs with validation (unknown ports, cycles, double-driven
  actuators and bad configuration are errors, not silent zeros). Logic can be a hand-written controller, a neural
  network whose inputs are small expressions over sensor ports, a router, a constant, or a **subgraph** (composite
  pattern, any depth) with group/ungroup that keeps the wiring intact.
- **Background evolution.** Each child mutates with a randomly picked strength (¼, ½, 1 or 2 × σ): some fine-tune,
  some jump further — the neural spider now reaches 8/8 targets in 20–30 generations instead of 6–7/8. Nothing learns until you start it (L, or the training button). New creatures start
  from random parameters; a creature loaded from a file starts from the snapshot the file marks as current (saving
  marks the snapshot matching the brain's state, adding a „zapis” snapshot if none does). A genetic algorithm trains
  the parameters on its own thread, starting from the creature's current ones. The scene only receives the
  *champion*, the best candidate on a fixed validation set, so creatures change rarely and only for the better.
- **Snapshots.** Any brain's parameters can be captured, compared and restored. Training writes its own snapshots and
  undo walks back through them.
- **Saving worlds.** A whole world goes to JSON and back: entities, bodies, target links and complete brains
  (modules, wiring, subgraphs, node positions, parameters and snapshots). Brains connect to bodies by slot name
  (`Eye`, `Whiskers`, `Spine`), not by id. Sense and actuator nodes in the brain graph are not stored — they are a
  live view of the body (one node per sensor and actuator, ports straight from the body), so only the logic and the
  wires are saved.
- **Studio.** A desktop app with four scenes (demo; snakes on terrain; spiders on terrain; climbing) which you *zoom into*: double-click a creature and the window turns into its panel (senses, body,
  actuators, brain). Click the brain and you are in a live graph editor; double-click a subgraph and you go one level
  deeper; double-click a neural network and you see its layers — neurons lit by their live activations, weights as
  coloured lines — and can change the number of neurons in each hidden layer and add or remove hidden layers (kept
  weights stay, new connections start random, the old shape stays in a snapshot, training resumes on the new shape).
  Pause, step, change speed and edit while it runs.

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
- moving creatures (body and brain) between worlds; a body editor for building creatures from blocks;
- climbing: snakes on slopes and trees.

## Projects

| Project | What it is |
| --- | --- |
| `Animata.Core` | Model, bodies (incl. bodies from blocks), sensors, actuators, brains, physics (BepuPhysics 2), training, snapshots, graph editing, saving. No UI. |
| `Animata.Rendering.HelixToolkit` | 3D scene: meshes, picking, dragging, whisker rays, fly camera. |
| `Animata.Studio` | Desktop app: zoomable panels, scene, creature view, brain graph editor, themes. |
| `Animata.Tests` | xUnit tests for the core. |

## Running

Requirements: **.NET 10 SDK**. The Studio uses HelixToolkit's SharpDX renderer, so it runs on **Windows**.

BepuPhysics comes from NuGet (`BepuPhysics` 2.5.0-beta.29, the newest package).

```powershell
dotnet build
dotnet test Animata.Tests
dotnet run --project Animata.Studio
```

CI (GitHub Actions, `.github/workflows/build.yml`) builds the whole solution and runs the tests on Windows and Linux for every push.

### Studio controls

| Where | Input | Action |
| --- | --- | --- |
| Everywhere | Double-click | Zoom into a creature or subgraph (cards in a creature open with a single click) |
| Everywhere | Esc · Alt+← · mouse Back | Zoom out one level |
| Scene, creature, graph | Space | Pause / resume the world |
| Scene | LMB drag | Move entities (dragging one of several selected moves them all) |
| Scene | LMB drag from empty space or a locked entity (floor) · Ctrl+click · Shift+click · Ctrl+A | Box-select · toggle · add to selection · select all |
| Scene | Del · Ctrl+C · Ctrl+X · Ctrl+V | Delete the whole selection · copy · cut · paste under the mouse (brains, snapshots and settings included; works across scenes) |
| Scene | RMB click | Context menu: insert a car, walec (disc robot), snake or spider (each with a two-hidden-layer neural network with random weights and a random colour; swap the brain module in the graph), a sphere (target), cylinder or box where you clicked; on an entity: enter, aim eyes, copy, cut, delete, and on a snake: wrap it around the nearest cylinder |
| Scene | RMB drag · WSADQE · wheel | Look around · fly · fly forward/back (the wheel works like W/S) |
| Scene | Ins · O · P · T · Del | Add sphere · cylinder · box · aim all eyes at the selected sphere · delete |
| Scene | G · magnet button | Snap to ground on/off: spheres, cylinders and dragged entities stand on the highest box below (e.g. the floor) |
| Scene | L · K | Start/stop training · random weights and retrain |
| Scene, creature | Ctrl+S · Z | Snapshot the brain · step back through snapshots |
| Scene | Save · Load (toolbar) | Write the whole world to a `.animata.json` file · replace the scene with a saved one |
| Brain graph | Drag output → input | Connect ports (grabbing a used input re-routes its wire) |
| Brain graph | Ctrl+G · Ctrl+Shift+G | Group selection into a subgraph · ungroup |
| Brain graph | Del · F · Ctrl+A | Delete · fit view · select all |
| Brain graph | Double-click · Enter | Enter a subgraph or a neural network |
| Neural network | Click neuron · click column | Show its bias and incoming weights · select a layer |
| Neural network | + · − · Ins · Del | One neuron more / less in the selected hidden layer · add a hidden layer after it · remove it |

## Author

Robert Śmietana
