namespace Vecxy.Assets;

public sealed class AssetImportContext
{
    private readonly IAssetsManager _assets;

    public string AssetsDirectory { get; }

    internal AssetImportContext(string assetsDirectory, IAssetsManager assets)
    {
        AssetsDirectory = assetsDirectory;
        _assets = assets;
    }

    public string GetFullPath(string relativePath)
    {
        var normalized = AssetsModule.NormalizePath(relativePath);
        var fullPath = Path.GetFullPath(Path.Combine(AssetsDirectory, normalized));
        var relative = Path.GetRelativePath(AssetsDirectory, fullPath);

        if (relative == ".." ||
            relative.StartsWith($"..{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
        {
            throw new InvalidOperationException($"Asset path escapes the assets directory: {relativePath}");
        }

        return fullPath;
    }

    public string ReadAllText(string relativePath) =>
        File.ReadAllText(GetFullPath(relativePath));

    public byte[] ReadAllBytes(string relativePath) =>
        File.ReadAllBytes(GetFullPath(relativePath));

    public AssetRef<T> Load<T>(string path) where T : class =>
        _assets.Load<T>(path);
}
