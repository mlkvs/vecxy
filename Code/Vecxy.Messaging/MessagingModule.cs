using Autofac;
using JetBrains.Annotations;
using Mediator.Net;
using Mediator.Net.Autofac;
using Vecxy.Kernel;

namespace Vecxy.Messaging;

[UsedImplicitly]
public sealed class MessagingModule : IModule
{
    public sealed class Definition : AModuleDefinition<MessagingModule>
    {
        public override void RegisterGlobal(ContainerBuilder builder)
        {   
            var mediator = new MediatorBuilder();

            foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                mediator.RegisterHandlers(assembly);
            }

            builder.RegisterMediator(mediator);
        }

        protected override void RegisterModule(ContainerBuilder builder)
        {
            builder.RegisterType<MessagingModule>().AsSelf().SingleInstance();
        }
    }
    public void OnInitialize()
    {
       
    }

    public void OnShutdown()
    {
       
    }
    
    public void Dispose()
    { 
        
    }
}