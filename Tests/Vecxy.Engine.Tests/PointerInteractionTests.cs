using System.Numerics;
using Vecxy.Assets;
using Vecxy.Input;
using Vecxy.Interaction;
using Vecxy.Physics;
using Vecxy.Rendering;
using Vecxy.Scene;
using Xunit;

namespace Vecxy.Engine.Tests;

public sealed class PointerInteractionTests
{
    [Fact]
    public void ColliderDispatchesEnterDownUpClickAndExit()
    {
        var scene = new SceneInstance(new EmptyScene());
        var sceneObject = scene.CreateObject("Target");
        var collider = sceneObject.AddComponent<BoxCollider2D>();
        var recorder = sceneObject.AddComponent<PointerRecorder>();
        scene.Activate();

        var hit = new PhysicsRaycastHit(
            sceneObject,
            collider,
            null,
            Vector3.Zero,
            Vector3.UnitZ,
            1.0f);
        var input = new FakeInput();
        var capture = new FakeCapture();
        var renderer = new FakeRenderer();
        var physics = new FakePhysics { Hit = hit };
        var module = new PointerInteractionModule(
            input,
            capture,
            renderer,
            physics);

        module.OnInitialize();
        module.OnUpdate(0.016f);
        input.LeftPressed = true;
        module.OnUpdate(0.016f);
        input.LeftPressed = false;
        module.OnUpdate(0.016f);
        physics.Hit = null;
        module.OnUpdate(0.016f);

        Assert.Equal(
            ["enter", "down", "up", "click", "exit"],
            recorder.Events);
    }

    [Fact]
    public void UiCapturePreventsPointerDispatch()
    {
        var scene = new SceneInstance(new EmptyScene());
        var sceneObject = scene.CreateObject("Target");
        var collider = sceneObject.AddComponent<BoxCollider>();
        var recorder = sceneObject.AddComponent<PointerRecorder>();
        scene.Activate();

        var input = new FakeInput { LeftPressed = true };
        var capture = new FakeCapture { SuppressMouse = true };
        var renderer = new FakeRenderer();
        var physics = new FakePhysics
        {
            Hit = new PhysicsRaycastHit(
                sceneObject,
                collider,
                null,
                Vector3.Zero,
                Vector3.UnitZ,
                1.0f)
        };
        var module = new PointerInteractionModule(
            input,
            capture,
            renderer,
            physics);

        module.OnInitialize();
        module.OnUpdate(0.016f);

        Assert.Empty(recorder.Events);
        Assert.Equal(0, physics.RaycastCount);
    }

    [Fact]
    public void BoxCollider2DRejectsInvalidDimensions()
    {
        var collider = new BoxCollider2D();

        Assert.Throws<ArgumentOutOfRangeException>(
            () => collider.Size = new Vector2(1.0f, 0.0f));
        Assert.Throws<ArgumentOutOfRangeException>(
            () => collider.Depth = float.NaN);
    }

    [Fact]
    public void BoxCollider2DAutoFitsLocalBoundsProvider()
    {
        var scene = new SceneInstance(new EmptyScene());
        var sceneObject = scene.CreateObject("Bounded sprite");
        sceneObject.AddComponent<StubBounds>();
        var collider = sceneObject.AddComponent<BoxCollider2D>();

        scene.Activate();

        Assert.Equal(new Vector2(6.0f, 8.0f), collider.Size);
        Assert.Equal(new Vector3(1.0f, 1.0f, 0.0f), collider.Center);
    }

    private sealed class EmptyScene : IScene;

    private sealed class StubBounds : AComponent, ILocalBoundsProvider
    {
        public Vector3 LocalBoundsMin => new(-2.0f, -3.0f, 0.0f);
        public Vector3 LocalBoundsMax => new(4.0f, 5.0f, 0.0f);
    }

    private sealed class PointerRecorder :
        AComponent,
        IPointerEnterHandler,
        IPointerExitHandler,
        IPointerDownHandler,
        IPointerUpHandler,
        IPointerClickHandler
    {
        public List<string> Events { get; } = [];

        public void OnPointerEnter(in PointerEventData eventData) =>
            Events.Add("enter");

        public void OnPointerExit(in PointerEventData eventData) =>
            Events.Add("exit");

        public void OnPointerDown(in PointerEventData eventData) =>
            Events.Add("down");

        public void OnPointerUp(in PointerEventData eventData) =>
            Events.Add("up");

        public void OnPointerClick(in PointerEventData eventData) =>
            Events.Add("click");
    }

    private sealed class FakeInput : IInputManager
    {
        public bool LeftPressed { get; set; }
        public Vector2 MousePosition { get; set; } = new(100.0f, 100.0f);
        public Vector2 MouseDelta { get; set; }
        public Vector2 MouseWheelDelta => Vector2.Zero;
        public bool IsKeyPressed(EKeyboardKey key) => false;
        public bool IsMouseButtonPressed(EMouseButton button) =>
            button == EMouseButton.Left && LeftPressed;
        public InputMap Create(AssetRef<InputAsset> asset, string mapName) =>
            throw new NotSupportedException();
    }

    private sealed class FakeCapture : IInputCaptureState
    {
        public bool SuppressKeyboard { get; set; }
        public bool SuppressMouse { get; set; }
    }

    private sealed class FakeRenderer : IRenderer
    {
        public RenderingStatistics Statistics { get; } = new();
        public bool Wireframe { get; set; }
        public bool ScenePresentationEnabled { get; set; }
        public nint SceneTextureId => 0;
        public int GameOutputWidth => 450;
        public int GameOutputHeight => 900;
        public GameView CreateGameView(IRenderTarget? target = null) =>
            throw new NotSupportedException();
        public void DestroyGameView(GameView view) =>
            throw new NotSupportedException();
        public void SetSceneViewportSize(int width, int height) { }
        public void SetSceneViewportScreenRect(Vecxy.Kernel.Rect? screenRect) { }
        public Vector2 ScreenToGameOutput(Vector2 screenPosition) => screenPosition;
        public bool TryCreateCameraRay(Vector2 screenPosition, out CameraRay ray)
        {
            ray = new CameraRay(
                screenPosition,
                new Vector3(0.0f, 0.0f, 10.0f),
                -Vector3.UnitZ,
                100.0f);
            return true;
        }
        public Mesh CreateQuad() => throw new NotSupportedException();
    }

    private sealed class FakePhysics : IPhysicsSystem
    {
        public PhysicsRaycastHit? Hit { get; set; }
        public int RaycastCount { get; private set; }
        public PhysicsSettings Settings => new PhysicsConfig().ToSettings();
        public void AddForce(RigidBody body, Vector3 force) =>
            throw new NotSupportedException();
        public void AddImpulse(RigidBody body, Vector3 impulse) =>
            throw new NotSupportedException();
        public void Teleport(RigidBody body, Vector3 position, Quaternion rotation) =>
            throw new NotSupportedException();
        public bool Raycast(
            Vector3 origin,
            Vector3 direction,
            float maxDistance,
            SceneObject? ignoreSceneObject,
            out PhysicsRaycastHit hit)
        {
            RaycastCount++;
            if (Hit is { } current)
            {
                hit = current;
                return true;
            }

            hit = default;
            return false;
        }
    }
}
