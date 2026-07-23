namespace Vecxy.Assets
{
    public enum EAssetType
    {
        Undefined, 
        Text, 
        Texture, 
        Model,
    }

    public interface IHotReloadableAsset
    {
        void OnHotReload(byte[] newData);
    }

    [Serializable]
    public abstract class Asset
    {
        public event Action<Asset>? Reloaded;
        public Guid Id { get; protected set; } = Guid.NewGuid();
        public abstract EAssetType Type { get; }
        public string Name { get; protected set; } = string.Empty;
        public string Path { get; protected set; } = string.Empty; // Relative Path

        public HashSet<string> Dependencies { get; } = new(StringComparer.OrdinalIgnoreCase);

        /// <summary>
        /// Initial load from binary data.
        /// </summary>
        public abstract void Load(byte[] data);

        /// <summary>
        /// Sets metadata.
        /// </summary>
        public void Initialize(string path)
        {
            Path = path.Replace("\\", "/");
            Name = System.IO.Path.GetFileNameWithoutExtension(path);
        }

        protected void NotifyReloaded() => Reloaded?.Invoke(this);
    }
}
