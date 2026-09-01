namespace Vecxy.Platforms;

public sealed record PlatformContext(
    PlatformKind Platform,
    string AssetsDirectory,
    Func<string, Stream>? OpenBootstrapAsset = null)
{
    public Stream OpenAsset(string relativePath) => OpenBootstrapAsset is not null
        ? OpenBootstrapAsset(relativePath)
        : File.OpenRead(Path.Combine(AssetsDirectory, relativePath));
}

internal static class DesktopBootstrapAssets
{
    public static Func<string, Stream>? CreateReader(string directory)
    {
        var assetManifestPath = Path.Combine(directory, "Assets.manifest");
        var packageManifestPath = Path.Combine(directory, "packages.manifest");
        if (!File.Exists(assetManifestPath) || !File.Exists(packageManifestPath))
            return null;

        var assets = Vecxy.Assets.AssetManifest.Load(assetManifestPath).Assets
            .ToDictionary(entry => Normalize(entry.Path), StringComparer.OrdinalIgnoreCase);
        var packages = System.Text.Json.JsonSerializer.Deserialize<Vecxy.Assets.VPackBuildManifest>(
                           File.ReadAllText(packageManifestPath),
                           Vecxy.Assets.AssetManifest.SerializerOptions)
                       ?? throw new InvalidDataException($"Package manifest is empty: {packageManifestPath}");
        var packageFiles = packages.Packages.ToDictionary(entry => entry.Id, entry => entry.File);

        return path =>
        {
            if (!assets.TryGetValue(Normalize(path), out var asset))
                throw new FileNotFoundException($"Bootstrap asset '{path}' is not present in Assets.manifest.", path);
            if (!packageFiles.TryGetValue(asset.Package, out var packageFile))
                throw new FileNotFoundException($"Package {asset.Package} for bootstrap asset '{path}' is not bundled.");

            var packagePath = Path.IsPathFullyQualified(packageFile)
                ? packageFile
                : Path.Combine(directory, packageFile);
            var reader = Vecxy.Assets.VPackReader.OpenAsync(File.OpenRead(packagePath))
                .GetAwaiter().GetResult();
            try
            {
                if (reader.Package != asset.Package)
                    throw new InvalidDataException($"VPack package ID mismatch in {packagePath}.");
                var bytes = reader.ReadAssetAsync(new Vecxy.Assets.AssetId(asset.Id))
                    .AsTask().GetAwaiter().GetResult();
                return new MemoryStream(bytes.ToArray(), writable: false);
            }
            finally
            {
                reader.DisposeAsync().AsTask().GetAwaiter().GetResult();
            }
        };
    }

    private static string Normalize(string path) => path.Replace('\\', '/').TrimStart('/');
}
