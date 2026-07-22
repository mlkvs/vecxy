using System.Text;
namespace Vecxy.Assets;

public class TextAsset : Asset, IHotReloadableAsset
{
    public string Content { get; private set; } = string.Empty;
    public override ASSET_TYPE Type => ASSET_TYPE.TEXT;

    // 1. Первичная загрузка
    public override void Load(byte[] data)
    {
        Content = Encoding.UTF8.GetString(data);
    }

    // 2. Поддержка Hot Reload (если файл изменили в блокноте)
    public void OnHotReload(byte[] newData)
    {
        Load(newData);
        // Тут можно кинуть событие, например OnTextChanged?.Invoke();
        System.Console.WriteLine($"[TextAsset] New content loaded: {Content[..Math.Min(10, Content.Length)]}...");
    }
}
