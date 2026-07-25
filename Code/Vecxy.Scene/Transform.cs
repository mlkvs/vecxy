using System.Numerics;

namespace Vecxy.Scene;

public sealed class Transform : AComponent
{
    private Vector3 _localPosition;
    private Quaternion _localRotation = Quaternion.Identity;
    private Vector3 _localScale = Vector3.One;
    private Matrix4x4 _localMatrix = Matrix4x4.Identity;
    private Matrix4x4 _worldMatrix = Matrix4x4.Identity;
    private bool _localDirty = true;
    private bool _worldDirty = true;

    public Vector3 Position
    {
        get => _localPosition;
        set
        {
            if (_localPosition == value)
                return;

            _localPosition = value;
            MarkDirty();
        }
    }

    public Quaternion Rotation
    {
        get => _localRotation;
        set
        {
            var normalized = NormalizeRotation(value);
            if (_localRotation == normalized)
                return;

            _localRotation = normalized;
            MarkDirty();
        }
    }

    public Vector3 Scale
    {
        get => _localScale;
        set
        {
            if (_localScale == value)
                return;

            _localScale = value;
            MarkDirty();
        }
    }

    public Vector3 LocalPosition
    {
        get => Position;
        set => Position = value;
    }

    public Quaternion LocalRotation
    {
        get => Rotation;
        set => Rotation = value;
    }

    public Vector3 LocalScale
    {
        get => Scale;
        set => Scale = value;
    }

    public Matrix4x4 LocalMatrix
    {
        get
        {
            if (_localDirty)
            {
                _localMatrix =
                    Matrix4x4.CreateScale(_localScale) *
                    Matrix4x4.CreateFromQuaternion(_localRotation) *
                    Matrix4x4.CreateTranslation(_localPosition);
                _localDirty = false;
            }

            return _localMatrix;
        }
        set
        {
            if (!Matrix4x4.Decompose(value, out var scale, out var rotation, out var position))
                throw new ArgumentException("Transform matrix cannot be decomposed.", nameof(value));

            _localPosition = position;
            _localRotation = NormalizeRotation(rotation);
            _localScale = scale;
            _localDirty = true;
            MarkWorldDirty();
        }
    }

    public Matrix4x4 WorldMatrix
    {
        get
        {
            if (_worldDirty)
            {
                _worldMatrix = SceneObject?.Parent is { } parent
                    ? LocalMatrix * parent.Transform.WorldMatrix
                    : LocalMatrix;
                _worldDirty = false;
            }

            return _worldMatrix;
        }
        set
        {
            if (SceneObject?.Parent is not { } parent)
            {
                LocalMatrix = value;
                return;
            }

            if (!Matrix4x4.Invert(parent.Transform.WorldMatrix, out var inverseParent))
                throw new InvalidOperationException(
                    "Cannot set world transform because the parent transform is not invertible.");

            LocalMatrix = value * inverseParent;
        }
    }

    public Vector3 WorldPosition
    {
        get
        {
            var world = WorldMatrix;
            return new Vector3(world.M41, world.M42, world.M43);
        }
        set
        {
            var world = WorldMatrix;
            world.M41 = value.X;
            world.M42 = value.Y;
            world.M43 = value.Z;
            WorldMatrix = world;
        }
    }

    public Quaternion WorldRotation
    {
        get
        {
            DecomposeWorld(out _, out var rotation, out _);
            return rotation;
        }
        set
        {
            DecomposeWorld(out var scale, out _, out var position);
            WorldMatrix =
                Matrix4x4.CreateScale(scale) *
                Matrix4x4.CreateFromQuaternion(NormalizeRotation(value)) *
                Matrix4x4.CreateTranslation(position);
        }
    }

    public Vector3 WorldScale
    {
        get
        {
            DecomposeWorld(out var scale, out _, out _);
            return scale;
        }
        set
        {
            DecomposeWorld(out _, out var rotation, out var position);
            WorldMatrix =
                Matrix4x4.CreateScale(value) *
                Matrix4x4.CreateFromQuaternion(rotation) *
                Matrix4x4.CreateTranslation(position);
        }
    }

    public Vector3 Forward =>
        Vector3.Normalize(Vector3.Transform(-Vector3.UnitZ, WorldRotation));

    public Vector3 Right =>
        Vector3.Normalize(Vector3.Transform(Vector3.UnitX, WorldRotation));

    public Vector3 Up =>
        Vector3.Normalize(Vector3.Transform(Vector3.UnitY, WorldRotation));

    public void Translate(Vector3 translation, bool worldSpace = false)
    {
        if (worldSpace)
            WorldPosition += translation;
        else
            Position += translation;
    }

    public void Rotate(Quaternion rotation, bool worldSpace = false)
    {
        var normalized = NormalizeRotation(rotation);
        if (worldSpace)
            WorldRotation = Quaternion.Normalize(normalized * WorldRotation);
        else
            Rotation = Quaternion.Normalize(Rotation * normalized);
    }

    public void LookAt(Vector3 target, Vector3? up = null)
    {
        var position = WorldPosition;
        if (Vector3.DistanceSquared(position, target) <= float.Epsilon)
            throw new ArgumentException(
                "Look-at target must differ from the transform position.",
                nameof(target));

        var upDirection = up ?? Vector3.UnitY;
        if (upDirection.LengthSquared() <= float.Epsilon)
            throw new ArgumentException(
                "Look-at up direction cannot be zero.",
                nameof(up));

        var view = Matrix4x4.CreateLookAt(
            position,
            target,
            Vector3.Normalize(upDirection));
        if (!Matrix4x4.Invert(view, out var world) ||
            !Matrix4x4.Decompose(world, out _, out var rotation, out _))
        {
            throw new InvalidOperationException(
                "Could not calculate the look-at rotation.");
        }

        WorldRotation = rotation;
    }

    internal void MarkWorldDirty()
    {
        if (_worldDirty)
            return;

        _worldDirty = true;

        if (SceneObject is not { } sceneObject)
            return;

        foreach (var child in sceneObject.Children)
            child.Transform.MarkWorldDirty();
    }

    private void MarkDirty()
    {
        _localDirty = true;
        MarkWorldDirty();
    }

    private void DecomposeWorld(
        out Vector3 scale,
        out Quaternion rotation,
        out Vector3 position)
    {
        if (!Matrix4x4.Decompose(
                WorldMatrix,
                out scale,
                out rotation,
                out position))
        {
            throw new InvalidOperationException(
                "World transform matrix cannot be decomposed.");
        }

        rotation = NormalizeRotation(rotation);
    }

    private static Quaternion NormalizeRotation(Quaternion rotation)
    {
        if (rotation.LengthSquared() <= float.Epsilon)
            throw new ArgumentException(
                "Rotation quaternion cannot be zero.",
                nameof(rotation));

        return Quaternion.Normalize(rotation);
    }
}
