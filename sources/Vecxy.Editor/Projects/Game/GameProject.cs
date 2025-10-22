using System.Runtime.Serialization;

namespace Vecxy.Editor;

[DataContract]
public class GameProject(string path, ProjectFile file) : Project(path, file)
{
}