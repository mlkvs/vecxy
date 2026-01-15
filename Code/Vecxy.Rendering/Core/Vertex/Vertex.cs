using System.Numerics;

namespace Vecxy.Rendering;

public struct Vertex
{
    public Vector3 Position { get; set; }
    public Vector3 Normal { get; set; }
    public Vector2 UV { get; set; }
}