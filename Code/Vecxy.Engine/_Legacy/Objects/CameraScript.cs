using System.Numerics;
using Vecxy.Rendering._Legacy;

namespace Vecxy.Engine._Legacy;

public sealed class CameraScript(IInput? input = null) : Script
{
    private float _yaw;
    private float _pitch;

    internal Camera3D RenderCamera { get; } = new();

    public float FieldOfView { get => RenderCamera.FieldOfView; set => RenderCamera.FieldOfView = value; }
    public float NearPlane { get => RenderCamera.NearPlane; set => RenderCamera.NearPlane = value; }
    public float FarPlane { get => RenderCamera.FarPlane; set => RenderCamera.FarPlane = value; }
    public bool FlyControlsEnabled { get; set; } = input is not null;
    public float MoveSpeed { get; set; } = 5f;
    public float FastMultiplier { get; set; } = 3f;
    public float LookSensitivity { get; set; } = 0.0025f;

    public override void OnStart()
    {
        var forward = Transform.Forward;
        _yaw = MathF.Atan2(-forward.X, -forward.Z);
        _pitch = MathF.Asin(Math.Clamp(forward.Y, -1f, 1f));
    }

    public override void OnUpdate(float deltaTime)
    {
        if (!FlyControlsEnabled || input is null) return;

        var looking = input.IsRightMouseDown;
        input.SetMouseCaptured(looking);
        var mouse = input.ConsumeMouseDelta();
        if (looking)
        {
            _yaw += mouse.X * LookSensitivity;
            _pitch = Math.Clamp(_pitch - mouse.Y * LookSensitivity,
                -MathF.PI * 0.495f, MathF.PI * 0.495f);
            Transform.Rotation = Quaternion.CreateFromYawPitchRoll(-_yaw, _pitch, 0f);
        }

        var movement = Vector3.Zero;
        if (input.IsKeyDown(InputKey.W)) movement += Transform.Forward;
        if (input.IsKeyDown(InputKey.S)) movement -= Transform.Forward;
        if (input.IsKeyDown(InputKey.D)) movement += Transform.Right;
        if (input.IsKeyDown(InputKey.A)) movement -= Transform.Right;
        if (input.IsKeyDown(InputKey.E)) movement += Vector3.UnitY;
        if (input.IsKeyDown(InputKey.Q)) movement -= Vector3.UnitY;
        if (movement.LengthSquared() <= 0f) return;

        var speed = MoveSpeed * (input.IsKeyDown(InputKey.LeftShift) ? FastMultiplier : 1f);
        Transform.Position += Vector3.Normalize(movement) * speed * deltaTime;
    }

    internal Camera3D Sync()
    {
        RenderCamera.Position = Transform.WorldPosition;
        RenderCamera.Rotation = Transform.WorldRotation;
        return RenderCamera;
    }

    public override void OnDestroy()
    {
        if (input is not null) input.SetMouseCaptured(false);
    }
}
