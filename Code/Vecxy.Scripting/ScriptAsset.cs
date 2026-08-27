using Vecxy.Assets;

namespace Vecxy.Scripting;

public sealed class ScriptAsset
{
    public required string Source { get; init; }
    public required string Path { get; init; }
}

public sealed class LuauAssetImporter : IAssetImporter<ScriptAsset>
{
    public IReadOnlyCollection<string> Extensions => [".luau"];

    public ScriptAsset Import(AssetMetadata metadata, AssetImportContext context) => new()
    {
        Source = context.ReadAllText(metadata.Path),
        Path = metadata.Path
    };
}
