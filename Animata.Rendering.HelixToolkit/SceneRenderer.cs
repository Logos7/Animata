using System.Numerics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Animata.Core.Bodies;
using Animata.Core.Entities;
using Animata.Core.WorldObjects;
using Animata.Core.Worlds;
using HelixToolkit.Avalonia.SharpDX;
using HelixToolkit.Geometry;
using HelixToolkit.SharpDX;

namespace Animata.Rendering.HelixToolkit;

/// <summary>
/// Widok świata w Helixie: modele encji (synchronizowane co klatkę z <see cref="Sync"/>; stwór z części — model na część,
/// podłoga — zablokowany klocek <see cref="Box"/>), wąsy, zaznaczanie i przeciąganie encji lewym przyciskiem myszy,
/// kamera latająca (<see cref="FlyCameraController"/>), klik prawym przyciskiem (bez obracania kamerą) — <see cref="ContextRequested"/>.
/// Zaznaczenie wielu: ramka (LPM ciągnięty od pustego miejsca), Ctrl+klik przełącza, Shift+klik dokłada;
/// przeciągnięcie zaznaczonej encji przesuwa całe zaznaczenie.
/// </summary>
public sealed class SceneRenderer : IDisposable
{
    private readonly DefaultEffectsManager _effects = new();
    private readonly Viewport3DX _viewport;
    private readonly PerspectiveCamera _camera;
    private readonly FlyCameraController _cameraController;
    private readonly WhiskerRenderer _whiskers;
    private readonly MuscleRenderer _muscles;
    private readonly Dictionary<Guid, MeshGeometryModel3D> _models = [];
    private readonly Dictionary<Guid, ModelState> _states = [];
    private readonly Dictionary<Guid, PartModels> _parts = [];
    private readonly HashSet<Guid> _current = [];
    private World? _world;
    private readonly Avalonia.Controls.Grid _root = new();
    private readonly Avalonia.Controls.Canvas _overlay = new() { IsHitTestVisible = false };
    private readonly Avalonia.Controls.Shapes.Rectangle _band = new()
    {
        Fill = new Avalonia.Media.SolidColorBrush(Avalonia.Media.Color.FromArgb(40, 96, 205, 255)),
        Stroke = new Avalonia.Media.SolidColorBrush(Avalonia.Media.Color.FromArgb(220, 96, 205, 255)),
        StrokeThickness = 1,
        IsVisible = false
    };
    private readonly List<Entity> _selection = [];
    private readonly Dictionary<Entity, Vector3> _dragOffsets = [];
    private Entity? _selected;
    private IPointer? _dragPointer;
    private float _dragPlane;
    private IPointer? _bandPointer;
    private Point _bandStart;
    private List<Entity> _bandBase = [];
    private Point? _pointer;

    public SceneRenderer()
    {
        _camera = new PerspectiveCamera
        {
            Position = new Vector3(0, 27, 21),
            LookDirection = new Vector3(0, -27, -21),
            UpDirection = Vector3.UnitZ,
            FarPlaneDistance = 250
        };

        _viewport = new Viewport3DX
        {
            EffectsManager = _effects,
            BackgroundColor = Avalonia.Media.Color.FromRgb(24, 30, 45),
            Camera = _camera,
            ModelUpDirection = Vector3.UnitZ,
            IsRotationEnabled = false,
            IsPanEnabled = false,
            IsZoomEnabled = false,
            IsMoveEnabled = false
        };
        _cameraController = new FlyCameraController(_viewport, _camera);
        _overlay.Children.Add(_band);
        _root.Children.Add(_viewport);
        _root.Children.Add(_overlay);
        _root.ClipToBounds = true;
        _cameraController.ContextClicked += OnContextClicked;
        _whiskers = new WhiskerRenderer(_viewport);
        _muscles = new MuscleRenderer(_viewport);
        _viewport.AddHandler(InputElement.PointerPressedEvent, OnPointerPressed, RoutingStrategies.Tunnel, true);
        _viewport.AddHandler(InputElement.PointerMovedEvent, OnPointerMoved, RoutingStrategies.Tunnel, true);
        _viewport.AddHandler(InputElement.PointerReleasedEvent, OnPointerReleased, RoutingStrategies.Tunnel, true);
        _viewport.PointerCaptureLost += OnPointerCaptureLost;
        _viewport.PointerExited += OnPointerExited;

        _viewport.Items.Add(new AmbientLight3D { Color = Avalonia.Media.Color.FromRgb(100, 100, 110) });
        _viewport.Items.Add(new DirectionalLight3D
        {
            Color = Avalonia.Media.Colors.White,
            Direction = new Vector3(-0.5f, 0.7f, -1)
        });

    }

    /// <summary>Widok do wstawienia w UI: viewport Helixa z nakładką na ramkę zaznaczenia.</summary>
    public Control View => _root;

    /// <summary>Wszystkie zaznaczone encje (główna — <see cref="SelectedEntity"/> — też tu jest).</summary>
    public IReadOnlyList<Entity> Selection => _selection;

    /// <summary>Punkt podłoża (z = 0) pod kursorem, gdy kursor jest nad widokiem; inaczej null.</summary>
    public Vector3? GroundUnderPointer => _pointer is { } point && TryGroundPoint(point, 0, out var ground) ? ground : null;

    /// <summary>
    /// Wysokość terenu dla przeciąganej encji w punkcie (np. przyciąganie do klocków); null — przeciąganie
    /// zostawia wysokość bez zmian.
    /// </summary>
    public Func<Entity, Vector3, float>? GroundHeight { get; set; }
    public Entity? SelectedEntity => _selected;
    public bool IsDragging => _dragPointer is not null;

    /// <summary>Zaznaczenie zmieniło się (kliknięciem albo przez <see cref="Select"/>).</summary>
    public event Action<Entity?>? SelectionChanged;

    /// <summary>Dwuklik na encji — „wejdź w to”.</summary>
    public event Action<Entity>? EntityActivated;

    /// <summary>
    /// Klik prawym przyciskiem w widoku (menu podręczne). Encja pod kursorem jest już zaznaczona, zanim przyjdzie zdarzenie.
    /// </summary>
    public event Action<SceneContext>? ContextRequested;

    /// <summary>Punkt widoku (piksele viewportu), w którym widać środek encji; false, gdy jest za kamerą.</summary>
    public bool TryProject(Entity aEntity, out Point aPoint) =>
        ScenePicker.TryProject(_camera, _viewport.Bounds.Size, aEntity.Body.Position + Vector3.UnitZ * 0.2f, out aPoint);

    public void Sync(World aWorld)
    {
        _world = aWorld;
        if (_selection.Any(aEntity => !aWorld.Contains(aEntity)))
        {
            ReleaseDrag();
            SetSelection(_selection.Where(aWorld.Contains).ToList(),
                _selected is not null && aWorld.Contains(_selected) ? _selected : null);
        }

        _current.Clear();
        foreach (var entity in aWorld.Entities)
        {
            if (entity is ArticulatedCreature articulated)
            {
                SyncParts(articulated);
                _current.Add(entity.Id);
                continue;
            }

            if (!_models.TryGetValue(entity.Id, out var model))
            {
                if (!SceneMeshes.CanDraw(entity))
                    continue;
                model = new MeshGeometryModel3D
                {
                    Geometry = SceneMeshes.CreateGeometry(entity),
                    Material = SceneMeshes.MaterialFor(entity, _selection.Contains(entity))
                };
                _models.Add(entity.Id, model);
                _viewport.Items.Add(model);
            }

            _current.Add(entity.Id);
            var body = entity.Body;
            var state = new ModelState(body.Position, body.Rotation, SceneMeshes.ShapeOf(entity), SceneMeshes.ColorOf(entity));

            var hasPrevious = _states.TryGetValue(entity.Id, out var previous);
            if (hasPrevious && previous.Shape != state.Shape)
                model.Geometry = SceneMeshes.CreateGeometry(entity);
            if (hasPrevious && previous.Color != state.Color)
                model.Material = SceneMeshes.MaterialFor(entity, _selection.Contains(entity));

            if (!hasPrevious || previous.Position != state.Position || previous.Rotation != state.Rotation)
                model.Transform = Matrix4x4.CreateFromQuaternion(body.Rotation) * Matrix4x4.CreateTranslation(body.Position);

            _states[entity.Id] = state;
        }

        foreach (var id in _models.Keys.Where(aId => !_current.Contains(aId)).ToArray())
        {
            _viewport.Items.Remove(_models[id]);
            _models.Remove(id);
            _states.Remove(id);
        }
        foreach (var id in _parts.Keys.Where(aId => !_current.Contains(aId)).ToArray())
        {
            foreach (var model in _parts[id].Models)
                _viewport.Items.Remove(model);
            _parts.Remove(id);
        }

        _whiskers.Sync(aWorld);
        _muscles.Sync(aWorld);
    }

    public void UpdateCamera(float aDelta) => _cameraController.Update(aDelta);

    /// <summary>Zaznacza tylko tę encję (null — nic).</summary>
    public void Select(Entity? aEntity) => SetSelection(aEntity is null ? [] : [aEntity], aEntity);

    /// <summary>Zaznacza podane encje; główna (do właściwości) — <paramref name="aPrimary"/> albo ostatnia z listy.</summary>
    public void SelectMany(IEnumerable<Entity> aEntities, Entity? aPrimary = null)
    {
        var entities = aEntities.Distinct().ToList();
        SetSelection(entities, aPrimary is not null && entities.Contains(aPrimary) ? aPrimary : entities.LastOrDefault());
    }

    /// <summary>Dokłada encję do zaznaczenia albo ją z niego zdejmuje (Ctrl+klik).</summary>
    public void Toggle(Entity aEntity)
    {
        var entities = _selection.ToList();
        if (!entities.Remove(aEntity))
            entities.Add(aEntity);
        SetSelection(entities, entities.Contains(aEntity) ? aEntity : entities.LastOrDefault());
    }

    private void SetSelection(List<Entity> aEntities, Entity? aPrimary)
    {
        if (aEntities.SequenceEqual(_selection) && ReferenceEquals(aPrimary, _selected))
            return;
        foreach (var old in _selection.Where(aOld => !aEntities.Contains(aOld)).ToList())
            Paint(old, false);
        _selection.Clear();
        _selection.AddRange(aEntities);
        foreach (var entity in _selection)
            Paint(entity, true);
        _selected = aPrimary;
        SelectionChanged?.Invoke(aPrimary);
    }

    /// <summary>Punkt podłoża (z = 0) pod środkiem widoku, a gdy kamera patrzy w niebo — początek układu.</summary>
    public Vector3 GroundPointAtCenter() =>
        TryGroundPoint(new Point(_viewport.Bounds.Width / 2, _viewport.Bounds.Height / 2), 0, out var point)
            ? point
            : Vector3.Zero;

    public void Dispose()
    {
        ReleaseDrag();
        _viewport.RemoveHandler(InputElement.PointerPressedEvent, OnPointerPressed);
        _viewport.RemoveHandler(InputElement.PointerMovedEvent, OnPointerMoved);
        _viewport.RemoveHandler(InputElement.PointerReleasedEvent, OnPointerReleased);
        _viewport.PointerCaptureLost -= OnPointerCaptureLost;
        _viewport.PointerExited -= OnPointerExited;
        _cameraController.ContextClicked -= OnContextClicked;
        _cameraController.Dispose();
        _viewport.Dispose();
        _effects.Dispose();
    }

    // ---------- zaznaczanie i przeciąganie ----------

    private void OnPointerPressed(object? aSender, PointerPressedEventArgs aEvent)
    {
        if (aEvent.GetCurrentPoint(_viewport).Properties.PointerUpdateKind != PointerUpdateKind.LeftButtonPressed)
            return;

        _viewport.Focus();
        var point = aEvent.GetPosition(_viewport);
        var control = (aEvent.KeyModifiers & (KeyModifiers.Control | KeyModifiers.Meta)) != 0;
        var shift = (aEvent.KeyModifiers & KeyModifiers.Shift) != 0;
        var hit = _world is not null && TryRay(point, out var origin, out var direction)
            ? ScenePicker.Pick(_world, origin, direction)
            : null;
        aEvent.Handled = true;

        if (hit is null || hit.Locked)
        {
            // Pusto albo encja zablokowana (np. podłoga) — zablokowana stoi jak tło: LPM jej nie zaznacza (także z Ctrl/Shift
            // i dwuklikiem), klik czyści zaznaczenie, a przeciągnięcie robi ramkę (z Ctrl/Shift dokłada do obecnego).
            // Zaznaczyć ją można z listy encji albo z menu PPM (tam też „Odblokuj”).
            _bandBase = control || shift ? _selection.ToList() : [];
            if (!control && !shift)
                Select(null);
            _bandStart = point;
            _bandPointer = aEvent.Pointer;
            _bandPointer.Capture(_viewport);
            return;
        }

        if (control)
        {
            Toggle(hit);
            return;
        }
        if (shift)
            SelectMany(_selection.Append(hit), hit);
        else if (_selection.Contains(hit))
            SetSelection(_selection.ToList(), hit);
        else
            Select(hit);

        if (aEvent.ClickCount >= 2)
        {
            ReleaseDrag();
            EntityActivated?.Invoke(hit);
            return;
        }
        if (TryGroundPoint(point, hit.Body.Position.Z, out var ground))
        {
            // Płaszczyzna przeciągania na wysokości z chwili chwycenia — wysokość z terenu nie może jej przesuwać.
            // Przesuwa się całe zaznaczenie (bez encji zablokowanych), każda encja z własnym przesunięciem względem kursora.
            _dragPlane = hit.Body.Position.Z;
            _dragOffsets.Clear();
            foreach (var entity in _selection.Where(aEntity => !aEntity.Locked))
                _dragOffsets[entity] = entity.Body.Position - ground;
            _dragPointer = aEvent.Pointer;
            _dragPointer.Capture(_viewport);
        }
    }

    private void OnPointerMoved(object? aSender, PointerEventArgs aEvent)
    {
        var point = aEvent.GetPosition(_viewport);
        _pointer = point;
        if (_bandPointer == aEvent.Pointer)
        {
            UpdateBand(point);
            aEvent.Handled = true;
            return;
        }
        if (_dragPointer != aEvent.Pointer || _dragOffsets.Count == 0)
            return;
        if (TryGroundPoint(point, _dragPlane, out var ground))
            foreach (var (entity, offset) in _dragOffsets)
            {
                var position = ground + offset;
                if (GroundHeight is { } height)
                    position.Z = height(entity, position);
                entity.Body.Position = position;
            }
        aEvent.Handled = true;
    }

    private void OnPointerReleased(object? aSender, PointerReleasedEventArgs aEvent)
    {
        if (aEvent.GetCurrentPoint(_viewport).Properties.PointerUpdateKind != PointerUpdateKind.LeftButtonReleased)
            return;
        if (_bandPointer == aEvent.Pointer)
        {
            UpdateBand(aEvent.GetPosition(_viewport));
            ReleaseBand();
            aEvent.Handled = true;
            return;
        }
        if (_dragPointer != aEvent.Pointer)
            return;
        ReleaseDrag();
        aEvent.Handled = true;
    }

    private void OnPointerCaptureLost(object? aSender, PointerCaptureLostEventArgs aEvent)
    {
        if (_dragPointer == aEvent.Pointer && aEvent.Pointer.Captured != _viewport)
        {
            _dragPointer = null;
            _dragOffsets.Clear();
        }
        if (_bandPointer == aEvent.Pointer && aEvent.Pointer.Captured != _viewport)
        {
            _bandPointer = null;
            _band.IsVisible = false;
        }
    }

    private void OnPointerExited(object? aSender, PointerEventArgs aEvent)
    {
        if (_bandPointer is null && _dragPointer is null)
            _pointer = null;
    }

    /// <summary>Ramka od punktu startu do kursora: zaznaczone = encje (poza zablokowanymi), których środek widać w ramce.</summary>
    private void UpdateBand(Point aPoint)
    {
        var rect = new Avalonia.Rect(Math.Min(_bandStart.X, aPoint.X), Math.Min(_bandStart.Y, aPoint.Y),
            Math.Abs(aPoint.X - _bandStart.X), Math.Abs(aPoint.Y - _bandStart.Y));
        if (rect.Width < 4 && rect.Height < 4)
        {
            _band.IsVisible = false;
            return;
        }
        _band.IsVisible = true;
        Avalonia.Controls.Canvas.SetLeft(_band, rect.X);
        Avalonia.Controls.Canvas.SetTop(_band, rect.Y);
        _band.Width = rect.Width;
        _band.Height = rect.Height;
        if (_world is null)
            return;
        var inside = _world.Entities.Where(aEntity => !aEntity.Locked &&
            ScenePicker.TryProject(_camera, _viewport.Bounds.Size, CenterOf(aEntity), out var screen) && rect.Contains(screen));
        SelectMany(_bandBase.Concat(inside));
    }

    private void ReleaseBand()
    {
        var pointer = _bandPointer;
        _bandPointer = null;
        _band.IsVisible = false;
        pointer?.Capture(null);
    }

    /// <summary>Punkt encji do ramki: środek części stwora z części, środek kuli, podstawa pozostałych.</summary>
    private static Vector3 CenterOf(Entity aEntity) => aEntity switch
    {
        ArticulatedCreature creature when creature.PartPositions.Count > 0 =>
            creature.PartPositions.Aggregate(Vector3.Zero, (aSum, aPart) => aSum + aPart) / creature.PartPositions.Count,
        Sphere sphere => sphere.Body.Position + Vector3.UnitZ * sphere.Radius,
        _ => aEntity.Body.Position + Vector3.UnitZ * 0.1f
    };

    private void ReleaseDrag()
    {
        var pointer = _dragPointer;
        _dragPointer = null;
        _dragOffsets.Clear();
        pointer?.Capture(null);
        ReleaseBand();
    }

    private void OnContextClicked(Point aPoint)
    {
        var entity = _world is not null && TryRay(aPoint, out var origin, out var direction)
            ? ScenePicker.Pick(_world, origin, direction)
            : null;
        // Klik w zaznaczoną encję zostawia całe zaznaczenie (menu działa na wszystkich); w pustkę — też.
        // Zablokowana encja dostaje menu (jest klikalna), ale się nie zaznacza.
        if (entity is { Locked: false } && _selection.Contains(entity))
            SetSelection(_selection.ToList(), entity);
        else if (entity is { Locked: false })
            Select(entity);
        Vector3? ground = TryGroundPoint(aPoint, 0, out var point) ? point : null;
        ContextRequested?.Invoke(new SceneContext(ground, entity));
    }

    private void Paint(Entity aEntity, bool aSelected)
    {
        var material = SceneMeshes.MaterialFor(aEntity, aSelected);
        if (_models.TryGetValue(aEntity.Id, out var model))
            model.Material = material;
        if (_parts.TryGetValue(aEntity.Id, out var parts))
            foreach (var part in parts.Models)
                part.Material = material;
    }

    /// <summary>Modele części stwora: przebudowa przy nowym planie ciała, co klatkę pozy części z fizyki.</summary>
    private void SyncParts(ArticulatedCreature aCreature)
    {
        aCreature.ApplyExternalMove();
        if (_parts.TryGetValue(aCreature.Id, out var painted) && painted.Color != aCreature.Color)
        {
            // Kolor zmieniony we właściwościach: nowe materiały części.
            painted.Color = aCreature.Color;
            Paint(aCreature, _selection.Contains(aCreature));
        }
        if (!_parts.TryGetValue(aCreature.Id, out var parts) || !ReferenceEquals(parts.Plan, aCreature.Plan))
        {
            if (parts is not null)
                foreach (var old in parts.Models)
                    _viewport.Items.Remove(old);
            parts = new PartModels(aCreature.Plan) { Color = aCreature.Color };
            var material = SceneMeshes.MaterialFor(aCreature, _selection.Contains(aCreature));
            for (var index = 0; index < aCreature.Plan.Parts.Count; index++)
            {
                var model = new MeshGeometryModel3D
                {
                    Geometry = SceneMeshes.CreatePartGeometry(aCreature.Plan.Parts[index], index == 0),
                    Material = material
                };
                parts.Models.Add(model);
                _viewport.Items.Add(model);
            }
            _parts[aCreature.Id] = parts;
        }

        var positions = aCreature.PartPositions;
        var orientations = aCreature.PartOrientations;
        for (var index = 0; index < parts.Models.Count && index < positions.Count; index++)
            parts.Models[index].Transform = Matrix4x4.CreateFromQuaternion(orientations[index]) * Matrix4x4.CreateTranslation(positions[index]);
    }

    private sealed class PartModels(BodyPlan aPlan)
    {
        public BodyPlan Plan { get; } = aPlan;
        public Vector3 Color { get; set; }
        public List<MeshGeometryModel3D> Models { get; } = [];
    }

    private bool TryRay(Point aPoint, out Vector3 aOrigin, out Vector3 aDirection) =>
        ScenePicker.TryRay(_camera, _viewport.Bounds.Size, aPoint, out aOrigin, out aDirection);

    private bool TryGroundPoint(Point aPoint, float aHeight, out Vector3 aResult)
    {
        aResult = default;
        return TryRay(aPoint, out var origin, out var direction) &&
            ScenePicker.TryGroundPoint(origin, direction, aHeight, out aResult);
    }

    private readonly record struct ModelState(Vector3 Position, Quaternion Rotation, Vector3 Shape, Vector3 Color);
}

/// <summary>Klik prawym przyciskiem w scenie: punkt podłoża pod kursorem (null — niebo), encja pod kursorem.</summary>
public readonly record struct SceneContext(Vector3? Ground, Entity? Entity);
