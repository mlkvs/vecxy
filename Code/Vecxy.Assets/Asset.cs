using System.Runtime.Serialization;

namespace Vecxy.Assets;

public enum ASSET_TYPE
{
    TEXT,
    SHADER,
    MATERIAL,
}

public interface IHotReloadable
{
    void OnHotReload(byte[] newData);
}

public interface IAssetsManager
{
    public AssetPack LoadPack(string path)
    {
        throw new NotImplementedException();
    }
}

public class AssetPack
{
    public TAsset LoadAsset<TAsset>(string path) where TAsset : Asset
    {
        // Здесь должна быть логика загрузки ассета из пакета
        throw new NotImplementedException();
    }
}

// Обновим базовый класс, чтобы можно было устанавливать значения
public abstract class Asset
{
    // Добавим init или protected set, чтобы менеджер мог их заполнить
    public Guid Id { get; protected set; } = Guid.NewGuid();
    public abstract ASSET_TYPE Type { get; }
    public string Name { get; protected set; }
    public string Path { get; protected set; }
    public HashSet<string> Dependencies { get; } = new(StringComparer.OrdinalIgnoreCase);

    public abstract void Load(byte[] data);

    // Вспомогательный метод для инициализации базовых полей
    public void Initialize(string path)
    {
        Path = path;
        Name = System.IO.Path.GetFileNameWithoutExtension(path);
    }
}

internal class AssetRecord
{
    public string FullPath { get; set; }
    public DateTime LastWriteTime { get; set; }
    public Asset AssetInstance { get; set; }
}

// Реализация TextAsset остается прежней
public class TextAsset : Asset, IHotReloadable
{
    public override ASSET_TYPE Type => ASSET_TYPE.TEXT;
    public string Content { get; private set; }

    public override void Load(byte[] data)
    {
        Content = System.Text.Encoding.UTF8.GetString(data);
        // Текст зависит от самого себя
        Dependencies.Add(Path);
    }

    public void OnHotReload(byte[] newData)
    {
        Content = System.Text.Encoding.UTF8.GetString(newData);
        Console.WriteLine($"[HotReload] TextAsset updated: {Path} | New Content: {Content.Length} chars");
    }
}
public class AssetManager
{
    private readonly string _assetsRoot;
    private readonly bool _isDev;
    private readonly Dictionary<string, AssetRecord> _records = new(StringComparer.OrdinalIgnoreCase);
    private Timer _debounceTimer;
    private const int DebounceMs = 500;

    public AssetManager(string rootPath, bool isDev)
    {
        _assetsRoot = Path.GetFullPath(rootPath);
        _isDev = isDev;

        // Сначала сканируем всё что есть
        PerformFullScan();

        if (_isDev)
        {
            SetupWatcher();
        }
    }

    private void SetupWatcher()
    {
        var watcher = new FileSystemWatcher(_assetsRoot, "*.*")
        {
            IncludeSubdirectories = true,
            EnableRaisingEvents = true
        };

        FileSystemEventHandler handler = (s, e) => ResetDebounce();
        watcher.Changed += handler;
        watcher.Created += handler;
        watcher.Deleted += handler;
        watcher.Renamed += (s, e) => ResetDebounce();

        _debounceTimer = new Timer(_ => PerformFullScan(), null, Timeout.Infinite, Timeout.Infinite);
    }

    private void ResetDebounce()
    {
        _debounceTimer.Change(DebounceMs, Timeout.Infinite);
    }

    public void PerformFullScan()
    {
        if (!Directory.Exists(_assetsRoot)) return;

        Console.WriteLine("[AssetManager] Scanning...");
        var files = Directory.GetFiles(_assetsRoot, "*.*", SearchOption.AllDirectories);

        foreach (var filePath in files)
        {
            // Работаем с относительным путем для удобства (зависит от твоих требований)
            // string relativePath = Path.GetRelativePath(_assetsRoot, filePath);

            var lastWrite = File.GetLastWriteTime(filePath);

            if (_records.TryGetValue(filePath, out var record))
            {
                if (lastWrite > record.LastWriteTime)
                {
                    record.LastWriteTime = lastWrite;
                    ReloadAsset(record, filePath);
                }
            }
            else
            {
                RegisterNewFile(filePath, lastWrite);
            }
        }
    }

    private void RegisterNewFile(string path, DateTime writeTime)
    {
        string ext = Path.GetExtension(path).ToLower();
        Asset asset = null;

        // Определяем тип ассета по расширению
        if (ext == ".txt")
        {
            asset = new TextAsset();
        }

        if (asset != null)
        {
            // 1. Инициализируем системные поля
            asset.Initialize(path);

            // 2. Читаем данные. 
            // Используем FileStream с FileShare.ReadWrite, чтобы не конфликтовать с IDE
            byte[] data = ReadFileDataWithRetry(path);

            if (data != null)
            {
                asset.Load(data);

                _records[path] = new AssetRecord
                {
                    FullPath = path,
                    LastWriteTime = writeTime,
                    AssetInstance = asset
                };
                Console.WriteLine($"[AssetManager] Loaded {asset.Type}: {asset.Name}");
            }
        }
    }

    private void ReloadAsset(AssetRecord record, string filePath)
    {
        byte[] data = ReadFileDataWithRetry(filePath);
        if (data == null) return;

        // Прямое обновление ассета
        if (record.AssetInstance is IHotReloadable reloadable)
        {
            reloadable.OnHotReload(data);
        }

        // Обновление зависимых ассетов (те, у кого этот путь в Dependencies)
        foreach (var r in _records.Values)
        {
            // Если ассет 'r' зависит от файла 'filePath'
            if (r.AssetInstance.Dependencies.Contains(filePath) && r.AssetInstance != record.AssetInstance)
            {
                if (r.AssetInstance is IHotReloadable depReloadable)
                {
                    // В реальном шейдере здесь была бы более сложная логика подхвата новых данных
                    // Сейчас просто уведомляем о перезагрузке
                    depReloadable.OnHotReload(null);
                }
            }
        }
    }

    // Хелпер для борьбы с блокировками файлов ОС
    private byte[] ReadFileDataWithRetry(string path)
    {
        for (int i = 0; i < 5; i++)
        {
            try
            {
                return File.ReadAllBytes(path);
            }
            catch (IOException)
            {
                Thread.Sleep(100); // Ждем чуть-чуть, пока IDE отпустит файл
            }
        }
        return null;
    }

    // Публичный метод получения
    public T Get<T>(string path) where T : Asset
    {
        // Приводим путь к полному, чтобы найти в словаре
        string fullPath = Path.GetFullPath(Path.Combine(_assetsRoot, path));
        if (_records.TryGetValue(fullPath, out var record))
        {
            return record.AssetInstance as T;
        }
        return null;
    }
}