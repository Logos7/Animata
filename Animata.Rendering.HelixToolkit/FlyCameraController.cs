using System.Numerics;
using Avalonia;
using Avalonia.Input;
using Avalonia.Interactivity;
using HelixToolkit.Avalonia.SharpDX;

namespace Animata.Rendering.HelixToolkit;

/// <summary>
/// Kamera „latająca”: W/S/A/D/Q/E przesuwa, kółko myszy to samo co W/S (ząbek = krótki odcinek lotu),
/// PPM + ruch myszy obraca. PPM kliknięty bez ruchu to nie obrót, tylko <see cref="ContextClicked"/> (menu podręczne).
/// Klawisze ruchu łapie na viewporcie w fazie tunnel, więc okno ich nie dostaje. Klawisze wciśnięte z Ctrl/Alt/Meta
/// kamera przepuszcza — to skróty okna (np. Ctrl+S). Esc łapie tylko w trakcie obracania; poza nim Esc cofa panel.
/// </summary>
internal sealed class FlyCameraController : IDisposable
{
    private const KeyModifiers ShortcutModifiers = KeyModifiers.Control | KeyModifiers.Alt | KeyModifiers.Meta;

    /// <summary>Prędkość lotu (m/s) — ta sama dla klawiszy i kółka.</summary>
    private const float Speed = 16;

    /// <summary>Ząbek kółka = tyle, ile przelatuje się, trzymając W przez 1/8 s.</summary>
    private const float WheelStep = Speed / 8;

    /// <summary>Ruch myszy (px) z wciśniętym PPM, od którego to już obracanie, a nie klik.</summary>
    private const double DragThreshold = 4;

    private readonly Viewport3DX _viewport;
    private readonly PerspectiveCamera _camera;
    private readonly HashSet<Key> _keys = [];
    private IPointer? _pointer;
    private Point _pressPoint;
    private Point _lastPoint;
    private bool _rotating;
    private float _wheelTravel;
    private float _yaw;
    private float _pitch = -MathF.Atan2(21, 27);

    public FlyCameraController(Viewport3DX aViewport, PerspectiveCamera aCamera)
    {
        _viewport = aViewport;
        _camera = aCamera;
        _viewport.Focusable = true;
        _viewport.AddHandler(InputElement.PointerPressedEvent, OnPointerPressed, RoutingStrategies.Tunnel, true);
        _viewport.AddHandler(InputElement.PointerReleasedEvent, OnPointerReleased, RoutingStrategies.Tunnel, true);
        _viewport.AddHandler(InputElement.PointerMovedEvent, OnPointerMoved, RoutingStrategies.Tunnel, true);
        _viewport.AddHandler(InputElement.PointerWheelChangedEvent, OnPointerWheel, RoutingStrategies.Tunnel, true);
        _viewport.AddHandler(InputElement.KeyDownEvent, OnKeyDown, RoutingStrategies.Tunnel, true);
        _viewport.AddHandler(InputElement.KeyUpEvent, OnKeyUp, RoutingStrategies.Tunnel, true);
        _viewport.PointerCaptureLost += OnPointerCaptureLost;
        _viewport.LostFocus += OnLostFocus;
    }

    /// <summary>PPM kliknięty bez obracania — punkt w pikselach viewportu.</summary>
    public event Action<Point>? ContextClicked;

    public void Update(float aDelta)
    {
        if (aDelta <= 0 || (_keys.Count == 0 && _wheelTravel == 0))
            return;

        var forward = Forward();
        var right = new Vector3(-MathF.Cos(_yaw), MathF.Sin(_yaw), 0);
        var movement = Vector3.Zero;
        if (_keys.Contains(Key.W)) movement += forward;
        if (_keys.Contains(Key.S)) movement -= forward;
        if (_keys.Contains(Key.D)) movement += right;
        if (_keys.Contains(Key.A)) movement -= right;
        if (_keys.Contains(Key.E)) movement += Vector3.UnitZ;
        if (_keys.Contains(Key.Q)) movement -= Vector3.UnitZ;

        if (movement != Vector3.Zero)
            _camera.Position += Vector3.Normalize(movement) * Speed * aDelta;

        // Kółko: ten sam kierunek co W/S, rozłożone na kilka klatek, żeby lot był płynny, a nie skokowy.
        if (_wheelTravel != 0)
        {
            var travel = _wheelTravel * MathF.Min(1, aDelta * 12);
            if (MathF.Abs(_wheelTravel - travel) < 1e-3f)
                travel = _wheelTravel;
            _camera.Position += forward * travel;
            _wheelTravel -= travel;
        }
    }

    public void Dispose()
    {
        ReleasePointer();
        _viewport.RemoveHandler(InputElement.PointerPressedEvent, OnPointerPressed);
        _viewport.RemoveHandler(InputElement.PointerReleasedEvent, OnPointerReleased);
        _viewport.RemoveHandler(InputElement.PointerMovedEvent, OnPointerMoved);
        _viewport.RemoveHandler(InputElement.PointerWheelChangedEvent, OnPointerWheel);
        _viewport.RemoveHandler(InputElement.KeyDownEvent, OnKeyDown);
        _viewport.RemoveHandler(InputElement.KeyUpEvent, OnKeyUp);
        _viewport.PointerCaptureLost -= OnPointerCaptureLost;
        _viewport.LostFocus -= OnLostFocus;
    }

    private void OnPointerPressed(object? aSender, PointerPressedEventArgs aEvent)
    {
        _viewport.Focus();
        if (aEvent.GetCurrentPoint(_viewport).Properties.PointerUpdateKind != PointerUpdateKind.RightButtonPressed)
            return;

        _pressPoint = _lastPoint = aEvent.GetPosition(_viewport);
        _rotating = false;
        _pointer = aEvent.Pointer;
        _pointer.Capture(_viewport);
        aEvent.Handled = true;
    }

    private void OnPointerReleased(object? aSender, PointerReleasedEventArgs aEvent)
    {
        if (_pointer != aEvent.Pointer ||
            aEvent.GetCurrentPoint(_viewport).Properties.PointerUpdateKind != PointerUpdateKind.RightButtonReleased)
            return;

        var click = !_rotating;
        ReleasePointer();
        aEvent.Handled = true;
        if (click)
            ContextClicked?.Invoke(aEvent.GetPosition(_viewport));
    }

    private void OnPointerMoved(object? aSender, PointerEventArgs aEvent)
    {
        if (_pointer != aEvent.Pointer)
            return;

        var point = aEvent.GetPosition(_viewport);
        aEvent.Handled = true;
        if (!_rotating)
        {
            var offset = point - _pressPoint;
            if (Math.Abs(offset.X) + Math.Abs(offset.Y) < DragThreshold)
                return;
            _rotating = true;
            _viewport.Cursor = new Cursor(StandardCursorType.None);
        }

        var delta = point - _lastPoint;
        _lastPoint = point;
        _yaw += (float)delta.X * 0.003f;
        _pitch = Math.Clamp(_pitch - (float)delta.Y * 0.003f, -1.55f, 1.55f);
        _camera.LookDirection = Forward() * 34;
    }

    private void OnPointerWheel(object? aSender, PointerWheelEventArgs aEvent)
    {
        _wheelTravel += (float)aEvent.Delta.Y * WheelStep;
        aEvent.Handled = true;
    }

    private void OnKeyDown(object? aSender, KeyEventArgs aEvent)
    {
        if (aEvent.Key == Key.Escape)
        {
            if (_pointer is null)
                return;
            ReleasePointer();
            aEvent.Handled = true;
            return;
        }

        if (IsMovementKey(aEvent.Key) && (aEvent.KeyModifiers & ShortcutModifiers) == 0)
        {
            _keys.Add(aEvent.Key);
            aEvent.Handled = true;
        }
    }

    private void OnKeyUp(object? aSender, KeyEventArgs aEvent)
    {
        if (_keys.Remove(aEvent.Key))
            aEvent.Handled = true;
    }

    private void OnLostFocus(object? aSender, RoutedEventArgs aEvent)
    {
        _keys.Clear();
        _wheelTravel = 0;
        ReleasePointer();
    }

    private void OnPointerCaptureLost(object? aSender, PointerCaptureLostEventArgs aEvent)
    {
        if (aEvent.Pointer != _pointer || aEvent.Pointer.Captured == _viewport)
            return;

        _pointer = null;
        _rotating = false;
        _viewport.Cursor = Cursor.Default;
    }

    private void ReleasePointer()
    {
        var pointer = _pointer;
        _pointer = null;
        _rotating = false;
        pointer?.Capture(null);
        _viewport.Cursor = Cursor.Default;
    }

    private Vector3 Forward()
    {
        var horizontal = MathF.Cos(_pitch);
        return new Vector3(-MathF.Sin(_yaw) * horizontal, -MathF.Cos(_yaw) * horizontal, MathF.Sin(_pitch));
    }

    private static bool IsMovementKey(Key aKey) => aKey is Key.W or Key.A or Key.S or Key.D or Key.Q or Key.E;
}
