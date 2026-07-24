using Vecxy.Assets;

namespace Vecxy.Engine._Legacy;

public sealed class ModelInstance(ModelAsset model) : Script
{
    public ModelAsset Model { get; } = model ?? throw new ArgumentNullException(nameof(model));

    public override void OnStart()
    {
        Model.Reloaded += OnModelReloaded;
        BuildHierarchy();
    }

    private void BuildHierarchy()
    {
        SceneObject.ClearChildren();
        foreach (var node in Model.Nodes)
            SceneObject.AddChild(CreateNode(node));
    }

    private static SceneObject CreateNode(ModelNode node)
    {
        var nodeObject = new SceneObject(node.Name);
        nodeObject.Transform.SetLocalMatrix(node.Transform);
        foreach (var primitive in node.Primitives)
            nodeObject.AddScript(new MeshRenderer(primitive));
        foreach (var child in node.Children)
            nodeObject.AddChild(CreateNode(child));
        return nodeObject;
    }

    private void OnModelReloaded(Asset _) => BuildHierarchy();

    public override void OnDestroy() => Model.Reloaded -= OnModelReloaded;
}
