using Vecxy.Assets;

namespace Vecxy.Rendering;

public abstract class APostProcessConfig : IYamlConfig
{
    public bool Enabled { get; set; } = true;
    public int Order { get; set; }

    public abstract void Validate();
}