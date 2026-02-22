using System.Globalization;
using Autofac;
using Vecxy.Assets;
using Vecxy.Assets.V2;
using Vecxy.Diagnostics;
using Vecxy.Engine;
using Vecxy.Kernel;
using static System.Net.Mime.MediaTypeNames;

namespace Vecxy.Sandbox;

public class Test
{
    public IAssetsManager AssetsManager { get; set; }

    public string TestS()
    {
        return "asdasd!!!!!";
    }
}

public class SandboxLayer : AppLayer
{
    public IAssetsManager AssetsManager { get; set; }
    public Test Testss { get; set; }

    public override void OnBindings(ContainerBuilder builder)
    {
        builder
            .RegisterType<Test>()
            .AsSelf()
            .PropertiesAutowired()
            .SingleInstance();
    }
    
    public override void OnInitialize()
    {
        AssetsManager.LoadPack("asdasd");
        Logger.Info(Testss.TestS());
        //AssetPacker.Pack("Assets", "Assets.vpack");

        //var t = AssetsManager.Instance.Get<TextAsset>("Test.txt");

        //Logger.Info(t.Content);

        //var pack = AssetPack.Load("Assets.vcxpack");

        //var text = pack.Load<TextAsset>("Some/Note.txt");

        //Logger.Info(text.Content);
    }

    public override void OnTick(float dt)
    {
        //Logger.Info("dt: " + dt.ToString(CultureInfo.InvariantCulture));
    }
}
