using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using Vecxy.Kernel;
using Vecxy.Diagnostics;

namespace Vecxy.Assets
{
    public class AssetsManager : IDisposable
    {
        public static AssetsManager? Instance { get; private set; }

        private readonly Dictionary<string, Asset> _loadedAssets = new(StringComparer.OrdinalIgnoreCase);
        // List of mounted containers (First Checked Priority -> Last)
        private readonly List<AssetContainer> _containers = new();

        // Hot Reload Stuff
        private FileSystemWatcher? _watcher;
        private readonly ConcurrentQueue<string> _changedQueue = new();

        public AssetsManager()
        {
            Instance = this;
        }

        public void Mount(AssetContainer container)
        {
            _containers.Add(container);

            // If this is a file system container, we can watch it for changes
            if (container is FileSystemContainer fsContainer)
            {
                SetupWatcher(fsContainer.RootPath);
            }
        }

        public void Initialize()
        {
            // Pre-loading logic could go here
        }

        public void Update()
        {
            // Process Hot Reload events on Main Thread
            if (_changedQueue.IsEmpty) return;

            while (_changedQueue.TryDequeue(out var fullPath))
            {
                ProcessFileChange(fullPath);
            }
        }

        public T? Get<T>(string relativePath) where T : Asset, new()
        {
            // Normalize
            relativePath = relativePath.Replace("\\", "/");

            // 1. Cache
            if (_loadedAssets.TryGetValue(relativePath, out var cached))
            {
                return cached as T;
            }

            // 2. Find in Containers
            byte[]? data = null;
            foreach (var container in _containers)
            {
                if (container.Contains(relativePath))
                {
                    data = container.LoadBytes(relativePath);
                    if (data != null) break; // Found it
                }
            }

            if (data == null)
            {
                Logger.Error($"[AssetsManager] Asset not found: {relativePath}");
                return null;
            }

            // 3. Create & Deserialize
            var asset = new T();
            asset.Initialize(relativePath);
            try
            {
                asset.Load(data);
                _loadedAssets[relativePath] = asset;
                return asset;
            }
            catch (Exception ex)
            {
                Logger.Error($"[AssetsManager] Failed to load asset '{relativePath}': {ex.Message}");
                return null;
            }
        }

        public IReadOnlyList<string> EnumeratePaths()
        {
            var paths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var container in _containers)
            {
                if (container is FileSystemContainer fileSystem)
                    foreach (var file in Directory.EnumerateFiles(fileSystem.RootPath, "*", SearchOption.AllDirectories))
                        paths.Add(Path.GetRelativePath(fileSystem.RootPath, file).Replace('\\', '/'));
                else if (container is PackedContainer packed)
                    foreach (var asset in packed.Manifest.AssetsMeta) paths.Add(asset.Path.Replace('\\', '/'));
            }
            return paths.OrderBy(x => x, StringComparer.OrdinalIgnoreCase).ToArray();
        }

        #region Watcher Logic

        private void SetupWatcher(string dir)
        {
            if (_watcher != null) return; // Only one watcher for now (or support multiple)

            _watcher = new FileSystemWatcher(dir, "*.*")
            {
                IncludeSubdirectories = true,
                NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.FileName | NotifyFilters.Size
            };

            FileSystemEventHandler handler = (s, e) => OnFileEvent(e.FullPath);
            _watcher.Changed += handler;
            _watcher.Created += handler;
            _watcher.Renamed += (s, e) => OnFileEvent(e.FullPath);
            _watcher.EnableRaisingEvents = true;
            Logger.Info($"[AssetsManager] Hot Reload active for: {dir}");
        }

        private void OnFileEvent(string fullPath)
        {
            _changedQueue.Enqueue(fullPath);
        }

        private void ProcessFileChange(string fullPath)
        {
            // We need to resolve fullPath -> relativePath used in keys
            // This assumes we can match it against our FileSystemContainers

            string? relativePath = null;
            FileSystemContainer? ownerContainer = null;

            foreach (var container in _containers)
            {
                if (container is FileSystemContainer fs)
                {
                    if (fullPath.StartsWith(fs.RootPath, StringComparison.OrdinalIgnoreCase))
                    {
                        relativePath = fullPath.Substring(fs.RootPath.Length).TrimStart(Path.DirectorySeparatorChar, '/').Replace("\\", "/");
                        ownerContainer = fs;
                        break;
                    }
                }
            }

            if (relativePath == null || ownerContainer == null) return;

            // Only reload if it is currently loaded/cached
            if (_loadedAssets.TryGetValue(relativePath, out var asset))
            {
                Logger.Info($"[HotReload] detected: {relativePath}");

                // Read from disk immediately
                byte[]? newData = ownerContainer.LoadBytes(relativePath);

                if (newData != null && asset is IHotReloadableAsset reloadable)
                {
                    try
                    {
                        reloadable.OnHotReload(newData);
                        NotifyDependencies(asset.Path);
                    }
                    catch (Exception exception)
                    {
                        Logger.Error(exception, $"[HotReload] Failed to reload '{relativePath}', keeping previous asset");
                    }
                }
            }
        }

        private void NotifyDependencies(string changedAssetPath)
        {
            foreach (var loaded in _loadedAssets.Values)
            {
                if (loaded.Dependencies.Contains(changedAssetPath))
                {
                    Logger.Info($"[HotReload] Dependency update: {loaded.Name} depends on {changedAssetPath}");
                    // In a complex system, we might re-trigger OnHotReload for the parent, 
                    // or call a specific 'OnDependencyChanged'.
                }
            }
        }

        #endregion

        public void Dispose()
        {
            _watcher?.Dispose();
            foreach (var c in _containers) c.Dispose();
            _loadedAssets.Clear();
            _containers.Clear();
        }
    }
}
