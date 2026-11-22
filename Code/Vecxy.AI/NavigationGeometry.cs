using System.Numerics;
using Vecxy.Pathfinding;
using Vecxy.Physics;
using Vecxy.Scene;

namespace Vecxy.AI;

internal static class NavigationGeometry
{
    public static NavigationBakeInput Capture(SceneInstance scene, NavigationBakeSettings settings)
    {
        var triangles = new List<NavigationTriangle>();
        var blockers = new List<NavigationBox>();
        var obstacles = new List<NavigationBox>();
        var solids = new List<NavigationBox>();
        foreach (var obj in scene.Objects)
        {
            if (!Enabled(obj))
                continue;
            if (obj.GetComponent<NavigationBlocker>() is { Enabled: true } blocker)
                blockers.Add(BlockerBox(blocker));
            if (obj.GetComponent<NavigationObstacle>() is { Enabled: true } obstacle)
                obstacles.AddRange(ObstacleBoxes(obstacle));
            if (!obj.IsStatic || UnderObstacle(obj))
                continue;
            foreach (var collider in obj.GetComponents<Collider>().Where(Solid))
            {
                if (collider is MeshCollider mesh)
                    AddMesh(triangles, mesh);
                else if (ColliderBox(collider) is { } box)
                {
                    solids.Add(box);
                    var faces = box.ToTriangles();
                    triangles.AddRange(collider is BoxCollider
                        ? faces : faces.Select(face => face with { SupportsWalking = false }));
                }
            }
        }
        return new(settings, triangles.ToArray(), blockers.ToArray(), obstacles.ToArray(), solids.ToArray());
    }

    public static NavigationBox BlockerBox(NavigationBlocker blocker) =>
        NavigationBox.FromTransform(blocker.Transform.WorldMatrix, Vector3.One);

    public static NavigationBox[] ObstacleBoxes(NavigationObstacle obstacle)
    {
        var boxes = new List<NavigationBox>();
        if (obstacle.UseColliders)
            foreach (var obj in obstacle.SceneObject!.EnumerateHierarchy())
                if (Enabled(obj))
                    foreach (var collider in obj.GetComponents<Collider>().Where(Solid))
                        if (ColliderBox(collider) is { } box)
                            boxes.Add(box);
        if (boxes.Count == 0 && obstacle.Size.X > 0 && obstacle.Size.Y > 0 && obstacle.Size.Z > 0)
        {
            var transform = Matrix4x4.CreateTranslation(obstacle.Center) * obstacle.Transform.WorldMatrix;
            boxes.Add(NavigationBox.FromTransform(transform, obstacle.Size));
        }
        return boxes.ToArray();
    }

    private static bool UnderObstacle(SceneObject obj)
    {
        for (SceneObject? owner = obj; owner is not null; owner = owner.Parent)
            if (owner.GetComponent<NavigationObstacle>() is { Enabled: true })
                return true;
        return false;
    }

    internal static bool Enabled(SceneObject obj)
    {
        for (SceneObject? owner = obj; owner is not null; owner = owner.Parent)
            if (!owner.Enabled || owner.IsDestroying)
                return false;
        return true;
    }

    private static bool Solid(Collider collider) => collider.Enabled && !collider.IsTrigger;

    private static Matrix4x4 MeshTransform(MeshCollider collider) =>
        Matrix4x4.CreateFromQuaternion(collider.Rotation) * Matrix4x4.CreateTranslation(collider.Center) *
        collider.Transform.WorldMatrix;

    private static void AddMesh(List<NavigationTriangle> result, MeshCollider collider)
    {
        var indices = collider.Indices;
        var vertices = collider.Vertices;
        var transform = MeshTransform(collider);
        var mirrored = transform.GetDeterminant() < 0;
        for (var i = 0; i < indices.Length; i += 3)
            result.Add(new(Vector3.Transform(vertices[indices[i]], transform),
                Vector3.Transform(vertices[indices[i + (mirrored ? 2 : 1)]], transform),
                Vector3.Transform(vertices[indices[i + (mirrored ? 1 : 2)]], transform)));
    }

    private static NavigationBox? ColliderBox(Collider collider)
    {
        if (collider is MeshCollider mesh)
        {
            if (mesh.Vertices.IsEmpty)
                return null;
            var low = mesh.Vertices[0];
            var high = low;
            foreach (var vertex in mesh.Vertices)
            {
                low = Vector3.Min(low, vertex);
                high = Vector3.Max(high, vertex);
            }
            return NavigationBox.FromTransform(Matrix4x4.CreateTranslation((low + high) * .5f) * MeshTransform(mesh),
                Vector3.Max(high - low, new Vector3(.001f)));
        }

        var transform = collider.Transform;
        var scale = Vector3.Abs(transform.WorldScale);
        var size = collider switch
        {
            BoxCollider box => box.Size * scale,
            SphereCollider sphere => new Vector3(2 * sphere.Radius * Math.Max(scale.X, Math.Max(scale.Y, scale.Z))),
            CapsuleCollider capsule => CapsuleSize(capsule, scale),
            _ => Vector3.Zero
        };
        if (size.X <= 0 || size.Y <= 0 || size.Z <= 0)
            return null;
        var boxTransform = Matrix4x4.CreateFromQuaternion(collider.Rotation) *
            Matrix4x4.CreateTranslation(collider.Center * scale) *
            Matrix4x4.CreateFromQuaternion(transform.WorldRotation) *
            Matrix4x4.CreateTranslation(transform.WorldPosition);
        if (collider is SphereCollider)
            boxTransform = Matrix4x4.CreateTranslation(boxTransform.Translation);
        return NavigationBox.FromTransform(boxTransform, size);
    }

    private static Vector3 CapsuleSize(CapsuleCollider capsule, Vector3 scale)
    {
        var diameter = 2 * capsule.Radius * Math.Max(scale.X, scale.Z);
        return new(diameter, diameter + capsule.Height * scale.Y, diameter);
    }
}
