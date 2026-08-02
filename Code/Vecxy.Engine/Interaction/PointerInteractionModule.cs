using Autofac;
using Vecxy.Assets;
using Vecxy.Diagnostics;
using Vecxy.Input;
using Vecxy.Kernel;
using Vecxy.Physics;
using Vecxy.Rendering;
using Vecxy.Scene;

namespace Vecxy.Interaction;

public sealed class PointerInteractionModule(
    IInputManager input,
    IInputCaptureState inputCapture,
    IRenderer renderer,
    IPhysicsSystem physics) :
    IModule,
    IModule.IUpdatable
{
    private static readonly EMouseButton[] Buttons =
    [
        EMouseButton.Left,
        EMouseButton.Right,
        EMouseButton.Middle,
        EMouseButton.Button4,
        EMouseButton.Button5,
        EMouseButton.Button6,
        EMouseButton.Button7,
        EMouseButton.Button8
    ];

    public sealed class Definition :
        AModuleDefinition<PointerInteractionModule>
    {
        protected override void RegisterModule(ContainerBuilder builder)
        {
            builder
                .RegisterType<PointerInteractionModule>()
                .AsSelf()
                .SingleInstance();
        }
    }

    private readonly Dictionary<EMouseButton, bool> _wasPressed = [];
    private readonly Dictionary<EMouseButton, PhysicsRaycastHit> _pressed = [];
    private PhysicsRaycastHit? _hovered;
    private CameraRay _lastRay;
    private bool _initialized;

    public void OnInitialize()
    {
        if (_initialized)
            return;

        foreach (var button in Buttons)
            _wasPressed[button] = IsPressed(button);

        _initialized = true;
    }

    public void OnUpdate(float deltaTime)
    {
        if (!_initialized)
            return;

        var suppressed = inputCapture.SuppressMouse;
        PhysicsRaycastHit? hit = null;
        if (!suppressed &&
            renderer.TryCreateCameraRay(input.PointerPosition, out var ray))
        {
            _lastRay = ray;
            if (physics.Raycast(
                    ray.Origin,
                    ray.Direction,
                    ray.MaxDistance,
                    ignoreSceneObject: null,
                    out var raycastHit))
            {
                hit = raycastHit;
            }
        }

        UpdateHover(hit);
        UpdateButtons(hit, suppressed);
    }

    public void OnShutdown()
    {
        _hovered = null;
        _pressed.Clear();
        _wasPressed.Clear();
        _initialized = false;
    }

    public void Dispose()
    {
        OnShutdown();
    }

    private void UpdateHover(PhysicsRaycastHit? hit)
    {
        var previous = _hovered;
        var changed = !SameTarget(previous, hit);
        if (changed && previous is { } exited)
        {
            Dispatch(
                exited,
                EPointerDispatch.Exit,
                EMouseButton.Undefined);
        }

        _hovered = hit;

        if (changed && hit is { } entered)
        {
            Dispatch(
                entered,
                EPointerDispatch.Enter,
                EMouseButton.Undefined);
        }

        if (hit is { } hovered &&
            input.PointerDelta.LengthSquared() > float.Epsilon)
        {
            Dispatch(
                hovered,
                EPointerDispatch.Move,
                EMouseButton.Undefined);
        }
    }

    private void UpdateButtons(
        PhysicsRaycastHit? hit,
        bool suppressed)
    {
        foreach (var button in Buttons)
        {
            var pressedNow = IsPressed(button);
            var wasPressed = _wasPressed.GetValueOrDefault(button);

            if (suppressed)
            {
                if (_pressed.Remove(button, out var cancelled))
                    Dispatch(cancelled, EPointerDispatch.Up, button);

                _wasPressed[button] = pressedNow;
                continue;
            }

            if (pressedNow && !wasPressed && hit is { } downHit)
            {
                _pressed[button] = downHit;
                Dispatch(downHit, EPointerDispatch.Down, button);
            }
            else if (!pressedNow && wasPressed &&
                     _pressed.Remove(button, out var pressedHit))
            {
                Dispatch(pressedHit, EPointerDispatch.Up, button);

                if (SameTarget(pressedHit, hit))
                    Dispatch(pressedHit, EPointerDispatch.Click, button);
            }

            _wasPressed[button] = pressedNow;
        }
    }

    private void Dispatch(
        PhysicsRaycastHit hit,
        EPointerDispatch type,
        EMouseButton button)
    {
        var sceneObject = hit.SceneObject;
        if (sceneObject.IsDestroyed || hit.Collider.IsDestroyed)
            return;

        var eventData = new PointerEventData(
            input.PointerPosition,
            _lastRay,
            hit,
            button,
            input.PointerKind,
            input.Touches.FirstOrDefault(touch => touch.IsPrimary).Id);

        foreach (var component in sceneObject.Components.ToArray())
        {
            if (!component.IsActive)
                continue;

            try
            {
                switch (type)
                {
                    case EPointerDispatch.Enter
                        when component is IPointerEnterHandler handler:
                        handler.OnPointerEnter(eventData);
                        break;

                    case EPointerDispatch.Exit
                        when component is IPointerExitHandler handler:
                        handler.OnPointerExit(eventData);
                        break;

                    case EPointerDispatch.Move
                        when component is IPointerMoveHandler handler:
                        handler.OnPointerMove(eventData);
                        break;

                    case EPointerDispatch.Down
                        when component is IPointerDownHandler handler:
                        handler.OnPointerDown(eventData);
                        break;

                    case EPointerDispatch.Up
                        when component is IPointerUpHandler handler:
                        handler.OnPointerUp(eventData);
                        break;

                    case EPointerDispatch.Click
                        when component is IPointerClickHandler handler:
                        handler.OnPointerClick(eventData);
                        break;
                }
            }
            catch (Exception exception)
            {
                Logger.Error(
                    exception,
                    $"Pointer callback failed on '{component.GetType().Name}'.");
            }
        }
    }

    private bool IsPressed(EMouseButton button) =>
        button == EMouseButton.Left
            ? input.IsPrimaryPointerPressed
            : input.IsMouseButtonPressed(button);

    private static bool SameTarget(
        PhysicsRaycastHit? first,
        PhysicsRaycastHit? second) =>
        first is { } firstHit &&
        second is { } secondHit &&
        ReferenceEquals(firstHit.Collider, secondHit.Collider);

    private static bool SameTarget(
        PhysicsRaycastHit first,
        PhysicsRaycastHit? second) =>
        second is { } secondHit &&
        ReferenceEquals(first.Collider, secondHit.Collider);

    private enum EPointerDispatch : byte
    {
        Enter,
        Exit,
        Move,
        Down,
        Up,
        Click
    }
}
