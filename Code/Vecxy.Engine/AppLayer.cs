using System.Runtime.Serialization;

namespace Vecxy.Engine;

public abstract class AppLayer
{
    public abstract void OnInitialize();
    public abstract void OnTick(float dt);
}