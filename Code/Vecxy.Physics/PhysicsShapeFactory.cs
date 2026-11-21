using System.Numerics;
using Jitter2.Collision.Shapes;
using Jitter2.LinearMath;

namespace Vecxy.Physics;

internal static class PhysicsShapeFactory
{
    public static RigidBodyShape[] CreateShapes(in PhysicsShapeDefinition definition)
    {
        if (definition.Mesh is not { } geometry)
            return [CreatePrimitive(definition)];

        var transform = definition.MeshTransform;
        var vertices = Array.ConvertAll(geometry.Vertices,
            vertex => ToJVector(Vector3.Transform(vertex, transform)));
        var indices = geometry.Indices;
        // Mirroring a mesh reverses winding, while its front face should stay outside.
        if (transform.GetDeterminant() < 0)
        {
            indices = (int[])indices.Clone();
            for (var i = 0; i < indices.Length; i += 3)
                (indices[i + 1], indices[i + 2]) = (indices[i + 2], indices[i + 1]);
        }

        var mesh = new TriangleMesh(vertices.AsSpan(), indices.AsSpan(), ignoreDegenerated: true);
        var count = mesh.Indices.Length;
        if (count == 0)
            throw new InvalidOperationException("Mesh Collider has no nondegenerate triangles.");

        var shapes = new RigidBodyShape[count];
        for (var i = 0; i < count; i++)
            shapes[i] = new MeshTriangleShape(mesh, i);
        return shapes;
    }

    private sealed class MeshTriangleShape(TriangleMesh mesh, int index) : TriangleShape(mesh, index)
    {
        public override bool LocalRayCast(in JVector origin, in JVector direction,
            out JVector normal, out float lambda)
        {
            if (base.LocalRayCast(origin, direction, out normal, out lambda))
                return true;

            // A rotated ray landing on a shared edge can miss both native
            // triangle tests. Keep a small, relative tolerance on the edges.
            var triangle = Mesh.Indices[Index];
            var a = Mesh.Vertices[triangle.IndexA];
            var b = Mesh.Vertices[triangle.IndexB];
            var c = Mesh.Vertices[triangle.IndexC];
            var face = JVector.Cross(b - a, c - a);
            var incidence = JVector.Dot(face, direction);
            if (!(incidence < 0))
                return false;

            var distance = JVector.Dot(face, a - origin) / incidence;
            if (distance < 0 || !float.IsFinite(distance))
                return false;

            var point = origin + distance * direction;
            var tolerance = -1e-6f * JVector.Dot(face, face);
            if (JVector.Dot(JVector.Cross(b - a, point - a), face) < tolerance ||
                JVector.Dot(JVector.Cross(c - b, point - b), face) < tolerance ||
                JVector.Dot(JVector.Cross(a - c, point - c), face) < tolerance)
                return false;

            normal = JVector.Normalize(face);
            lambda = distance;
            return true;
        }
    }

    private static RigidBodyShape CreatePrimitive(
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
