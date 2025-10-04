using System.Reflection;

namespace Vecxy.Reflection.Tests;

public class EmbeddedResource_Tests
{
    private Assembly _assembly;

    [SetUp]
    public void Setup()
    {
        _assembly = Assembly.GetExecutingAssembly();
    }

    [Test]
    public void Has()
    {
        var has1 = EmbeddedResource.Has(_assembly, "test1.txt");
        var has2 = EmbeddedResource.Has(_assembly, "test2.txt");
        var has3 = EmbeddedResource.Has(_assembly, "test3.txt");
        
        Assert.That(has1 && has2 && has3, Is.True);
    }
    
    [Test]
    public void Has_NotFound()
    {
        var has = EmbeddedResource.Has(_assembly, "test4.txt");
        
        Assert.That(has, Is.False);
    }

    [Test]
    public void From()
    {
        var resources = EmbeddedResource.From(_assembly);
        
        Assert.That(resources.Length, Is.EqualTo(3));
    }

    [Test]
    public void From_Check_ResourceName()
    {
        var targetResourceName = "test1.txt";
        
        var resources = EmbeddedResource.From(_assembly);
        
        var isFound = false;

        foreach (var resource in resources)
        {
            if (resource.ResourceName.Equals(targetResourceName))
            {
                isFound = true;
            }
        }
        
        Assert.That(isFound, Is.True);
    }

    [Test]
    public void Get()
    {
        var resource = EmbeddedResource.Get(_assembly, "test1.txt");
        
        Assert.That(resource, Is.Not.Null);
    }
    
    [Test]
    public void Get_NotFound()
    {
        var resource = EmbeddedResource.Get(_assembly, "test4.txt");
        
        Assert.That(resource, Is.Null);
    }
    
    [Test]
    public void TryGet()
    {
        var result = EmbeddedResource.TryGet(_assembly, "test1.txt", out var resource);
        
        Assert.Multiple(() =>
        {
            Assert.That(resource, Is.Not.Null);
            Assert.That(result, Is.True);
        });
    }
    
    [Test]
    public void TryGet_NotFound()
    {
        var result = EmbeddedResource.TryGet(_assembly, "test4.txt", out var resource);
        
        Assert.Multiple(() =>
        {
            Assert.That(resource, Is.Null);
            Assert.That(result, Is.False);
        });
    }
}