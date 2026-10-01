# Mapa kodu Animaty

Projekty i ich zależności, kto kogo posiada w działającym programie, hierarchie klas, gniazda ciała każdego stwora oraz przebieg ticku, budowy obiektu, nauki i zapisu. Stan: krok 34. Diagramy są w Mermaid, więc GitHub rysuje je bezpośrednio w tym pliku. Przy zmianach w strukturze kodu (nowe klasy, inna własność, nowy krok budowy) aktualizuj odpowiedni diagram.

**Legenda strzałek w diagramach klas**

| Zapis | Znaczenie |
|---|---|
| `A <\|-- B` | B dziedziczy po A (klasa bazowa u grotu) |
| `A <\|.. B` | B implementuje interfejs A |
| `A *-- B` | A posiada B (zawieranie, wspólny czas życia) |
| `A o-- B` | A trzyma B, ale B żyje też poza nim |
| `A ..> B` | A używa B (zależność, bez posiadania) |
| `"*"` / `"0..1"` | liczność: wiele / co najwyżej jeden |

**Spis:** [Projekty i paczki](#projekty) · [Własność w działającym świecie](#wlasnosc) · [Własność w Studiu](#studio-wlasnosc) · [Hierarchia encji](#encje) · [Budowa obiektu: rejestr i Spawn](#budowa) · [Ciało z klocków](#cialo) · [Zmysły i napędy](#zmysly) · [Gniazda ciała każdego stwora](#gniazda) · [Mózg: moduły](#mozg) · [Stany modułów i snapshoty](#stany) · [Jeden tick](#tick) · [Nauka w tle](#nauka) · [Zapis świata i mózgu](#zapis) · [Klasy UI: panele, kontrolki, renderer](#ui)

<a id="projekty"></a>

## Projekty i paczki

Pięć projektów w `Animata.slnx`. Rdzeń nie zna UI; logika sceny Studia siedzi w osobnym projekcie bez UI, więc testy sięgają do niej bez Avalonii.

```mermaid
flowchart LR
  Studio["Animata.Studio<br/>WinExe · Avalonia 11.3.21"]
  Session["Animata.Studio.Session<br/>logika sceny bez UI"]
  Render["Animata.Rendering.HelixToolkit<br/>HelixToolkit.Avalonia.SharpDX 3.1.2"]
  Core["Animata.Core<br/>model, fizyka, mózgi, nauka, zapis"]
  Tests["Animata.Tests<br/>xUnit 2.9.3"]
  Bepu[("BepuPhysics 2.5.0-beta.29<br/>+ BepuUtilities")]
  Studio --> Session
  Studio --> Render
  Studio --> Core
  Session --> Core
  Render --> Core
  Tests --> Core
  Tests --> Session
  Core --> Bepu
```

*Strzałka = odwołanie projektu (ProjectReference) albo paczki NuGet. Studio jest jedynym projektem, który widzi wszystkie pozostałe.*

<a id="wlasnosc"></a>

## Własność w działającym świecie

Od świata w dół do części ciała i modułu mózgu. Ciało to sprzęt, mózg to oprogramowanie; mózg dotyka ciała tylko przez węzły gniazd.

```mermaid
classDiagram
  direction TB
  class DemoScene {
    +World
    +Creatures
  }
  class World {
    +Entities
    +Physics
    +Time
    +Update(delta)
    +Add(entity)
    +Remove(entity)
  }
  class PhysicsWorld {
    +Simulation
    +Step(delta)
    +IsTouching(body)
  }
  class Entity {
    +Id
    +Name
    +Body
    +Locked
    +Place(pos, rot)
  }
  class ActiveEntity {
    +Brain
    +Equip()
    +BrainPresets
    +TrainingRig
  }
  class ArticulatedCreature {
    +Plan
    +Color
    +PartPositions
    +SetJointTarget()
  }
  class Body {
    +Position
    +Rotation
    +Sensors
    +Actuators
    +FindSensor(slot)
    +FindActuator(slot)
  }
  class Brain {
    +Graph
    +Snapshots
    +Body
    +SyncBody()
    +Think()
    +Act()
    +Restore(snapshot)
  }
  class BrainGraph {
    +Modules
    +Connections
    +Positions
    +Validate()
  }
  class BrainModule {
    +Id
    +Name
    +InputPorts
    +OutputPorts
    +Evaluate()
    +Commit()
  }
  class SensorModule {
    +Slot
  }
  class ActuatorModule {
    +Slot
    +LastCommand
  }
  class CompositeModule {
    +Inner
    +Input
    +Output
  }
  class BrainSnapshot {
    +Id
    +Label
    +CreatedUtc
    +Modules
  }

  DemoScene *-- World
  World "1" *-- "*" Entity : Entities
  World "1" *-- "0..1" PhysicsWorld : powstaje z 1. stworem
  Entity *-- Body
  Entity <|-- ActiveEntity
  ActiveEntity <|-- ArticulatedCreature
  ArticulatedCreature <|-- Creature
  Creature --> CreatureDesign : projekt
  ActiveEntity "1" *-- "0..1" Brain
  Body "1" *-- "*" Sensor
  Body "1" *-- "*" Actuator
  Brain ..> Body : widok ciała
  Brain *-- BrainGraph
  Brain "1" *-- "*" BrainSnapshot
  BrainGraph "1" *-- "*" BrainModule
  BrainGraph "1" *-- "*" BrainConnection
  BrainModule <|-- SensorModule
  BrainModule <|-- ActuatorModule
  BrainModule <|-- CompositeModule
  CompositeModule *-- BrainGraph : Inner
  SensorModule ..> Sensor : gniazdo
  ActuatorModule ..> Actuator : gniazdo
  ArticulatedCreature *-- BodyPlan : Plan
  ArticulatedCreature ..> PhysicsWorld : bryły i stawy Bepu
```

*Węzły `SensorModule` i `ActuatorModule` nie są zapisywane: `Brain.SyncBody` tworzy je z gniazd ciała (Id = MD5 z rodzaju i nazwy gniazda), tylko na najwyższym poziomie grafu.*

<a id="studio-wlasnosc"></a>

## Własność w Studiu

Okno trzyma sceny (sesje) i jeden stos paneli. Sesja nie wie nic o UI; panele czytają sesję i wołają jej akcje.

```mermaid
classDiagram
  direction LR
  class StudioWindow {
    -scenes
    -panels
    -timer 33 ms
    +OpenFile(path)
  }
  class StudioSession {
    +World
    +Training
    +History
    +Speed
    +Tick(elapsed)
    +AddCreature(type, pos)
    +ChangeSetting()
    +InstallBrain()
    +Copy() +Paste()
    +Save() +Load()
  }
  class TrainingController {
    +Start(creature)
    +Stop(brain)
    +Poll()
  }
  class SnapshotHistory {
    +Capture()
    +StepBack()
  }
  class PanelNavigator {
    +Stack
    +Push(panel, point)
    +PopTo(index)
  }
  class NavigationBar
  class SimulationPanel
  class SceneRenderer {
    +Sync(world)
    +Selection
  }
  class RecentFiles {
    +Add(path)
    +Paths
  }

  StudioWindow *-- PanelNavigator
  StudioWindow *-- NavigationBar
  StudioWindow "1" *-- "*" StudioSession : sceny
  StudioWindow "1" *-- "*" SimulationPanel : panel na scenę
  NavigationBar ..> PanelNavigator
  PanelNavigator o-- "*" StudioPanel : stos
  StudioPanel <|-- SimulationPanel
  SimulationPanel ..> StudioSession
  SimulationPanel *-- SceneRenderer
  StudioSession *-- DemoScene
  StudioSession *-- TrainingController
  StudioSession *-- SnapshotHistory
  TrainingController ..> SnapshotHistory : mistrz → snapshot
  StudioWindow ..> RecentFiles
```

*Zegar okna woła `Tick(czas klatki)` tylko dla sceny, której panel jest na stosie; ukryta scena stoi, a jej nauka czeka między pokoleniami.*

<a id="encje"></a>

## Hierarchia encji

Każdy obiekt świata jest `Entity`. Stwory są bryłami złożonymi z części; klocek i cylinder to statyczne bryły fizyki, kula (cel oka) fizyki nie ma.

```mermaid
classDiagram
  direction TB
  class Entity {
    <<abstract>>
    +Id
    +Name
    +Body
    +Locked
    +BoundingRadius
    +Category
    +Place()
  }
  class ActiveEntity {
    <<abstract>>
    +Brain
    +Equip()
    +BrainPresets
    +TrainingRig
    +Describe()
  }
  class StaticEntity {
    <<abstract>>
  }
  class PhysicalStaticEntity {
    <<abstract>>
    #Shape
    #Build(physics)
  }
  class IPhysicalEntity {
    <<interface>>
    +IsDynamic
    +AttachPhysics()
    +DetachPhysics()
    +BeforePhysicsStep()
    +AfterPhysicsStep()
  }
  class ArticulatedCreature {
    +Plan
    +Color
    +Rebuild(plan)
    +PlaceBent()
    +PlaceParts()
  }
  class Creature {
    +Design
    +Values
    +Blueprint()
    +Reshape()
  }
  class CreatureDesign {
    +Id
    +Name
    +Blueprint(values)
    +Settings
    +Presets
    +TrainingRig
  }
  class Car {
    <<static>>
    +Design
  }
  class Disc {
    <<static>>
    +Design
  }
  class Snake {
    <<static>>
    +Design
    +SetSegments()
    +WrapAround()
  }
  class Spider {
    <<static>>
    +Design
  }
  class Humanoid {
    <<static>>
    +Design
    +Ports
    +Posture()
  }
  class MuscleHumanoid {
    <<static>>
    +Design
    +MusclePorts
    +LegMusclesOf(side)
  }
  class Box {
    +Size
    +Color
  }
  class Cylinder {
    +Radius
    +Height
    +Grip
    +Color
  }
  class Sphere {
    +Radius
  }

  Entity <|-- ActiveEntity
  Entity <|-- StaticEntity
  ActiveEntity <|-- ArticulatedCreature
  ArticulatedCreature <|-- Creature
  Creature --> CreatureDesign : Design
  Car ..> CreatureDesign : definiuje
  Disc ..> CreatureDesign : definiuje
  Snake ..> CreatureDesign : definiuje
  Spider ..> CreatureDesign : definiuje
  Humanoid ..> CreatureDesign : definiuje
  MuscleHumanoid ..> CreatureDesign : definiuje
  StaticEntity <|-- PhysicalStaticEntity
  StaticEntity <|-- Sphere
  PhysicalStaticEntity <|-- Box
  PhysicalStaticEntity <|-- Cylinder
  IPhysicalEntity <|.. ArticulatedCreature
  IPhysicalEntity <|.. PhysicalStaticEntity
  IPhysicalEntity ..> PhysicsWorld
  Snake ..> SnakeWrap : owijanie
  Snake ..> PortRewiring : zmiana segmentów
  Car ..> WhiskerRewiring : zmiana wąsów
  WhiskerRewiring ..> PortRewiring
```

*Każdy stwór to jedna klasa `Creature` z projektem (`CreatureDesign`); autko, walec, wąż i pająk to projekty, nie klasy. Własna podklasa `ArticulatedCreature` nadal działa, ale nie jest potrzebna. Podłoga nie ma własnej klasy: to `Box` z `Locked = true`. Ustawienia — właściwości z `[Setting]` (np. `Grip`) i ustawienia projektu (np. `Segments`, `Whiskers`) — edytuje jeden wspólny edytor i zapisuje plik świata.*

<a id="budowa"></a>

## Budowa obiektu: rejestr i Spawn

Każdy obiekt powstaje jedną drogą. Rejestr zna rodzaje i ich identyfikatory w pliku; `Spawn` wykonuje kroki zawsze w tej samej kolejności.

```mermaid
classDiagram
  direction TB
  class EntityTypes {
    <<static>>
    +All
    +Creatures
    +Find(id)
    +Of(entity)
    +Register(type)
  }
  class EntityType {
    +Id
    +Name
    +Icon
    +ClrType
    +Design
    +Create()
    +IsCreature
  }
  class CreatureDesign {
    +Blueprint(values)
    +Settings
    +Presets
    +TrainingRig
    +Type
    +FromBlueprint()
  }
  class CreatureBlueprint {
    +Plan
    +Sensors
    +Actuators
    +ServoFrequency
    +BoundingRadius
    +ToJson()
    +FromJson()
  }
  class SlotSpec {
    +Slot
    +Type
    +Settings
    +Touch
  }
  class SlotTypes {
    <<static>>
    +CreateSensor(spec, plan)
    +CreateActuator(spec, plan)
    +Register()
  }
  class DesignSetting {
    +Name
    +Default
    +Get
    +Set
  }
  class Spawn {
    +Type
    +Settings
    +Slots
    +Brain
    +Id
    +Name
    +Pose
    +Build()
  }
  class WorldObjectCatalog {
    <<static>>
    +CreateCar()
    +CreateSnake()
    +CreateFloor()
    +BuildBrain()
    +InstallBrain()
    +CreateScenes()
  }
  class Settings {
    <<static>>
    +Describe(type)
    +Of(owner)
    +Capture()
    +Apply()
    +Copy()
  }
  class SettingAttribute {
    +Label
    +Unit
    +Min
    +Max
    +Step
    +Scale
    +Reshapes
    +Slots
    +Derived
  }
  class SettingInfo {
    +Get()
    +Set()
    +Choices()
    +Range()
  }
  class BrainPreset {
    +Name
    +Description
    +Create()
    +HandTuned
  }

  EntityTypes "1" *-- "*" EntityType
  EntityType o-- CreatureDesign : stwór z projektu
  CreatureDesign ..> CreatureBlueprint : ciało z wartości ustawień
  CreatureDesign "1" *-- "*" DesignSetting
  CreatureBlueprint "1" *-- "*" SlotSpec
  SlotTypes ..> SlotSpec : zmysł albo napęd z gniazda
  Spawn --> EntityType
  Spawn ..> Entity : Build()
  WorldObjectCatalog ..> Spawn
  WorldFile ..> Spawn : odczyt pliku i schowka
  StudioSession ..> Spawn : AddCreature
  SeekRigs ..> Spawn : rig ogólny
  Settings ..> SettingInfo
  SettingInfo *-- SettingAttribute
  ActiveEntity ..> BrainPreset : BrainPresets
  CreatureDesign ..> BrainPreset : Presets
```

*Fabryki katalogu, plik świata, schowek, rigi nauki i Studio składają `Spawn`; nikt poza nim nie woła konstruktorów obiektów.*

```mermaid
flowchart TB
  A["EntityType.Create<br/>ciało, zmysły, napędy, pusty mózg z węzłami gniazd"]
  B["Settings<br/>ustawienia obiektu — mogą przebudować ciało"]
  C["Slots<br/>ustawienia zmysłów i napędów, np. cel oka"]
  D["Brain<br/>gotowy mózg, sterownik albo zapisany"]
  E["Id i Name"]
  F["Pose<br/>Place albo Body wprost, gdy Locked"]
  A --> B --> C --> D --> E --> F
```

*Ustawienia gniazd idą po ustawieniach obiektu, bo np. liczba segmentów węża zmienia liczbę portów stawów.*

<a id="cialo"></a>

## Ciało z klocków

Plan ciała to dane: części i stawy. `ArticulatedCreature` zamienia go na bryły i ograniczenia Bepu.

```mermaid
classDiagram
  direction LR
  class BodyPlan {
    +Parts
    +Joints
    +Muscles
  }
  class MusclePlan {
    +Name
    +Origin, OriginPoint
    +Insertion, InsertionPoint
    +MaxForce
    +OptimalLength
    +MaxVelocity
    +ForceLength()
    +Passive()
  }
  class PartPlan {
    +Name
    +Shape
    +Size
    +Mass
    +RestPose
    +Friction
    +LateralFriction
    +BackwardFriction
  }
  class JointPlan {
    +Name
    +Parent
    +Child
    +Anchor
    +Kind
    +MaxYaw
    +MaxPitch
    +MinYaw
    +MinPitch
    +Strength
    +HasYaw
    +HasPitch
    +Bends
    +DrivesYaw/DrivesPitch
  }
  class PartShape {
    <<enum>>
    Capsule Box Sphere Cylinder
  }
  class JointKind {
    <<enum>>
    Ball Fixed Wheel Passive
  }
  class BodyPlanBuilder {
    +Part()
    +Joint()
    +Hinge()
    +Swivel()
    +Weld()
    +Wheel()
    +Passive()
    +Muscle()
    +Build()
  }
  class ArticulatedCreature {
    -bodies
    -servos
    +SetJointTarget()
    +SetWheelTarget()
    +SetMuscleExcitation()
    +MuscleActivation/Length/Force()
    +IsPartTouching()
  }
  BodyPlan "1" *-- "*" PartPlan
  BodyPlan "1" *-- "*" JointPlan
  BodyPlan "1" *-- "*" MusclePlan
  PartPlan ..> PartShape
  JointPlan ..> JointKind
  BodyPlanBuilder ..> BodyPlan : waliduje i buduje
  ArticulatedCreature *-- BodyPlan
  ArticulatedCreature ..> PhysicsWorld : część = bryła, staw = serwo, mięsień = silnik liniowy
```

*Staw kulowy to BallSocket + AngularServo (skręt i pochylenie w osiach dziecka), z zakresem od Min do Max wokół pozy spoczynkowej — może być jednokierunkowy (kolano pająka: tylko zgięcie). Zawias (`Hinge`) i obrotnica (`Swivel`) to staw kulowy z zablokowaną jedną osią; porty kręgosłupa i czucia stawów są tylko dla ruchomych osi. Koło to zawieszenie, prowadnica, zawias i opcjonalnie silnik. Części 1–2 stawy od siebie się nie zderzają.*

*Staw bierny (`Passive`) to te same osie bez serwa: BallSocket, więzy osi (AngularHinge albo AngularSwivelHinge), ograniczniki TwistLimit wokół osi skrętu i pochylenia i tłumik kątowy (Strength = N·m·s/rad). Rusza nim mięsień: silnik liniowy (LinearAxisMotor) między przyczepami, z siłą z modelu Hilla — długość i aktywacja co krok, szybkość niejawnie w solverze (tłumik, który dąży do skracania), więc nie drga przy kroku świata 1/30 s.*

<a id="zmysly"></a>

## Zmysły i napędy

Zmysł czyta świat i wystawia porty; napęd przyjmuje komendy na portach i zmienia świat w fazie Act.

```mermaid
classDiagram
  direction TB
  class Sensor {
    <<abstract>>
    +Slot
    +OutputPorts
    +Read(owner, world)
  }
  class Actuator {
    <<abstract>>
    +Slot
    +InputPorts
    +Apply(owner, commands, dt)
  }
  class TargetSensor {
    +TargetId → Found Distance Gap DirectionX/Y/Z
  }
  class RaySensor {
    +RayAngles
    +Range
    +Detects → Ray0…Rayn
  }
  class JointSensor {
    → Yaw0… Pitch0…
  }
  class ClockSensor {
    +Frequency → Sin Cos
  }
  class FeelSensor {
    +Reach → Ahead HeadPitch HeadRoll Touch
  }
  class TouchSensor {
    → port na część
  }
  class DiskDriveActuator {
    Turn Step ·
    +MaxSpeed
    +MaxTurnSpeed
    +DriveTorque
  }
  class SteeringDriveActuator {
    Steer Throttle ·
    +MaxSpeed
    +MaxReverseSpeed
    +MaxSteerAngle
    +DriveTorque
  }
  class SpineActuator {
    Yaw0… Pitch0… (stawy z serwem)
  }
  class MuscleActuator {
    pobudzenie na mięsień 0…1
  }
  class BalanceSensor {
    +Gain
    +RateGain → Pitch Roll PitchRate RollRate Height VelX VelY
  }
  class MuscleSensor {
    → Dł… Szyb… Siła… na mięsień
  }
  Sensor <|-- TargetSensor
  Sensor <|-- RaySensor
  Sensor <|-- JointSensor
  Sensor <|-- ClockSensor
  Sensor <|-- FeelSensor
  Sensor <|-- TouchSensor
  Sensor <|-- BalanceSensor
  Sensor <|-- MuscleSensor
  Actuator <|-- DiskDriveActuator
  Actuator <|-- SteeringDriveActuator
  Actuator <|-- SpineActuator
  Actuator <|-- MuscleActuator
```

*Po strzałce „→” są porty wyjściowe zmysłu; w napędach przed kropką porty komend, po niej ustawienia `[Setting]`.*

<a id="gniazda"></a>

## Gniazda ciała każdego stwora

Mózg łączy się z ciałem po nazwie gniazda (`Slot`), nie po Id. Ten sam mózg pasuje do każdego ciała z tymi gniazdami i portami. Gniazda opisuje projekt ciała (`CreatureBlueprint`): nazwa, rodzaj z `SlotTypes` i ustawienia.

| Stwór | Zmysły (gniazdo: klasa) | Napędy | Gotowe mózgi | Rig nauki |
|---|---|---|---|---|
| Autko | Eye: TargetSensor · Whiskers: RaySensor (1–25, domyślnie 5) | Wheels: SteeringDriveActuator | sieć, AvoidAndSeek | CarWith(n) |
| Walec | Eye: TargetSensor | Wheels: DiskDriveActuator | sieć, sieć z ręcznymi wagami, Approach | Disk |
| Wąż | Eye: TargetSensor · Joints: JointSensor · Clock: ClockSensor (1.2 Hz) · Feel: FeelSensor | Spine: SpineActuator | sieć, CPG pełzanie, CPG toczenie | SnakeWith(n) · ClimbWith(n) |
| Pająk | Eye: TargetSensor · Joints: JointSensor (12 osi) · Clock: ClockSensor (2.5 Hz) · Feel: FeelSensor · Touch: TouchSensor | Legs: SpineActuator (12 osi: zamach, uniesienie, kolano × 4 nogi) | sieć, kłus | Spider |
| Humanoid | Eye: TargetSensor · Joints: JointSensor (18 osi) · Clock: ClockSensor (1 Hz) · Balance: BalanceSensor · Touch: TouchSensor (stopy, tułów) | Body: SpineActuator (18 osi: talia, biodra, kolana, kostki, barki, łokcie) | stój i idź (dwie sieci + automat), stój i idź ręcznie, sieć stania, stanie ręczne, chód ręczny | moduł stania: HumanoidStand · reszta: HumanoidWalk (`ModuleRig`) |
| Humanoid mięśniowy | jak Humanoid (Joints: także 12 osi biernych nóg) · MuscleSense: MuscleSensor (72 porty) | Body: SpineActuator (talia, barki, łokcie) · Muscles: MuscleActuator (24 mięśnie nóg) | stój i idź · mięśnie, stój i idź · dwie sieci, stanie mięśniami, chód mięśniami | stanie: MuscleStand · reszta: MuscleWalk (CMA-ES) |
| Nowy stwór | dowolne, w `CreatureBlueprint.Sensors` | dowolne, także kilka | `CreatureDesign.Presets` (domyślnie ogólna sieć) | `TrainingRig` ?? Generic |

<a id="mozg"></a>

## Mózg: moduły

Graf modułów połączonych port do portu. Uczą się tylko moduły z `ITrainableModule`; podgraf to moduł z własnym grafem w środku.

```mermaid
classDiagram
  direction TB
  class BrainModule {
    <<abstract>>
    +Id
    +Name
    +InputPorts
    +OutputPorts
    +Evaluate()
    +Commit()
    +CaptureState()
    +RestoreState()
    +Reset()
    +Validate()
  }
  class ITrainableModule {
    <<interface>>
    +GetParameters()
    +SetParameters()
    +Randomize()
  }
  class TrainableModules {
    <<static>>
    +ParameterCount(state)
    +Create(state, params)
  }
  class NeuralNetworkModule {
    +Network
    +Ports
    +Inputs
    +Outputs
  }
  class NeuralNetwork {
    +Layers
    +Weights
    +Biases
    +ResizeLayer()
    +InsertLayer()
    +RemoveLayer()
  }
  class NeuralInput {
    +Expression
  }
  class NeuralOutput {
    +Port
    +Scale
    +Offset
  }
  class SensorExpression {
    <<static>>
    +Compile(text)
  }
  class CompositeModule {
    +Inner
    +Input
    +Output
  }
  class BrainGraphEditing {
    <<static>>
    +TryConnect()
    +Group()
    +Ungroup()
    +AddPort()
    +AutoLayout()
  }
  class BrainException {
    +ModuleId
  }

  BrainModule <|-- SensorModule
  BrainModule <|-- ActuatorModule
  BrainModule <|-- NeuralNetworkModule
  BrainModule <|-- CpgModule
  BrainModule <|-- GaitModule
  BrainModule <|-- BalanceModule
  BrainModule <|-- BipedGaitModule
  BrainModule <|-- StateMachineModule
  BrainModule <|-- MuscleControlModule
  MuscleControlModule <|-- MuscleStandModule
  MuscleControlModule <|-- MuscleGaitModule
  MuscleControlModule *-- MuscleGeometry : ramiona sił
  BrainModule <|-- ApproachTargetModule
  BrainModule <|-- AvoidAndSeekModule
  BrainModule <|-- RouterModule
  BrainModule <|-- ConstantModule
  BrainModule <|-- CompositeModule
  BrainModule <|-- SubgraphInputModule
  BrainModule <|-- SubgraphOutputModule
  ITrainableModule <|.. NeuralNetworkModule
  ITrainableModule <|.. CpgModule
  ITrainableModule <|.. GaitModule
  ITrainableModule <|.. BalanceModule
  ITrainableModule <|.. BipedGaitModule
  ITrainableModule <|.. MuscleStandModule
  ITrainableModule <|.. MuscleGaitModule
  StateMachineModule "1" *-- "*" StateTransition
  StateTransition ..> SensorExpression : warunek
  TrainableModules ..> ITrainableModule
  NeuralNetworkModule *-- NeuralNetwork
  NeuralNetworkModule "1" *-- "*" NeuralInput
  NeuralNetworkModule "1" *-- "*" NeuralOutput
  NeuralInput ..> SensorExpression
  CompositeModule *-- BrainGraph : Inner
  CompositeModule *-- SubgraphInputModule
  CompositeModule *-- SubgraphOutputModule
  BrainGraphEditing ..> BrainGraph
  BrainGraph ..> BrainException : błąd kompilacji i działania
```

*Automat stanów (`StateMachineModule`) ma dla każdego stanu osobne wejścia „stan.port” (np. „Stoję.Pitch3”, „Idę.Pitch3”) i jedno wyjście na port. Przejścia to warunki nad portami warunków (np. `Found * Gap > 0.8`); wyjście miesza stany płynnie przez `BlendSeconds`, a nowe przejście czeka `MinDwellSeconds`. Humanoid ma tak dwie sieci: stania i chodu.*

*Graf kompiluje się leniwie: sortowanie topologiczne, porty, cykle, jedno wejście na port, każdy napęd sterowany raz. Błąd niesie Id winnego modułu, więc Studio może go podświetlić.*

<a id="stany"></a>

## Stany modułów i snapshoty

Snapshot zapisuje stan modułów, nigdy strukturę grafu. Każdy moduł umie zamienić się w stan i z niego odtworzyć.

```mermaid
classDiagram
  direction TB
  class ModuleState {
    <<abstract record>>
    +ToJson()
    +SameAs(other)
    +CreateModule(id)
  }
  class BrainSnapshot {
    +Id
    +Label
    +CreatedUtc
    +Modules
  }
  class ModuleSnapshot {
    +ModuleId
    +ModuleName
    +State
  }
  class SnapshotHistory {
    +Capture()
    +StepBack()
    +Forget()
  }
  class Brain {
    +Capture()
    +CaptureIfChanged()
    +Restore() atomowe
    +Matches()
    +CurrentSnapshot()
    +Clear()
  }
  ModuleState <|-- NeuralNetworkState
  ModuleState <|-- CpgState
  ModuleState <|-- GaitState
  ModuleState <|-- BalanceState
  ModuleState <|-- BipedGaitState
  ModuleState <|-- StateMachineState
  ModuleState <|-- MuscleStandState
  ModuleState <|-- MuscleGaitState
  ModuleState <|-- ApproachTargetState
  ModuleState <|-- AvoidAndSeekState
  ModuleState <|-- ConstantState
  ModuleState <|-- RouterState
  ModuleState <|-- CompositeState
  CompositeState "1" *-- "*" ModuleSnapshot : wnętrze po Id
  BrainSnapshot "1" *-- "*" ModuleSnapshot
  ModuleSnapshot *-- ModuleState
  Brain "1" *-- "*" BrainSnapshot
  SnapshotHistory ..> Brain
  BrainModule ..> ModuleState : CaptureState / RestoreState
```

*Nowy typ modułu to rekord stanu z `CreateModule` i wpis `JsonDerivedType`. `Restore` jest atomowe: stan, który nie pasuje do grafu, niczego nie zmienia.*

<a id="tick"></a>

## Jeden tick

Od klatki okna do kroku fizyki. Fazy idą po kolei dla wszystkich encji naraz, więc kolejność na liście nie ma znaczenia.

```mermaid
flowchart LR
  T["StudioWindow<br/>zegar ~33 ms"] -->|"czas klatki"| S["StudioSession.Tick<br/>kroki = czas / (1/30 s) × Speed<br/>najwyżej 8"]
  S -->|"Poll: mistrzowie,<br/>restart przy zmianie"| TC["TrainingController"]
  S -->|"każdy krok"| U["World.Update(1/30 s)"]
  subgraph U2 ["World.Update"]
    direction LR
    Th["Think<br/>Brain.Think: zmysły → moduły"] --> Ac["Act<br/>ActuatorModule.Commit → Actuator.Apply"]
    Ac --> Be["BeforePhysicsStep<br/>przestawienia, cele serw, łuski"]
    Be --> St["PhysicsWorld.Step<br/>Bepu, 4 podkroki, 1 wątek"]
    St --> Af["AfterPhysicsStep<br/>pozy części → Body"]
    Af --> Ti["Time += Δt"] --> Fl["odłożone Add / Remove"]
  end
  U --> Th
```

*Błąd mózgu (`BrainException`) pauzuje scenę i zostawia Id modułu do podświetlenia.*

<a id="nauka"></a>

## Nauka w tle

Stwór w scenie sam się nie uczy. Ewolucja liczy w ukrytych światach, a scena dostaje tylko wagi kolejnych mistrzów.

```mermaid
classDiagram
  direction TB
  class TrainingController {
    +Start(creature)
    +Stop(brain)
    +Poll()
    +RandomizeAndRestart()
    +Paused
  }
  class BackgroundTrainer {
    +Start()
    +Stop()
    +Paused
    +TryGetProgress()
  }
  class Evolution {
    +Step(fitness)
    +Best
    +Generation
  }
  class CmaEs {
    +Step(fitness, gen)
    +Best
    +Sigma
  }
  class EvolutionOptions {
    +Algorithm Genetic|CmaEs
    +PopulationSize 48
    +EliteCount 6
    +MutationSigma
    +MutationScales
    +MaxParallelism
  }
  class SeekTargetTask {
    +Evaluate(params, gen)
    +Validate(params)
    +Run()
  }
  class SeekTargetOptions {
    +EpisodeSeconds
    +Min/MaxDistance
    +Obstacles
    +Slabs
    +Weights
  }
  class SeekRig {
    +Name
    +CreateCreature
    +Effort
    +DefaultOptions
    +PrepareWorld
    +Posture
    +Setup
    +Algorithm
  }
  class SeekRigs {
    <<static>>
    +Disk
    +CarWith(n)
    +SnakeWith(n)
    +ClimbWith(n)
    +Spider
    +HumanoidStand
    +HumanoidWalk
    +MuscleStand
    +MuscleWalk
    +Generic()
    +For(creature, module)
  }
  class TrainingProgress {
    +Generation
    +Champion
    +ChampionScore
  }
  TrainingController "1" *-- "*" BackgroundTrainer : jedna nauka na moduł
  BackgroundTrainer *-- Evolution
  Evolution *-- EvolutionOptions
  Evolution *-- CmaEs : gdy Algorithm = CmaEs
  BackgroundTrainer ..> SeekTargetTask : fitness i walidacja
  BackgroundTrainer ..> TrainingProgress
  SeekTargetTask *-- SeekRig
  SeekTargetTask *-- SeekTargetOptions
  SeekRigs ..> SeekRig
  SeekTargetTask ..> World : nowy świat na każdą próbę
  TrainingController ..> SeekRigs : For(creature)
  TrainingController ..> ITrainableModule : SetParameters(mistrz)
```

*`SeekRigs.For` kopiuje do ciał w próbach ustawienia zmysłów i napędów stwora (bez celów).*

```mermaid
sequenceDiagram
  participant UI as Wątek UI
  participant TC as TrainingController
  participant BT as BackgroundTrainer (wątek)
  participant EV as Evolution
  participant ST as SeekTargetTask
  UI->>TC: Start(stwór)
  TC->>TC: SeekRigs.For, snapshot „przed nauką”
  TC->>BT: Start()
  loop każde pokolenie
    BT->>EV: Step(fitness)
    EV->>ST: Evaluate × 48 (równolegle, rdzenie − 1)
    ST-->>EV: koszt z prób w ukrytych światach
    BT->>ST: Validate(zwycięzca)
    BT-->>BT: lepszy na walidacji → mistrz
  end
  loop co klatkę
    UI->>TC: Poll()
    TC->>TC: nowy mistrz → SetParameters
    TC->>TC: co ~1/3 s: warunki inne → Stop + Start
  end
  UI->>TC: Stop(mózg)
  TC->>TC: mistrz → snapshot
```

*Warunki nauki to rig, konfiguracja uczonego modułu bez parametrów i ustawienia gniazd bez celów.*

<a id="zapis"></a>

## Zapis świata i mózgu

Dwa formaty JSON: świat (`*.animata.json`, format 5) i sam mózg (`*.brain.json`, format 2). Odczyt świata idzie przez `Spawn`.

```mermaid
classDiagram
  direction LR
  class WorldFile {
    <<static>>
    +Capture(world)
    +Restore(doc)
    +ToJson()
    +FromJson()
    +CaptureEntities()
    +RestoreCopies()
  }
  class WorldFileMigration {
    <<static>>
    3 → 4 → 5
  }
  class WorldDocument {
    +Format 5
    +Name
    +Time
    +Entities
  }
  class EntityDocument {
    +Id
    +Type
    +Name
    +Position
    +Rotation
    +Settings
    +Sensors
    +Actuators
    +Parts
    +Brain
  }
  class BrainDocument {
    +Modules
    +Connections
    +Positions
    +Snapshots
    +Current
  }
  class ModuleDocument {
    <<abstract>>
  }
  class StateNode {
    +Id
    +Name
    +State
  }
  class CompositeNode {
    +Id
    +Name
    +InputId
    +OutputId
    +Ports
    +Inner
  }
  class BrainFile {
    <<static>>
    +Capture()
    +Load(brain, doc)
  }
  class BrainFileDocument {
    +Format 2
    +Name
    +Body
    +Brain
  }
  class BodySlotDocument {
    +Slot
    +Sensor
    +Type
    +Ports
  }
  class BrainLoadReport {
    +MissingSlots
    +DroppedConnections
    +Fits
  }
  WorldFile ..> WorldDocument
  WorldFile ..> WorldFileMigration : przy odczycie
  WorldFile ..> Spawn : RestoreEntity
  WorldDocument "1" *-- "*" EntityDocument
  EntityDocument *-- BrainDocument
  BrainDocument "1" *-- "*" ModuleDocument
  BrainDocument "1" *-- "*" BrainSnapshot
  ModuleDocument <|-- StateNode
  ModuleDocument <|-- CompositeNode
  StateNode *-- ModuleState
  CompositeNode *-- BrainDocument : Inner
  BrainFile ..> BrainFileDocument
  BrainFile ..> BrainLoadReport
  BrainFileDocument "1" *-- "*" BodySlotDocument
  BrainFileDocument *-- BrainDocument
```

*Węzły gniazd ciała nie trafiają do pliku; po odczycie odtwarza je `SyncBody`. Stan chwilowy (faza CPG, prędkości, pamięć sterowników) się nie zapisuje.*

<a id="ui"></a>

## Klasy UI: panele, kontrolki, renderer

Budowa w kodzie, bez XAML. Panel to jeden poziom nawigacji wgłąb; kontrolki rysowane ręcznie przemalowują się po zmianie motywu.

```mermaid
classDiagram
  direction TB
  class StudioPanel {
    <<abstract>>
    +Title
    +Build()
    +Refresh(dt)
    +HandleKey()
  }
  class ThemedControl {
    <<abstract>>
  }
  class SceneRenderer {
    +View
    +Sync(world)
    +Select()
    +ContextRequested
  }
  class ModuleInspector {
    +Changed
    +EnterRequested
  }
  class SettingsEditor {
    <<static>>
    +Rows()
    +SlotRows()
  }
  class PanelParts {
    <<static>>
    +EntityPicker()
    +ColorPicker()
    +BrainMenuItems()
  }
  StudioPanel <|-- MenuPanel
  StudioPanel <|-- SimulationPanel
  StudioPanel <|-- CreaturePanel
  StudioPanel <|-- GraphPanel
  StudioPanel <|-- NetworkPanel
  ThemedControl <|-- GraphCanvas
  ThemedControl <|-- NetworkView
  ThemedControl <|-- BodyDiagram
  ThemedControl <|-- SceneMiniMap
  ThemedControl <|-- Sparkline
  ThemedControl <|-- Gauge
  ThemedControl <|-- Compass
  ThemedControl <|-- WeightHeatmap
  SimulationPanel *-- SceneRenderer
  SceneRenderer *-- FlyCameraController
  SceneRenderer *-- WhiskerRenderer
  SceneRenderer ..> ScenePicker : promień, rzut, trafienia
  SceneRenderer ..> SceneMeshes
  GraphPanel *-- GraphCanvas
  GraphPanel *-- ModuleInspector
  NetworkPanel *-- NetworkView
  CreaturePanel *-- BodyDiagram
  BodyDiagram ..> PartSketch
  SceneMiniMap ..> PartSketch
  SimulationPanel ..> SettingsEditor
  CreaturePanel ..> SettingsEditor
  ModuleInspector ..> SettingsEditor
  MenuPanel *-- SceneMiniMap : miniatura sceny
```

*Ścieżka nawigacji: Menu → Scena → stwór → Mózg → podgraf… albo sieć. Pasek nawigacji należy do okna, więc w przejściu lecą tylko panele.*

---

161 typów w 4 projektach produkcyjnych (bez testów). Diagramy pomijają rekordy pomocnicze (`SeekEpisode`, `SlabSpec`, `ObstacleSpec`, `EpisodeResult`, `BrainContext`) i klasy narzędziowe UI (`Ui`, `Icons`, `Dialogs`, `WorldFiles`, `BrainFiles`, `StudioTheme`).
