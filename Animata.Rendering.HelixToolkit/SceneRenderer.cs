using System.Numerics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Animata.Core.Entities;
using Animata.Core.Worlds;
using HelixToolkit.Avalonia.SharpDX;
using HelixToolkit.Geometry;
using HelixToolkit.SharpDX;

namespace Animata.Rendering.HelixToolkit;

/// <summary>
/// Widok świata w Helixie: modele encji (synchronizowane co klatkę z <see cref="Sync"/>), wąsy, zaznaczanie
/// i przeciąganie encji lewym przyciskiem myszy, kamera latająca (<see cref="FlyCameraController"/>),
/// klik prawym przyciskiem (bez obracania kamerą) — <see cref="ContextRequested"/>.
/// </summary>
public sealed class SceneRenderer : IDisposable
{
    private readonly DefaultEffectsManager _effects = new();
    private readonly Viewport3DX _viewport;
    private readonly PerspectiveCamera _camera;
    private readonly FlyCameraController _cameraController;
    private readonly WhiskerRenderer _whiskers;
    private readonly Dictionary<Guid, MeshGeometryModel3D> _models = [];
    private readonly Dictionary<Guid, ModelState> _states = [];
    private readonly HashSet<Guid> _current = [];
    private World? _world;
    private Entity? _selected;
    private IPointer? _dragPointer;
    private Vector3 _dragOffset;

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
        _cameraController.ContextClicked += OnContextClicked;
        _whiskers = new WhiskerRenderer(_viewport);
        _viewport.AddHandler(InputElement.PointerPressedEvent, OnPointerPressed, RoutingStrategies.Tunnel, true);
        _viewport.AddHandler(InputElement.PointerMovedEvent, OnPointerMoved, RoutingStrategies.Tunnel, true);
        _viewport.AddHandler(InputElement.PointerReleasedEvent, OnPointerReleased, RoutingStrategies.Tunnel, true);
        _viewport.PointerCaptureLost += OnPointerCaptureLost;

        _viewport.Items.Add(new AmbientLight3D { Color = Avalonia.Media.Color.FromRgb(100, 100, 110) });
        _viewport.Items.Add(new DirectionalLight3D
        {
            Color = Avalonia.Media.Colors.White,
            Direction = new Vector3(-0.5f, 0.7f, -1)
        });

        var ground = new MeshBuilder();
        ground.AddBox(new Vector3(0, 0, -0.08f), 22, 16, 0.12f);
        _viewport.Items.Add(new MeshGeometryModel3D
        {
            Geometry = ground.ToMeshGeometry3D(),
            Material = SceneMeshes.Material(0.20f, 0.27f, 0.33f)
        });
    }

    public Control View => _viewport;
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
        if (_selected is not null && !aWorld.Contains(_selected))
        {
            ReleaseDrag();
            Select(null);
        }

        _current.Clear();
        foreach (var entity in aWorld.Entities)
        {
            if (!_models.TryGetValue(entity.Id, out var model))
            {
                if (!SceneMeshes.CanDraw(entity))
                    continue;
                model = new MeshGeometryModel3D
                {
                    Geometry = SceneMeshes.CreateGeometry(entity),
                    Material = SceneMeshes.MaterialFor(entity, ReferenceEquals(entity, _selected))
                };
                _models.Add(entity.Id, model);
                _viewport.Items.Add(model);
            }

            _current.Add(entity.Id);
            var body = entity.Body;
            var state = new ModelState(body.Position, body.Rotation, body.Scale, SceneMeshes.ShapeOf(entity));

            var hasPrevious = _states.TryGetValue(entity.Id, out var previous);
            if (hasPrevious && previous.Shape != state.Shape)
                model.Geometry = SceneMeshes.CreateGeometry(entity);

            if (!hasPrevious ||
                previous.Position != state.Position || previous.Rotation != state.Rotation || previous.Scale != state.Scale)
                model.Transform = Matrix4x4.CreateScale(body.Scale)
                    * Matrix4x4.CreateFromQuaternion(body.Rotation)
                    * Matrix4x4.CreateTranslation(body.Position);

            _states[entity.Id] = state;
        }

        foreach (var id in _models.Keys.Where(aId => !_current.Contains(aId)).ToArray())
        {
            _viewport.Items.Remove(_models[id]);
            _models.Remove(id);
            _states.Remove(id);
        }

        _whiskers.Sync(aWorld);
    }

    public void UpdateCamera(float aDelta) => _cameraController.Update(aDelta);

    public void Select(Entity? aEntity)
    {
        if (ReferenceEquals(_selected, aEntity))
            return;
        if (_selected is not null && _models.TryGetValue(_selected.Id, out var oldModel))
            oldModel.Material = SceneMeshes.MaterialFor(_selected, false);
        _selected = aEntity;
        if (aEntity is not null && _models.TryGetValue(aEntity.Id, out var model))
            model.Material = SceneMeshes.MaterialFor(aEntity, true);
        SelectionChanged?.Invoke(aEntity);
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
        var selected = _world is not null && TryRay(point, out var origin, out var direction)
            ? ScenePicker.Pick(_world, origin, direction)
            : null;
        Select(selected);
        if (selected is not null && aEvent.ClickCount >= 2)
        {
            ReleaseDrag();
            aEvent.Handled = true;
            EntityActivated?.Invoke(selected);
            return;
        }
        if (selected is not null && TryGroundPoint(point, selected.Body.Position.Z, out var ground))
        {
            _dragOffset = selected.Body.Position - ground;
            _dragPointer = aEvent.Pointer;
            _dragPointer.Capture(_viewport);
        }
        aEvent.Handled = true;
    }

    private void OnPointerMoved(object? aSender, PointerEventArgs aEvent)
    {
        if (_dragPointer != aEvent.Pointer || _selected is null)
            return;
        if (TryGroundPoint(aEvent.GetPosition(_viewport), _selected.Body.Position.Z, out var ground))
            _selected.Body.Position = ground + _dragOffset;
        aEvent.Handled = true;
    }

    private void OnPointerReleased(object? aSender, PointerReleasedEventArgs aEvent)
    {
        if (_dragPointer != aEvent.Pointer ||
            aEvent.GetCurrentPoint(_viewport).Properties.PointerUpdateKind != PointerUpdateKind.LeftButtonReleased)
            return;
        ReleaseDrag();
        aEvent.Handled = true;
    }

    private void OnPointerCaptureLost(object? aSender, PointerCaptureLostEventArgs aEvent)
    {
        if (_dragPointer == aEvent.Pointer && aEvent.Pointer.Captured != _viewport)
            _dragPointer = null;
    }

    private void ReleaseDrag()
    {
        var pointer = _dragPointer;
        _dragPointer = null;
        pointer?.Capture(null);
    }

    private void OnContextClicked(Point aPoint)
    {
        var entity = _world is not null && TryRay(aPoint, out var origin, out var direction)
            ? ScenePicker.Pick(_world, origin, direction)
            : null;
        Select(entity);
        Vector3? ground = TryGroundPoint(aPoint, 0, out var point) ? point : null;
        ContextRequested?.Invoke(new SceneContext(aPoint, ground, entity));
    }

    private bool TryRay(Point aPoint, out Vector3 aOrigin, out Vector3 aDirection) =>
        ScenePicker.TryRay(_camera, _viewport.Bounds.Size, aPoint, out aOrigin, out aDirection);

    private bool TryGroundPoint(Point aPoint, float aHeight, out Vector3 aResult)
    {
        aResult = default;
        return TryRay(aPoint, out var origin, out var direction) &&
            ScenePicker.TryGroundPoint(origin, direction, aHeight, out aResult);
    }

    private readonly record struct ModelState(Vector3 Position, Quaternion Rotation, Vector3 Scale, Vector3 Shape);
}

/// <summary>Klik prawym przyciskiem w scenie: piksel viewportu, punkt podłoża pod kursorem (null — niebo), encja pod kursorem.</summary>
public readonly record struct SceneContext(Point Point, Vector3? Ground, Entity? Entity);
