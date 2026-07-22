using System;
using System.Collections.Generic;
using Newtonsoft.Json;

namespace Vecxy.Assets
{
    // Add comment for test git
    public enum ASSET_TYPE { UNDEFINED, TEXT, TEXTURE, MODEL, AUDIO }

    public interface IHotReloadableAsset
    {
        void OnHotReload(byte[] newData);
    }

    [Serializable]
    public abstract class Asset
    {
        public event Action<Asset>? Reloaded;
        public Guid Id { get; protected set; } = Guid.NewGuid();
        public abstract ASSET_TYPE Type { get; }
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
