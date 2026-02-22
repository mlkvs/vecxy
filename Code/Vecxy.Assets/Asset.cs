using System;
using System.Collections.Generic;
using Newtonsoft.Json;

namespace Vecxy.Assets
{
    public enum ASSET_TYPE { UNDEFINED, TEXT, TEXTURE, MODEL, AUDIO }

    public interface IHotReloadableAsset
    {
        void OnHotReload(byte[] newData);
    }

    [Serializable]
    public abstract class Asset
    {
        public Guid Id { get; protected set; } = Guid.NewGuid();
        public abstract ASSET_TYPE Type { get; }
        public string Name { get; protected set; }
        public string Path { get; protected set; } // Relative Path

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
    }
}