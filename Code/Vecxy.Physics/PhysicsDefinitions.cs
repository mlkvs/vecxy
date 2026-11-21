using System.Numerics;
using Vecxy.Scene;

namespace Vecxy.Physics;

internal enum EPhysicsShapeType : byte
{
    Box,
    Sphere,
    Capsule,
    Mesh
}

internal readonly record struct PhysicsBodyDefinition(
    Jitter2.Dynamics.MotionType MotionType,
    float Mass,
    bool AffectedByGravity,
    float LinearDamping,
    float AngularDamping,
    bool EnableSpeculativeContacts,
    bool FreezeRotation);

internal readonly record struct PhysicsShapeDefinition(
    EPhysicsShapeType Type,
    Vector3 Center,
    Quaternion Rotation,
    Vector3 Size,
    float Radius,
    float Height,
    MeshCollider.Geometry? Mesh = null,
    Matrix4x4 MeshTransform = default,
    bool DoubleSided = false)
{
    public bool Matches(in PhysicsShapeDefinition other)
    {
        if (this == other)
            return true;
        if (Mesh is null || !ReferenceEquals(Mesh, other.Mesh) || DoubleSided != other.DoubleSided)
            return false;

        // Removing the body's rotation from the world matrix introduces
        // roundoff when a kinematic mesh rotates without changing its shape.
        for (var row = 0; row < 4; row++)
        for (var column = 0; column < 3; column++)
        {
            var current = MeshTransform[row, column];
            var next = other.MeshTransform[row, column];
            var tolerance = 1e-6f * Math.Max(1, Math.Max(Math.Abs(current), Math.Abs(next)));
            if (!float.IsFinite(next) || Math.Abs(current - next) > tolerance)
                return false;
        }
        return true;
    }
}

internal readonly record struct PhysicsPose(
    Vector3 Position,
    Quaternion Rotation)
{
    public static PhysicsPose From(Transform transform) =>
        new(transform.WorldPosition, transform.WorldRotation);
}

internal static class PhysicsDescriptionFactory
{
    public static PhysicsBodyDefinition DescribeBody(
        SceneObject sceneObject,
        RigidBody? rigidBody)
    {
        var motionType = sceneObject.IsStatic
            ? Jitter2.Dynamics.MotionType.Static
            : rigidBody is null || !rigidBody.IsActive
                ? Jitter2.Dynamics.MotionType.Kinematic
                : rigidBody.MotionType switch
                {
                    EPhysicsMotionType.Dynamic =>
                        Jitter2.Dynamics.MotionType.Dynamic,
                    EPhysicsMotionType.Kinematic =>
                        Jitter2.Dynamics.MotionType.Kinematic,
                    _ => throw new ArgumentOutOfRangeException()
                };

        return new PhysicsBodyDefinition(
            motionType,
            rigidBody?.Mass ?? 1.0f,
            rigidBody?.AffectedByGravity ?? false,
            rigidBody?.LinearDamping ?? 0.0f,
            rigidBody?.AngularDamping ?? 0.0f,
            rigidBody?.EnableSpeculativeContacts ?? false,
            rigidBody?.FreezeRotation ?? false);
    }

    public static PhysicsShapeDefinition DescribeShape(
        Collider collider,
        Transform transform)
    {
        if (collider is MeshCollider mesh)
        {
            var geometry = mesh.Mesh ?? throw new InvalidOperationException("Mesh Collider has no geometry.");
            var linear = transform.WorldMatrix;
            linear.Translation = Vector3.Zero;
            var meshTransform = Matrix4x4.CreateFromQuaternion(mesh.Rotation)
                * Matrix4x4.CreateTranslation(mesh.Center)
                * linear
                * Matrix4x4.CreateFromQuaternion(Quaternion.Conjugate(transform.WorldRotation));
            return new PhysicsShapeDefinition(EPhysicsShapeType.Mesh, default, default, default,
                0, 0, geometry, meshTransform, mesh.DoubleSided);
        }

        var worldScale = transform.WorldScale;
        var scale = new Vector3(
            Math.Abs(worldScale.X),
            Math.Abs(worldScale.Y),
            Math.Abs(worldScale.Z));

        if (scale.X <= float.Epsilon ||
            scale.Y <= float.Epsilon ||
            scale.Z <= float.Epsilon)
        {
            throw new InvalidOperationException(
                $"Collider '{collider.GetType().Name}' cannot use zero world scale.");
        }

        var center = collider.Center * scale;

        return collider switch
        {
            BoxCollider box => new PhysicsShapeDefinition(
                EPhysicsShapeType.Box,
                center,
                collider.Rotation,
                box.Size * scale,
                0.0f,
                0.0f),

            BoxCollider2D box => new PhysicsShapeDefinition(
                EPhysicsShapeType.Box,
                center,
                collider.Rotation,
                new Vector3(
                    box.Size.X * scale.X,
                    box.Size.Y * scale.Y,
                    box.Depth * scale.Z),
                0.0f,
                0.0f),

            SphereCollider sphere => new PhysicsShapeDefinition(
                EPhysicsShapeType.Sphere,
                center,
                Quaternion.Identity,
                Vector3.Zero,
                sphere.Radius * Math.Max(
                    scale.X,
                    Math.Max(scale.Y, scale.Z)),
                0.0f),

            CapsuleCollider capsule => new PhysicsShapeDefinition(
                EPhysicsShapeType.Capsule,
                center,
                collider.Rotation,
                Vector3.Zero,
                capsule.Radius * Math.Max(scale.X, scale.Z),
                capsule.Height * scale.Y),

            _ => throw new NotSupportedException(
                $"Unsupported collider type '{collider.GetType().Name}'.")
        };
    }
}
