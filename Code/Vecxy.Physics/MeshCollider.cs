using System.Numerics;
using Vecxy.Prototypes;
using Vecxy.Scene;

namespace Vecxy.Physics;

/// <summary>Indexed triangle surface for static and kinematic bodies.</summary>
[Authoring("Mesh Collider", Category = "Physics", Key = "meshCollider",
    Description = "Uses the object's triangles on static or kinematic bodies.")]
public sealed class MeshCollider : Collider
{
    internal sealed record Geometry(Vector3[] Vertices, int[] Indices);

    internal Geometry? Mesh { get; private set; }

    public void SetGeometry(ReadOnlySpan<Vector3> vertices, ReadOnlySpan<int> indices)
    {
        if (indices.Length == 0 || indices.Length % 3 != 0)
            throw new ArgumentException("Provide at least one indexed triangle.", nameof(indices));

        foreach (var vertex in vertices)
            ThrowIfNotFinite(vertex, nameof(vertices));
        foreach (var index in indices)
            if ((uint)index >= (uint)vertices.Length)
                throw new ArgumentOutOfRangeException(nameof(indices));

        Mesh = new Geometry(vertices.ToArray(), indices.ToArray());
    }

    public sealed class Prototype : APrototype<MeshCollider, Prototype.Options>
    {
        public sealed class Options : ColliderOptions
        {
            public Vector3[] Vertices { get; set; } = [];
            public int[] Indices { get; set; } = [];
        }

        protected override MeshCollider Instantiate(IPrototypeContext context)
        {
            var ctx = RequireContext<ScenePrototypeContext>(context);
            return (ctx.Object ?? ctx.CreateObject("Mesh collider")).AddComponent<MeshCollider>();
        }

        protected override void Configure(MeshCollider collider, Options options)
        {
            collider.SetGeometry(options.Vertices, options.Indices);
            options.Apply(collider);
        }
    }
}
