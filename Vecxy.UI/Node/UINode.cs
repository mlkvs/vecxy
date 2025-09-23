namespace Vecxy.UI;

public class UINode
{
    public string Id { get; set; }
    public List<string> Classes { get; private set; }
    public List<UINode> Children { get; private set; }
    
    
    public UINode Parent { get; private set; }

    public UINode()
    {
        Id = Guid.NewGuid().ToString("N"); // Автогенерация ID по умолчанию
        Classes = new List<string>();
        Children = new List<UINode>();
    }

    public UINode(string id) : this()
    {
        if (!string.IsNullOrEmpty(id))
            Id = id;
    }

    public void AddNode(UINode node)
    {
        if (node == null)
            throw new ArgumentNullException(nameof(node));
        
        if (node == this)
            throw new InvalidOperationException("Cannot add node to itself");
            
        if (Children.Contains(node))
            throw new InvalidOperationException("Node already exists in children");
        
        Children.Add(node);
    }

    public void RemoveNode(UINode node)
    {
        if (node == null)
            throw new ArgumentNullException(nameof(node));
        
        if (!Children.Contains(node))
            throw new InvalidOperationException("Node not found in children");
        
        Children.Remove(node);
    }

    public bool RemoveNodeById(string id)
    {
        var node = Children.FirstOrDefault(n => n.Id == id);
        if (node != null)
        {
            Children.Remove(node);
            return true;
        }
        return false;
    }

    public TNode GetNodeById<TNode>(string id) where TNode : UINode
    {
        if (string.IsNullOrEmpty(id))
            throw new ArgumentException("ID cannot be null or empty", nameof(id));
        
        // Проверяем текущий узел
        if (Id == id && this is TNode typedThis)
            return typedThis;
        
        // Рекурсивно ищем в детях
        return FindNodeInChildren<TNode>(id, this);
    }

    public UINode GetNodeById(string id)
        => GetNodeById<UINode>(id);

    public bool HasClass(string className)
        => Classes.Contains(className);

    public void AddClass(string className)
    {
        if (!HasClass(className))
            Classes.Add(className);
    }

    public void RemoveClass(string className)
        => Classes.Remove(className);

    public void ToggleClass(string className)
    {
        if (HasClass(className))
            RemoveClass(className);
        else
            AddClass(className);
    }

    // Вспомогательные методы
    private TNode FindNodeInChildren<TNode>(string id, UINode currentNode) where TNode : UINode
    {
        foreach (var child in currentNode.Children)
        {
            // Проверяем текущего ребенка
            if (child.Id == id && child is TNode typedChild)
                return typedChild;
            
            // Рекурсивно ищем в детях ребенка
            var found = FindNodeInChildren<TNode>(id, child);
            if (found != null)
                return found;
        }
        
        return null;
    }

    // Дополнительные полезные методы
    public IEnumerable<UINode> GetNodesByClass(string className)
    {
        var results = new List<UINode>();
        CollectNodesByClass(className, this, results);
        return results;
    }

    public IEnumerable<TNode> GetNodesOfType<TNode>() where TNode : UINode
    {
        var results = new List<TNode>();
        CollectNodesOfType<TNode>(this, results);
        return results;
    }

    private void CollectNodesByClass(string className, UINode currentNode, List<UINode> results)
    {
        if (currentNode.HasClass(className))
            results.Add(currentNode);
        
        foreach (var child in currentNode.Children)
            CollectNodesByClass(className, child, results);
    }

    private void CollectNodesOfType<TNode>(UINode currentNode, List<TNode> results) where TNode : UINode
    {
        if (currentNode is TNode typedNode)
            results.Add(typedNode);
        
        foreach (var child in currentNode.Children)
            CollectNodesOfType<TNode>(child, results);
    }
}