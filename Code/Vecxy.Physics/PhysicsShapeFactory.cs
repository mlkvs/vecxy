using Jitter2.Collision.Shapes;
using Jitter2.LinearMath;

namespace Vecxy.Physics;

internal static class PhysicsShapeFactory
{
    public static RigidBodyShape? Create(Collider collider)
    {
        return collider switch
        {
            BoxCollider box => CreateBoxShape(box),
            CapsuleCollider capsule => CreateCapsuleShape(capsule),
            SphereCollider sphere => new SphereShape(sphere.Radius),
            _ => null
        };
    }

    private static RigidBodyShape CreateBoxShape(BoxCollider box)
    {
        var shape = new BoxShape(ToJVector(box.Size));

        if (box.Center == System.Numerics.Vector3.Zero)
            return shape;

        return new TransformedShape(
            shape,
            translation: ToJVector(box.Center));
    }

    private static RigidBodyShape CreateCapsuleShape(CapsuleCollider capsule)
    {
        var shape = new CapsuleShape(capsule.Radius, capsule.Height);

        if (capsule.Center == System.Numerics.Vector3.Zero)
            return shape;

        return new TransformedShape(
            shape,
            ToJVector(capsule.Center));
    }

    public static JVector ToJVector(System.Numerics.Vector3 value) =>
        new(value.X, value.Y, value.Z);

    public static JQuaternion ToJQuaternion(System.Numerics.Quaternion value) =>
        new(value.X, value.Y, value.Z, value.W);

    public static System.Numerics.Vector3 ToVector3(JVector value) =>
        new(value.X, value.Y, value.Z);

    public static System.Numerics.Quaternion ToQuaternion(JQuaternion value) =>
        new(value.X, value.Y, value.Z, value.W);
}
