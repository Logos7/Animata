using System.Numerics;
using Animata.Core.Brains.Modules;
using Animata.Core.Entities;
using Animata.Core.Worlds;

namespace Animata.Core.WorldObjects;

// Gotowe sceny: demo, węże, pająki, wspinaczka.
public static partial class WorldObjectCatalog
{
    /// <summary>
    /// Scena węży: podłoga 30 × 30 m, kula na klocku, kilka płaskich klocków (4–10 cm) i dwa węże —
    /// z CPG i z własną siecią. Oba mają losowe parametry i uczą się dopiero po starcie nauki.
    /// </summary>
    public static DemoScene CreateSnakeScene()
    {
        var world = FloorWorld(30, 30);
        AddBoxes(world,
        [
            CreateBox(new Vector3(0.5f, 0.8f, 0), new Vector3(2.2f, 1.6f, 0.06f), 0.3f),
            CreateBox(new Vector3(-1.2f, 3.6f, 0), new Vector3(1.4f, 2.4f, 0.04f), -0.5f),
            CreateBox(new Vector3(2.6f, -1.6f, 0), new Vector3(1.8f, 1.2f, 0.08f), 0.9f),
            CreateBox(new Vector3(4.2f, 3.2f, 0), new Vector3(2, 2, 0.1f)),
            CreateBox(new Vector3(-3.4f, 0.6f, 0), new Vector3(1.2f, 1.2f, 0.05f), 0.2f)
        ]);
        var target = AddTarget(world, new Vector3(4.2f, 3.2f, 0));
        var snake = AddNamed(world, CreateLearningSnake(new Vector3(-4.5f, -2.5f, 0), 0.6f, target.Id), "Wąż CPG");
        var neural = AddNamed(world, CreateNeuralSnake(new Vector3(-5.5f, 3.5f, 0), -0.1f, target.Id), "Wąż NN");
        return new DemoScene(world, [snake, neural]);
    }

    /// <summary>Scena pająków: podłoga 30 × 30, kilka niskich klocków, kula, pająk z chodem (CPG) i pająk z siecią.</summary>
    public static DemoScene CreateSpiderScene()
    {
        var world = FloorWorld(30, 30);
        AddBoxes(world,
        [
            CreateBox(new Vector3(0, 0.5f, 0), new Vector3(1.6f, 2.4f, 0.04f), 0.2f),
            CreateBox(new Vector3(2.8f, -1.5f, 0), new Vector3(1.4f, 1.4f, 0.06f), -0.4f),
            CreateBox(new Vector3(-2.2f, 2.8f, 0), new Vector3(2, 1, 0.05f), 0.8f)
        ]);
        var target = AddTarget(world, new Vector3(4, 2, 0));
        var gait = AddNamed(world, CreateLearningSpider(new Vector3(-4, -2, 0), 0.4f, target.Id), "Pająk");
        var neural = AddNamed(world, CreateNeuralSpider(new Vector3(-4.5f, 1.5f, 0), 0, target.Id), "Pająk NN");
        return new DemoScene(world, [gait, neural]);
    }

    /// <summary>
    /// Dwa tory, żeby stwory nie tłoczyły się przy jednym celu:
    /// - dół: kula za czterema cylindrami i dwa autka z wąsami (sterownik i losowa sieć),
    /// - góra: wolny tor z własną kulą i dwa walce bez wąsów (sterownik i losowa sieć).
    /// Ręczne wagi sieci walca są zapisane w jej mózgu jako snapshot „ręczne wagi”.
    /// </summary>
    public static DemoScene CreateDemo()
    {
        var world = FloorWorld(22, 16);
        var carTarget = AddTarget(world, new Vector3(5, -1.5f, 0), "Kula (dół)");
        var cylinderTarget = AddTarget(world, new Vector3(5, 5, 0), "Kula (góra)");

        world.Add(CreateCylinder(new Vector3(1.5f, -1.3f, 0), 0.8f));
        world.Add(CreateCylinder(new Vector3(-0.5f, 0.6f, 0), 0.5f));
        world.Add(CreateCylinder(new Vector3(-0.5f, -3.9f, 0), 0.6f));
        world.Add(CreateCylinder(new Vector3(3.4f, -3.5f, 0), 0.4f));

        ActiveEntity[] creatures =
        [
            AddNamed(world, CreateControllerCar(new Vector3(-5, -2.7f, 0), 0, carTarget.Id), "Autko sterownik"),
            AddNamed(world, CreateNeuralCar(new Vector3(-5, -0.3f, 0), 0, carTarget.Id), "Autko NN"),
            AddNamed(world, CreateControllerSeeker(new Vector3(-5, 3.8f, 0), cylinderTarget.Id), "Walec sterownik"),
            AddNamed(world, CreateLearningSeeker(new Vector3(-5, 6.2f, 0), cylinderTarget.Id), "Walec NN")
        ];
        return new DemoScene(world, creatures);
    }

    /// <summary>
    /// Wspinaczka: dwa cylindry-pnie (r 0.25 m, 3 m, tarcie chwytne) z kulami na szczytach i dwa węże owinięte u podstaw —
    /// „Wąż” z siecią (losowe wagi, uczy się wspinać po L) i „Wąż (toczenie)” z ręcznym CPG, który od razu wkręca się w górę.
    /// </summary>
    public static DemoScene CreateClimbScene()
    {
        var world = FloorWorld(20, 20);
        var creatures = new List<ActiveEntity>();
        foreach (var (x, name, neural) in new[] { (-2f, "Wąż", true), (2f, "Wąż (toczenie)", false) })
        {
            var tree = AddNamed(world, Training.SeekRigs.CreateClimbCylinder(new Vector3(x, 0, 0), 3), $"Cylinder {creatures.Count + 1}");
            var target = CreateSphere(new Vector3(x, 0, tree.Height));
            target.Radius = Training.SeekRigs.ClimbTargetRadius;
            AddNamed(world, target, $"Kula {creatures.Count + 1}");
            BrainModule brain = neural ? CreateSnakeNeuralModule(Snake.DefaultSegments, SnakeHiddenLayers) : CreateClimbingCpg();
            var snake = CreateSnake(Vector3.Zero, 0, RandomColor(), target.Id, brain);
            snake.Name = name;
            Snake.WrapAround(snake, tree, creatures.Count * 1.3f);
            world.Add(snake);
            creatures.Add(snake);
        }
        return new DemoScene(world, creatures);
    }

    /// <summary>Świat z podłogą szer. × głęb. (zablokowany klocek z górą na z = 0).</summary>
    private static World FloorWorld(float aWidth, float aDepth)
    {
        var world = new World();
        world.Add(CreateFloor(aWidth, aDepth));
        return world;
    }

    /// <summary>Kula-cel postawiona na terenie.</summary>
    private static Sphere AddTarget(World aWorld, Vector3 aPosition, string aName = "Kula")
    {
        var target = AddNamed(aWorld, CreateSphere(aPosition), aName);
        Terrain.Snap(aWorld, target);
        return target;
    }

    /// <summary>Dodaje klocki do świata z nazwami „Klocek 1”, „Klocek 2”, …</summary>
    private static void AddBoxes(World aWorld, Box[] aBoxes)
    {
        for (var index = 0; index < aBoxes.Length; index++)
            AddNamed(aWorld, aBoxes[index], $"Klocek {index + 1}");
    }
}
