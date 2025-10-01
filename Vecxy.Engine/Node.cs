namespace Vecxy.Engine;

public class Node
{
    public List<Node> Children { get; } = new();
    
    public Transform Transform { get; }
    public List<Component> Components { get; } = new();

    public TComponent? GetComponent<TComponent>() where TComponent : Component
    {
        var targetType = typeof(TComponent);
        
        for (int index = 0, count = Components.Count; index < count; index++)
        {
            var component = Components[index];

            var type = component.GetType();

            if (type == targetType)
            {
                return component as TComponent;
            }
        }

        return null;
    }
}