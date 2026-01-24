using System.Globalization;
using Vecxy.Diagnostics;
using Vecxy.Engine;

namespace Vecxy.Sandbox;

public class SandboxLayer : AppLayer
{
    public override void OnInitialize()
    {
        Logger.Info(App.Info.Name);
    }

    public override void OnTick(float dt)
    {
        Logger.Info("dt: " + dt.ToString(CultureInfo.InvariantCulture));
    }
}
