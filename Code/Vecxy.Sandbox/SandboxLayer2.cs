using System.Globalization;
using Autofac;
using Vecxy.Assets;
using Vecxy.Assets.V2;
using Vecxy.Diagnostics;
using Vecxy.Engine;
using Vecxy.Kernel;
using static System.Net.Mime.MediaTypeNames;

namespace Vecxy.Sandbox;


public class SandboxLayer2 : AppLayer
{
    public Test Testss { get; set; }


    public override void OnBindings(ContainerBuilder builder)
    {
    }

    public override void OnInitialize()
    {
      
    }

    public override void OnTick(float dt)
    {
        //Logger.Info("dt: " + dt.ToString(CultureInfo.InvariantCulture));
    }
}
