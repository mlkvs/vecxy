using System.Numerics;

namespace Vecxy.Engine._Legacy;

[Serializable]
public sealed class Transform
{
    private Transform? _parent;
    private readonly List<Transform> _children = [];

    public Vector3 Position { get; set; } = Vector3.Zero;
    public Quaternion Rotation { get; set; } = Quaternion.Identity;
    public Vector3 Scale { get; set; } = Vector3.One;
    public Transform? Parent => _parent;
    public IReadOnlyList<Transform> Children => _children;

    public Matrix4x4 LocalMatrix => Matrix4x4.CreateScale(Scale) *
        Matrix4x4.CreateFromQuaternion(Rotation) * Matrix4x4.CreateTranslation(Position);
    public Matrix4x4 WorldMatrix => _parent is null ? LocalMatrix : LocalMatrix * _parent.WorldMatrix;
    public Vector3 WorldPosition => new(WorldMatrix.M41, WorldMatrix.M42, WorldMatrix.M43);
    public Quaternion WorldRotation => _parent is null ? Rotation : Quaternion.Normalize(Rotation * _parent.WorldRotation);
    public Vector3 Forward => Vector3.Normalize(Vector3.Transform(-Vector3.UnitZ, WorldRotation));
    public Vector3 Right => Vector3.Normalize(Vector3.Transform(Vector3.UnitX, WorldRotation));

    public void SetParent(Transform? parent)
    {
        if (ReferenceEquals(parent, this)) throw new InvalidOperationException("A transform cannot parent itself.");
        for (var ancestor = parent; ancestor is not null; ancestor = ancestor.Parent)
            if (ReferenceEquals(ancestor, this)) throw new InvalidOperationException("Transform hierarchy cannot contain cycles.");
        _parent?._children.Remove(this);
        _parent = parent;
        if (parent is not null && !parent._children.Contains(this)) parent._children.Add(this);
    }

    public void SetLocalMatrix(Matrix4x4 matrix)
    {
        if (!Matrix4x4.Decompose(matrix, out var scale, out var rotation, out var translation))
            throw new ArgumentException("Matrix cannot be decomposed into a transform.", nameof(matrix));
        Position = translation;
        Rotation = Quaternion.Normalize(rotation);
        Scale = scale;
    }
}
