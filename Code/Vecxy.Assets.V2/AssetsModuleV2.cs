using Autofac;
using Vecxy.Diagnostics;
using Vecxy.Kernel;

namespace Vecxy.Assets.V2;

public class AssetsModuleV2 : IModule
{
    public class Installer : Autofac.Module
    {
        protected override void Load(ContainerBuilder builder)
        {
            builder
                .RegisterType<AssetsManager>()
                .As<IAssetsManager>()
                .PropertiesAutowired()
                .SingleInstance();
        }
    }
    
    public void Dispose()
    {
        // TODO release managed resources here
    }

    public void OnLoad(ILifetimeScope scope)
    {
        
    }

    public void OnInitialize()
    { 
        Logger.Info("AssetsModuleV2 initialized");
    }

    public void OnTick(float deltaTime)
    {
    }

    public void OnFrame()
    {
    }

    public void OnUnload()
    {
    }
}
