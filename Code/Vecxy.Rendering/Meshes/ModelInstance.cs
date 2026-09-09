using Vecxy.Scene;

namespace Vecxy.Rendering;

[SingleComponent]
public sealed class ModelInstance : AComponent
{
    private readonly SceneObject?[] _nodes;

    public Model Model { get; }
    public IReadOnlyList<SceneObject?> Nodes => _nodes;

    internal ModelInstance(Model model, SceneObject?[] nodes)
    {
        ArgumentNullException.ThrowIfNull(model);
        ArgumentNullException.ThrowIfNull(nodes);
        Model = model;
        _nodes = nodes;
    }

    public SceneObject GetNode(int nodeIndex)
    {
        if (nodeIndex < 0 || nodeIndex >= _nodes.Length)
            throw new ArgumentOutOfRangeException(nameof(nodeIndex));
        return _nodes[nodeIndex] ?? throw new InvalidOperationException(
            $"Model node {nodeIndex} is not part of the instantiated glTF scene.");
    }

    public SceneObject GetNode(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        var matches = _nodes.Where(node => node is not null && string.Equals(node.Name, name, StringComparison.Ordinal))
            .Cast<SceneObject>()
            .ToArray();
        return matches.Length switch
        {
            1 => matches[0],
            0 => throw new KeyNotFoundException($"Model has no node named '{name}'."),
            _ => throw new InvalidOperationException($"Model contains more than one node named '{name}'; use its node index.")
        };
    }

    public bool TryGetNode(int nodeIndex, out SceneObject node)
    {
        var value = nodeIndex >= 0 && nodeIndex < _nodes.Length ? _nodes[nodeIndex] : null;
        node = value!;
        return value is not null;
    }
}
