using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using Newtonsoft.Json;
using Vecxy.Diagnostics;

namespace Vecxy.Assets
{
    /// <summary>
    /// Abstract base class for any asset source (Folder, Zip, VPack, Network, etc.)
    /// </summary>
    public abstract class AssetContainer : IDisposable
    {
        public string Name { get; protected set; }

        /// <summary>
        /// Reads raw bytes of the asset. Returns null if not found.
        /// </summary>
        public abstract byte[] LoadBytes(string relativePath);

        /// <summary>
        /// Checks if asset exists in this container.
        /// </summary>
        public abstract bool Contains(string relativePath);

        public abstract void Dispose();
    }

    /// <summary>
    /// Implementation for loose files on disk (Dev Mode).
    /// </summary>
    public class FileSystemContainer : AssetContainer
    {
        public string RootPath { get; }

        public FileSystemContainer(string name, string rootPath)
        {
            Name = name;
            RootPath = Path.GetFullPath(rootPath);
        }

        public override bool Contains(string relativePath)
        {
            var fullPath = Path.Combine(RootPath, relativePath);
            return File.Exists(fullPath);
        }

        public override byte[] LoadBytes(string relativePath)
        {
            var fullPath = Path.Combine(RootPath, relativePath);
            if (!File.Exists(fullPath)) return null;

            // Retry logic for file locking conflicts (common in Editors)
            for (int i = 0; i < 5; i++)
            {
                try
                {
                    return File.ReadAllBytes(fullPath);
                }
                catch (IOException)
                {
                    System.Threading.Thread.Sleep(50);
                }
            }
            return null;
        }

        public override void Dispose() { /* Nothing to dispose */ }
    }

    /// <summary>
    /// Implementation for binary packed files (Prod Mode).
    /// </summary>
    public class PackedContainer : AssetContainer
    {
        private const string SIGNATURE = "VPACK";
        private FileStream _stream;
        private readonly object _lock = new object();
        private Dictionary<string, AssetMetaInfo> _metaLookup;
        public AssetPackManifest Manifest { get; private set; }

        private PackedContainer(string name, FileStream stream, AssetPackManifest manifest)
        {
            Name = name;
            _stream = stream;
            Manifest = manifest;
            // Case-insensitive lookup, forward slashes
            _metaLookup = manifest.AssetsMeta.ToDictionary(
                m => m.Path.Replace("\\", "/"),
                m => m,
                StringComparer.OrdinalIgnoreCase
            );
        }

        public static PackedContainer Load(string packPath)
        {
            if (!File.Exists(packPath)) throw new FileNotFoundException("Pack not found", packPath);

            var fs = new FileStream(packPath, FileMode.Open, FileAccess.Read, FileShare.Read);
            var reader = new BinaryReader(fs);

            try
            {
                var sig = Encoding.UTF8.GetString(reader.ReadBytes(5));
                if (sig != SIGNATURE) throw new Exception("Invalid pack signature");

                reader.ReadInt32(); // Version
                var manifestPos = reader.ReadInt64();

                // Read Manifest
                fs.Seek(manifestPos, SeekOrigin.Begin);
                var jsonBytes = reader.ReadBytes((int)(fs.Length - manifestPos));
                var manifest = JsonConvert.DeserializeObject<AssetPackManifest>(Encoding.UTF8.GetString(jsonBytes));

                return new PackedContainer(Path.GetFileNameWithoutExtension(packPath), fs, manifest);
            }
            catch
            {
                fs.Dispose();
                throw;
            }
        }

        public override bool Contains(string relativePath)
        {
            return _metaLookup.ContainsKey(relativePath.Replace("\\", "/"));
        }

        public override byte[] LoadBytes(string relativePath)
        {
            var path = relativePath.Replace("\\", "/");
            if (!_metaLookup.TryGetValue(path, out var meta)) return null;

            lock (_lock)
            {
                _stream.Seek(meta.Offset, SeekOrigin.Begin);
                var buffer = new byte[meta.CompressedSize];
                var read = _stream.Read(buffer, 0, (int)meta.CompressedSize);

                if (meta.CompressedSize == 0) return Array.Empty<byte>();

                // Always decompress assuming the packer compressed it (or stored it in GZip format)
                using var ms = new MemoryStream(buffer);
                using var gs = new GZipStream(ms, CompressionMode.Decompress);
                using var output = new MemoryStream();
                gs.CopyTo(output);
                return output.ToArray();
            }
        }

        public override void Dispose()
        {
            _stream?.Dispose();
        }
    }
}