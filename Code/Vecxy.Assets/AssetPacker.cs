using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using Newtonsoft.Json;

namespace Vecxy.Assets
{
    /// <summary>
    /// Metadata stored in the Pack Manifest
    /// </summary>
    [Serializable]
    public class AssetMetaInfo
    {
        public ASSET_TYPE Type;
        public string Name;
        public string Path; // Relative (Key)
        [JsonIgnore] public string FullPath; // Temp during packing
        public long Size;
        public long Offset;
        public long CompressedSize;
    }

    [Serializable]
    public class AssetPackManifest
    {
        public string Version;
        public DateTime CreatedAt;
        public List<AssetMetaInfo> AssetsMeta;
    }

    /// <summary>
    /// Simple configuration model parsed from YAML-like .pack file
    /// </summary>
    public class PackConfig
    {
        public string Name { get; set; } = null;
        public bool Compress { get; set; } = true;
    }

    public static class AssetPacker
    {
        /// <summary>
        /// Creates a .vpack file from a source directory.
        /// </summary>
        public static void Pack(string sourceDir, string outputDir)
        {
            if (!Directory.Exists(sourceDir)) throw new DirectoryNotFoundException(sourceDir);
            if (!Directory.Exists(outputDir)) Directory.CreateDirectory(outputDir);

            // 1. Read Config if exists (e.g. Tank.pack)
            var config = ParseConfigInFolder(sourceDir) ?? new PackConfig();

            // Determine Output Filename
            // If name is in valid yaml, use it, else use folder name
            string packName = !string.IsNullOrEmpty(config.Name)
                ? config.Name
                : new DirectoryInfo(sourceDir).Name;

            string outputPath = Path.Combine(outputDir, packName + ".vpack");

            Console.WriteLine($"[AssetPacker] Packing '{sourceDir}' -> '{outputPath}'");
            Console.WriteLine($"[AssetPacker] Name: {packName}, Compress: {config.Compress}");

            // 2. Collect Files
            var metaList = new List<AssetMetaInfo>();
            CollectFiles(sourceDir, sourceDir, metaList);

            // 3. Write Binary
            using (var fs = new FileStream(outputPath, FileMode.Create))
            using (var writer = new BinaryWriter(fs))
            {
                // Header
                writer.Write(Encoding.UTF8.GetBytes("VPACK"));
                writer.Write((int)1); // Version
                var offsetPos = fs.Position;
                writer.Write((long)0); // Placeholder for Manifest Offset

                // Body (Files)
                foreach (var meta in metaList)
                {
                    byte[] rawData = File.ReadAllBytes(meta.FullPath);
                    meta.Offset = fs.Position;

                    using var ms = new MemoryStream();
                    // We wrap in GZipStream regardless. 
                    // If config.Compress is False, we simple use NoCompression mode for unified reading logic.
                    var compLevel = config.Compress ? CompressionLevel.Optimal : CompressionLevel.NoCompression;

                    using (var gs = new GZipStream(ms, compLevel))
                    {
                        gs.Write(rawData, 0, rawData.Length);
                    }

                    var blob = ms.ToArray();
                    meta.CompressedSize = blob.Length;
                    writer.Write(blob);
                }

                // Footer (Manifest)
                long manifestStart = fs.Position;
                var manifest = new AssetPackManifest
                {
                    Version = "1.0",
                    CreatedAt = DateTime.UtcNow,
                    AssetsMeta = metaList
                };

                string json = JsonConvert.SerializeObject(manifest, Formatting.Indented);
                writer.Write(Encoding.UTF8.GetBytes(json));

                // Backfill Offset
                fs.Seek(offsetPos, SeekOrigin.Begin);
                writer.Write(manifestStart);
            }

            Console.WriteLine($"[AssetPacker] Success.");
        }

        private static void CollectFiles(string current, string baseDir, List<AssetMetaInfo> list)
        {
            foreach (var file in Directory.GetFiles(current))
            {
                var ext = Path.GetExtension(file).ToLower();
                // Skip system files and the config file itself
                if (ext == ".meta" || ext == ".ds_store" || ext == ".pack") continue;

                list.Add(new AssetMetaInfo
                {
                    Name = Path.GetFileNameWithoutExtension(file),
                    Path = Path.GetRelativePath(baseDir, file).Replace("\\", "/"),
                    FullPath = file,
                    Size = new FileInfo(file).Length,
                    Type = ExtensionToType(ext)
                });
            }

            foreach (var d in Directory.GetDirectories(current))
            {
                CollectFiles(d, baseDir, list);
            }
        }

        private static PackConfig ParseConfigInFolder(string dir)
        {
            // Find any file ending in .pack
            var files = Directory.GetFiles(dir, "*.pack");
            if (files.Length == 0) return null;

            // Use the first one found
            var cfgPath = files[0];
            var conf = new PackConfig();

            try
            {
                foreach (var line in File.ReadAllLines(cfgPath))
                {
                    var seg = line.Trim();
                    if (string.IsNullOrEmpty(seg) || seg.StartsWith("#")) continue;

                    var parts = seg.Split(new[] { ':' }, 2);
                    if (parts.Length < 2) continue;

                    var key = parts[0].Trim().ToLower();
                    var val = parts[1].Trim().Trim('"', '\'');

                    if (key == "name") conf.Name = val;
                    if (key == "compress")
                    {
                        if (bool.TryParse(val, out var c))
                        {
                            conf.Compress = c;
                        }
                    }
                }
            }
            catch { /* Warning? */ }

            return conf;
        }

        private static ASSET_TYPE ExtensionToType(string ext)
        {
            return ext switch
            {
                ".txt" or ".json" or ".xml" or ".yaml" => ASSET_TYPE.TEXT,
                ".png" or ".jpg" or ".tga" => ASSET_TYPE.TEXTURE,
                ".obj" or ".fbx" => ASSET_TYPE.MODEL,
                _ => ASSET_TYPE.UNDEFINED
            };
        }
    }
}