using System.Text;

namespace Vecxy.Assets._Legacy;

public class TextAsset : Asset, IHotReloadableAsset
{
    public string Content { get; private set; } = string.Empty;
    public override EAssetType Type => EAssetType.Text;

    // 1. Первичная загрузка
    public override void Load(byte[] data)
    {
        Content = Encoding.UTF8.GetString(data);
    }

    // 2. Поддержка Hot Reload (если файл изменили в блокноте)
    public void OnHotReload(byte[] newData)
    {
        Load(newData);
        NotifyReloaded();
    }
}
