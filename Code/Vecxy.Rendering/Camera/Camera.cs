using System.Numerics;
using Vecxy.Scene;

namespace Vecxy.Rendering;

public sealed class Camera : AComponent
{
    public class Prototype : APrototype<Camera, Prototype.Options>
    {
        public class Options : IPrototype.IOptions
        {
            public ECameraProjection Projection { get; set; } = ECameraProjection.Perspective;
            public Vector4 ClearColor { get; set; } = new(0.02f, 0.03f, 0.05f, 1.0f);
            
            public float FieldOfView { get; set; } = 60.0f;
            public float NearPlane { get; set; } = 0.1f;
            public float FarPlane { get; set; } = 100.0f;
            public float OrthographicSize { get; set; } = 10.0f;
        }

        protected override Camera Instantiate(InstantiateContext ctx)
        {
            if (ctx.Scene == null)
            {
                throw new NotImplementedException();
            }
            
            var cameraObject = ctx.Scene.CreateObject("Camera");
            cameraObject.Transform.Position = ctx.Position;
            
            return cameraObject.AddComponent<Camera>();
        }

        protected override void Configure(Camera component, Options options)
        {
            component.Projection = options.Projection;
            component.OrthographicSize = options.OrthographicSize;
            component.FieldOfView = options.FieldOfView;
            component.NearPlane = options.NearPlane;
            component.FarPlane = options.FarPlane;
            component.ClearColor = options.ClearColor;
        }
    }
    
    private float _fieldOfView = 60.0f;
    private float _nearPlane = 0.1f;
    private float _farPlane = 1000.0f;
    private float _orthographicSize = 5.0f;

    public ECameraProjection Projection { get; set; } = ECameraProjection.Perspective;

    public float FieldOfView
    {
        get => _fieldOfView;
        set
        {
            if (value is <= 0.0f or >= 180.0f)
                throw new ArgumentOutOfRangeException(
                    nameof(value),
                    "Field of view must be between 0 and 180 degrees.");

            _fieldOfView = value;
        }
    }

    public float NearPlane
    {
        get => _nearPlane;
        set
        {
            if (value <= 0.0f || value >= _farPlane)
                throw new ArgumentOutOfRangeException(
                    nameof(value),
                    "Near plane must be positive and less than the far plane.");

            _nearPlane = value;
        }
    }

    public float FarPlane
    {
        get => _farPlane;
        set
        {
            if (value <= _nearPlane)
                throw new ArgumentOutOfRangeException(
                    nameof(value),
                    "Far plane must be greater than the near plane.");

            _farPlane = value;
        }
    }

    public float OrthographicSize
    {
        get => _orthographicSize;
        set
        {
            if (value <= 0.0f)
                throw new ArgumentOutOfRangeException(
                    nameof(value),
                    "Orthographic size must be positive.");

            _orthographicSize = value;
        }
    }

    public Vector4 ClearColor { get; set; } =
        new(0.02f, 0.03f, 0.05f, 1.0f);

    public bool UsePostProcessing { get; set; }

    public int Priority { get; set; }

    public SceneRenderTarget? TargetTexture { get; set; }

    public Matrix4x4 ViewMatrix
    {
        get
        {
            if (!Matrix4x4.Invert(
                    Transform.WorldMatrix,
                    out var view))
            {
                throw new InvalidOperationException(
                    "Camera transform is not invertible.");
            }

            return view;
        }
    }

    public Matrix4x4 GetProjectionMatrix(float aspectRatio)
    {
        if (aspectRatio <= 0.0f ||
            float.IsNaN(aspectRatio) ||
            float.IsInfinity(aspectRatio))
        {
            throw new ArgumentOutOfRangeException(nameof(aspectRatio));
        }

        return Projection switch
        {
            ECameraProjection.Perspective =>
                CreatePerspectiveProjection(
                    MathF.PI / 180.0f * _fieldOfView,
                    aspectRatio,
                    _nearPlane,
                    _farPlane),

            ECameraProjection.Orthographic =>
                CreateOrthographicProjection(
                    _orthographicSize * 2.0f * aspectRatio,
                    _orthographicSize * 2.0f,
                    _nearPlane,
                    _farPlane),

            _ => throw new ArgumentOutOfRangeException()
        };
    }

    private static Matrix4x4 CreatePerspectiveProjection(float fieldOfView, float aspectRatio,
        float nearPlane,
        float farPlane)
    {
        var verticalScale =
            1.0f / MathF.Tan(fieldOfView * 0.5f);
        var depth = nearPlane - farPlane;

        return new Matrix4x4(
            verticalScale / aspectRatio, 0.0f, 0.0f, 0.0f,
            0.0f, verticalScale, 0.0f, 0.0f,
            0.0f, 0.0f, (farPlane + nearPlane) / depth, -1.0f,
            0.0f, 0.0f, 2.0f * farPlane * nearPlane / depth, 0.0f);
    }

    private static Matrix4x4 CreateOrthographicProjection(float width, float height, float nearPlane, float farPlane)
    {
        var depth = farPlane - nearPlane;

        return new Matrix4x4
        (
            2.0f / width, 0.0f, 0.0f, 0.0f,
            0.0f, 2.0f / height, 0.0f, 0.0f,
            0.0f, 0.0f, -2.0f / depth, 0.0f,
            0.0f, 0.0f, -(farPlane + nearPlane) / depth, 1.0f
        );
    }
}
