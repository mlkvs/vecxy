using Autofac;
using Vecxy.Kernel;

namespace Vecxy.Engine.Physics;

public sealed class PhysicsModule : IModule
{
    private bool _disposed;
    public PhysicsWorld World { get; } = new();
    public void OnLoad(ILifetimeScope scope) { }
    public void OnInitialize() { }
    public void OnTick(float deltaTime) => World.Step(deltaTime);
    public void OnUnload() => Dispose();
    public void Dispose() { if (_disposed) return; _disposed = true; World.Dispose(); }
}
