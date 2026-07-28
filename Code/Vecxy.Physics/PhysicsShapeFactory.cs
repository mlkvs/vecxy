using System.Numerics;
using Jitter2.Collision.Shapes;
using Jitter2.LinearMath;

namespace Vecxy.Physics;

internal static class PhysicsShapeFactory
{
    public static RigidBodyShape Create(
        in PhysicsShapeDefinition definition)
    {
        RigidBodyShape shape = definition.Type switch
        {
            EPhysicsShapeType.Box =>
                new BoxShape(ToJVector(definition.Size)),
            EPhysicsShapeType.Sphere =>
                new SphereShape(definition.Radius),
            EPhysicsShapeType.Capsule =>
                new CapsuleShape(
                    definition.Radius,
                    definition.Height),
            _ => throw new ArgumentOutOfRangeException(
                nameof(definition))
        };

        if (definition.Center == Vector3.Zero &&
            definition.Rotation == Quaternion.Identity)
        {
            return shape;
        }

        var orientation = JMatrix.CreateFromQuaternion(
            ToJQuaternion(definition.Rotation));

        return new TransformedShape(
            shape,
            ToJVector(definition.Center),
            orientation);
    }

    public static JVector ToJVector(Vector3 value) =>
        new(value.X, value.Y, value.Z);

    public static JQuaternion ToJQuaternion(Quaternion value) =>
        new(value.X, value.Y, value.Z, value.W);

    public static Vector3 ToVector3(JVector value) =>
        new(value.X, value.Y, value.Z);

    public static Quaternion ToQuaternion(JQuaternion value) =>
        new(value.X, value.Y, value.Z, value.W);
}
