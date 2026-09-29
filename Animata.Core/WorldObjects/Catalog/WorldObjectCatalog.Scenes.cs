using System.Numerics;
using Animata.Core.Bodies;
using Animata.Core.Brains;
using Animata.Core.Brains.Modules;
using Animata.Core.Entities;
using Animata.Core.Worlds;

namespace Animata.Core.WorldObjects;

// Gotowe sceny: demo, węże, pająki.
public static partial class WorldObjectCatalog
{
    /// <summary>
    /// Scena węży: podłoga 30 × 30 m, kula na klocku, kilka płaskich klocków (4–10 cm) i dwa węże —
    /// z CPG i z własną siecią. Oba mają losowe parametry i uczą się dopiero po starcie nauki.
    /// </summary>
    public static DemoScene CreateSnakeScene()
    {
        var world = new World();
        world.Add(CreateFloor(30, 30));
        AddBoxes(world,
        [
            CreateBox(new Vector3(0.5f, 0.8f, 0), new Vector3(2.2f, 1.6f, 0.06f), 0.3f),
            CreateBox(new Vector3(-1.2f, 3.6f, 0), new Vector3(1.4f, 2.4f, 0.04f), -0.5f),
            CreateBox(new Vector3(2.6f, -1.6f, 0), new Vector3(1.8f, 1.2f, 0.08f), 0.9f),
            CreateBox(new Vector3(4.2f, 3.2f, 0), new Vector3(2, 2, 0.1f)),
            CreateBox(new Vector3(-3.4f, 0.6f, 0), new Vector3(1.2f, 1.2f, 0.05f), 0.2f)
        ]);

        var target = CreateSphere(new Vector3(4.2f, 3.2f, 0));
        target.Name = "Kula";
        world.Add(target);
        Terrain.Snap(world, target);
        var snake = CreateLearningSnake(new Vector3(-4.5f, -2.5f, 0), 0.6f, target.Id);
        snake.Name = "Wąż CPG";
        world.Add(snake);
        var neural = CreateNeuralSnake(new Vector3(-5.5f, 3.5f, 0), -0.1f, target.Id);
        neural.Name = "Wąż NN";
        world.Add(neural);
        return new DemoScene(world, [snake, neural]);
    }

    /// <summary>Scena pająków: podłoga 30 × 30, kilka niskich klocków, kula, pająk z chodem (CPG) i pająk z siecią.</summary>
    public static DemoScene CreateSpiderScene()
    {
        var world = new World();
        world.Add(CreateFloor(30, 30));
        AddBoxes(world,
        [
            CreateBox(new Vector3(0, 0.5f, 0), new Vector3(1.6f, 2.4f, 0.04f), 0.2f),
            CreateBox(new Vector3(2.8f, -1.5f, 0), new Vector3(1.4f, 1.4f, 0.06f), -0.4f),
            CreateBox(new Vector3(-2.2f, 2.8f, 0), new Vector3(2, 1, 0.05f), 0.8f)
        ]);
        var target = CreateSphere(new Vector3(4, 2, 0));
        target.Name = "Kula";
        world.Add(target);
        Terrain.Snap(world, target);
        var gait = CreateLearningSpider(new Vector3(-4, -2, 0), 0.4f, target.Id);
        gait.Name = "Pająk";
        world.Add(gait);
        var neural = CreateNeuralSpider(new Vector3(-4.5f, 1.5f, 0), 0, target.Id);
        neural.Name = "Pająk NN";
        world.Add(neural);
        return new DemoScene(world, [gait, neural]);
    }

    /// <summary>Dodaje klocki do świata z nazwami „Klocek 1”, „Klocek 2”, …</summary>
    private static void AddBoxes(World aWorld, Box[] aBoxes)
    {
        for (var index = 0; index < aBoxes.Length; index++)
        {
            aBoxes[index].Name = $"Klocek {index + 1}";
            aWorld.Add(aBoxes[index]);
        }
    }

    /// <summary>
    /// Dwa tory, żeby stwory nie tłoczyły się przy jednym celu:
    /// - dół: kula za czterema cylindrami i dwa autka z wąsami (sterownik i losowa sieć),
    /// - góra: wolny tor z własną kulą i dwa walce bez wąsów (sterownik i losowa sieć).
    /// Ręczne wagi sieci walca są zapisane w jej mózgu jako snapshot „ręczne wagi”.
    /// </summary>
    public static DemoScene CreateDemo()
    {
        var world = new World();
        world.Add(CreateFloor(22, 16));
        var carTarget = CreateSphere(new Vector3(5, -1.5f, 0));
        var cylinderTarget = CreateSphere(new Vector3(5, 5, 0));
        carTarget.Name = "Kula (dół)";
        cylinderTarget.Name = "Kula (góra)";
        world.Add(carTarget);
        world.Add(cylinderTarget);

        world.Add(CreateCylinder(new Vector3(1.5f, -1.3f, 0), 0.8f));
        world.Add(CreateCylinder(new Vector3(-0.5f, 0.6f, 0), 0.5f));
        world.Add(CreateCylinder(new Vector3(-0.5f, -3.9f, 0), 0.6f));
        world.Add(CreateCylinder(new Vector3(3.4f, -3.5f, 0), 0.4f));

        var controllerCar = CreateControllerCar(new Vector3(-5, -2.7f, 0), 0, carTarget.Id);
        var neuralCar = CreateNeuralCar(new Vector3(-5, -0.3f, 0), 0, carTarget.Id);
        var controller = CreateControllerSeeker(new Vector3(-5, 3.8f, 0), cylinderTarget.Id);
        var neural = CreateLearningSeeker(new Vector3(-5, 6.2f, 0), cylinderTarget.Id);
        controllerCar.Name = "Autko sterownik";
        neuralCar.Name = "Autko NN";
        controller.Name = "Walec sterownik";
        neural.Name = "Walec NN";

        ActiveEntity[] creatures = [controllerCar, neuralCar, controller, neural];
        foreach (var creature in creatures)
            world.Add(creature);
        return new DemoScene(world, creatures);
    }

    /// <summary>
    /// Wspinaczka: dwa cylindry-pnie (r 0.25 m, 3 m, tarcie chwytne) z kulami na szczytach i dwa węże owinięte u podstaw —
    /// „Wąż” z siecią (losowe wagi, uczy się wspinać po L) i „Wąż (toczenie)” z ręcznym CPG, który od razu wkręca się w górę.
    /// </summary>
    public static DemoScene CreateClimbScene()
    {
        var world = new World();
        world.Add(CreateFloor(20, 20));
        var creatures = new List<ActiveEntity>();
        foreach (var (x, name, neural) in new[] { (-2f, "Wąż", true), (2f, "Wąż (toczenie)", false) })
        {
            var tree = Training.SeekRigs.CreateClimbCylinder(new Vector3(x, 0, 0), 3);
            tree.Name = $"Cylinder {creatures.Count + 1}";
            world.Add(tree);
            var target = CreateSphere(new Vector3(x, 0, tree.Height));
            target.Radius = Training.SeekRigs.ClimbTargetRadius;
            target.Name = $"Kula {creatures.Count + 1}";
            world.Add(target);
            BrainModule brain = neural ? CreateSnakeNeuralModule(DefaultSnakeSegments, SnakeHiddenLayers) : CreateClimbingCpg();
            var snake = CreateSnake(Vector3.Zero, 0, RandomColor(), target.Id, brain);
            snake.Name = name;
            snake.WrapAround(tree, creatures.Count * 1.3f);
            world.Add(snake);
            creatures.Add(snake);
        }
        return new DemoScene(world, creatures);
    }
}
