using System.Numerics;
using Vecxy.Scene;

namespace Vecxy.Physics;

internal enum EPhysicsShapeType : byte
{
    Box,
    Sphere,
    Capsule
}

internal readonly record struct PhysicsBodyDefinition(
    Jitter2.Dynamics.MotionType MotionType,
    float Mass,
    bool AffectedByGravity,
    float LinearDamping,
    float AngularDamping,
    bool EnableSpeculativeContacts);

internal readonly record struct PhysicsShapeDefinition(
    EPhysicsShapeType Type,
    Vector3 Center,
    Quaternion Rotation,
    Vector3 Size,
    float Radius,
    float Height);

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
            rigidBody?.EnableSpeculativeContacts ?? false);
    }

    public static PhysicsShapeDefinition DescribeShape(
        Collider collider,
        Vector3 worldScale)
    {
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
