namespace Vecxy.Engine;

public interface IProject
{
    public string Id { get; }
    public string Name { get; }
    public string Author { get; }
    public string Description { get; }
    public void SetName(string name);
}

public class Project(string id) : IProject
{
    public string Id => Id;
    public string Name { get; private set; }
    public string Author { get; private set; }
    public string Description { get; private set; }
    
    
    public void SetName(string name)
    {
        Name = name;
    }
}