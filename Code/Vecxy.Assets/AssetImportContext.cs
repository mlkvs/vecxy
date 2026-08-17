namespace Vecxy.Assets;

public sealed class AssetImportContext
{
    private readonly IAssetsManager _assets;
    private readonly IReadOnlyList<string> _assetDirectories;

    public string AssetsDirectory { get; }

    internal AssetImportContext(
        string assetsDirectory,
        IReadOnlyList<string> assetDirectories,
        IAssetsManager assets)
    {
        AssetsDirectory = assetsDirectory;
        _assetDirectories = assetDirectories;
        _assets = assets;
    }

    public string GetFullPath(string relativePath)
    {
        var normalized = AssetsModule.NormalizePath(relativePath);
        var primaryPath = GetValidatedPath(AssetsDirectory, normalized, relativePath);
        if (File.Exists(primaryPath))
            return primaryPath;

        foreach (var directory in _assetDirectories)
        {
            var candidate = GetValidatedPath(directory, normalized, relativePath);
            if (File.Exists(candidate))
                return candidate;
        }

        return primaryPath;
    }

    private static string GetValidatedPath(
        string directory,
        string normalized,
        string originalPath)
    {
        var fullPath = Path.GetFullPath(Path.Combine(directory, normalized));
        var relative = Path.GetRelativePath(directory, fullPath);

        if (relative == ".." ||
            relative.StartsWith($"..{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
        {
            throw new InvalidOperationException($"Asset path escapes the assets directory: {originalPath}");
        }

        return fullPath;
    }

    public string ReadAllText(string relativePath) =>
        File.ReadAllText(GetFullPath(relativePath));

    internal IReadOnlyList<string> ReadAllTextLayers(string relativePath)
    {
        var normalized = AssetsModule.NormalizePath(relativePath);
        var sources = new List<string>();

        foreach (var directory in new[] { AssetsDirectory }.Concat(_assetDirectories))
        {
            var path = GetValidatedPath(directory, normalized, relativePath);
            if (File.Exists(path))
                sources.Add(File.ReadAllText(path));
        }

        return sources;
    }

    public byte[] ReadAllBytes(string relativePath) =>
        File.ReadAllBytes(GetFullPath(relativePath));

    public AssetRef<T> Load<T>(string path) where T : class =>
        _assets.Load<T>(path);
}
