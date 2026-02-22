
using Autofac;
using Vecxy.Kernel;
using Vecxy.Diagnostics;

namespace Vecxy.Assets
{
    public enum AssetLoadMode
    {
        /// <summary>
        /// Reads files directly from disk. Enables Hot Reloading.
        /// </summary>
        LooseFiles,
        /// <summary>
        /// Reads from a compiled .vpack file. fast, compressed, read-only.
        /// </summary>
        Packed
    }

    public class AssetsModule : IModule
    {
        public AssetsManager Manager { get; private set; }

        // Configuration
        public AssetLoadMode LoadMode { get; set; }
        public string SourcePath { get; set; } // Path to Folder or Path to .vpack file

        public void OnLoad(Autofac.ILifetimeScope scope)
        {
            // 1. Determine Paths intelligently if not set manually
            if (string.IsNullOrEmpty(SourcePath))
            {
                var baseDir = AppDomain.CurrentDomain.BaseDirectory;

                // Default logic: Try to find "AssetsData" folder
                // In Dev environment (Source)
                var devPath = Path.GetFullPath(Path.Combine(baseDir, "../../../AssetsData"));
                // In Prod environment (Next to Exe)
                var prodPath = Path.Combine(baseDir, "AssetsData");

                if (LoadMode == AssetLoadMode.LooseFiles)
                {
                    // Prefer Dev path if exists, else Prod path
                    SourcePath = Directory.Exists(devPath) ? devPath : prodPath;
                }
                else
                {
                    // For packed, we look for the file inside the prod folder usually
                    SourcePath = Path.Combine(prodPath, "data.vpack");
                }
            }

            // 2. Initialize Manager
            Manager = new AssetsManager();

            // 3. Mount the container based on Mode
            try
            {
                if (LoadMode == AssetLoadMode.LooseFiles)
                {
                    if (!Directory.Exists(SourcePath)) Directory.CreateDirectory(SourcePath);

                    // Mount Folder (Editable, Watchable)
                    var container = new FileSystemContainer("MainAssets", SourcePath);
                    Manager.Mount(container);
                    Logger.Info($"[AssetsModule] Mounted FileSystem: {SourcePath}");
                }
                else
                {
                    // Mount Pack (Read-Only, Optimized)
                    if (File.Exists(SourcePath))
                    {
                        var container = PackedContainer.Load(SourcePath);
                        Manager.Mount(container);
                        Logger.Info($"[AssetsModule] Mounted Pack: {SourcePath}");
                    }
                    else
                    {
                        Logger.Error($"[AssetsModule] Pack file not found: {SourcePath}");
                    }
                }
            }
            catch (Exception ex)
            {
                Logger.Error($"[AssetsModule] Failed to mount assets: {ex.Message}");
            }
        }

        public void OnInitialize()
        {
            // AssetsManager doesn't need explicit Initialize if Mount is used, 
            // but we can use it for pre-warming or validation.
            Manager.Initialize();
        }

        public void OnTick(float deltaTime)
        {
            Manager.Update();
        }

        public void OnFrame()
        {
            throw new NotImplementedException();
        }

        public void OnUnload()
        {
            Manager?.Dispose();
            Logger.Info("[AssetsModule] Unloaded.");
        }

        public void Dispose()
        {
            Manager?.Dispose();
        }
    }
}