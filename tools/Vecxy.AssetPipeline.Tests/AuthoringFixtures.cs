using System.Numerics;
using Vecxy.Prototypes;
using Vecxy.Scene;

[Authoring("Test device", Category = "Tests", Key = "device")]
public sealed class AuthoringDevice : AComponent
{
    public sealed class Prototype : APrototype<AuthoringDevice, Prototype.Options>
    {
        public class CommonOptions
        {
            [Authoring("Duration", Min = 0, Unit = "s", Order = 1)]
            public float Duration { get; set; } = 30;
        }
        public sealed class Options : CommonOptions
        {
            [Authoring] public bool Powered { get; set; } = true;
            [Authoring("Offset")] public Vector3 Offset { get; set; } = new(1, 2, 3);
            [Authoring("Mode", Default = "Fast")] public DeviceMode Mode { get; set; } = DeviceMode.Normal;
            public int InternalOnly { get; set; } = 7;
        }
        protected override AuthoringDevice Instantiate(IPrototypeContext context) =>
            RequireContext<ScenePrototypeContext>(context).CreateObject("Device").AddComponent<AuthoringDevice>();
    }
}
public enum DeviceMode { Normal, Fast }
public sealed class NotExposedComponent : AComponent { }
