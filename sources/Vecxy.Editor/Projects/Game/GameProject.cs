using System.Runtime.Serialization;

namespace Vecxy.Editor;

[DataContract]
public class GameProject(string path, ProjectInfo info) : Project(path, info)
{
    public override void Open()
    {
        base.Open();
    }

    public override void Save()
    {
        base.Save();
    }
}