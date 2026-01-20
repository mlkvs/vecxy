using System.Runtime.Serialization;

namespace Vecxy.Engine;

[DataContract]
public struct AppInfo
{
    [DataMember(Name = "version")] public string Version;
    [DataMember(Name = "name")] public string Name;
    [DataMember(Name = "description")] public string Description;
    [DataMember(Name = "author")] public string Author;
}

public abstract class ApplicationLayer()
{
    public abstract void OnInitialize();
    public abstract void OnTick();
}