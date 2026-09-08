using Vecxy.Prototypes;

namespace Vecxy.UI;

public sealed class UiPrototypeContext : APrototypeContext
{
    public required UiDocument Document { get; init; }
    public UiElement? Parent { get; init; }
}

public sealed class UiPrototypeSystem : APrototypeSystem<UiElement, UiPrototypeContext>
{
    protected override object Instantiate(IPrototype prototype, object options, UiPrototypeContext context)
    {
        UiElement? result = null;
        try
        {
            result = (UiElement)base.Instantiate(prototype, options, context);
            if (options is UiElementPrototypeOptions elementOptions)
            {
                foreach (var child in elementOptions.Children)
                {
                    _ = context.Prototypes.Instantiate(child.Path, new UiPrototypeContext
                    {
                        Document = context.Document,
                        Parent = result
                    }, child.Overrides.Count == 0 ? null :
                        new PrototypeOverrides(PrototypeSerializer.SerializeOptions(child.Overrides)));
                }
            }
            if (context.Parent is not null && result.Parent is null)
                context.Parent.Add(result);
            return result;
        }
        catch (Exception exception)
        {
            result?.DestroyPrototypeTree();
            throw new PrototypeInstantiationException($"Could not instantiate UI prototype '{prototype.TargetType.FullName}'.", exception);
        }
    }
}
