using Vecxy.Scene;

namespace Vecxy.Physics;

public abstract class Collider : AComponent
{
    public bool IsTrigger { get; set; }
}
