using System.Text;

namespace Vecxy.Assets;

public sealed class ShaderAsset
{
    public TextAsset Vertex { get; }
    public TextAsset Fragment { get; }

    internal ShaderAsset(TextAsset vertex, TextAsset fragment)
    {
        Vertex = vertex;
        Fragment = fragment;
    }
}

public sealed class ShaderAssetImporter : IAssetImporter<ShaderAsset>
{
    private const string TypeDirective = "#type";

    public IReadOnlyCollection<string> Extensions { get; } = [".glsl"];

    public ShaderAsset Import(
        AssetMetadata metadata,
        AssetImportContext context)
    {
        var source = context.ReadAllText(metadata.Path);
        var stages = SplitStages(source, metadata.Path);

        if (!stages.TryGetValue(EShaderStage.Vertex, out var vertex))
        {
            throw new InvalidDataException(
                $"Shader '{metadata.Path}' has no vertex stage.");
        }

        if (!stages.TryGetValue(EShaderStage.Fragment, out var fragment))
        {
            throw new InvalidDataException(
                $"Shader '{metadata.Path}' has no fragment stage.");
        }

        return new ShaderAsset(
            new TextAsset { Content = vertex },
            new TextAsset { Content = fragment });
    }

    private static Dictionary<EShaderStage, string> SplitStages(
        string source,
        string path)
    {
        var stages = new Dictionary<EShaderStage, string>();
        EShaderStage? currentStage = null;
        StringBuilder? currentSource = null;

        using var reader = new StringReader(source);
        while (reader.ReadLine() is { } line)
        {
            var trimmed = line.Trim();
            if (trimmed.StartsWith(TypeDirective, StringComparison.Ordinal))
            {
                if (currentStage is not null && currentSource is not null)
                {
                    AddStage(stages, currentStage.Value, currentSource.ToString(), path);
                }

                currentStage = ParseStage(trimmed[TypeDirective.Length..].Trim(), path);
                currentSource = new StringBuilder();
                continue;
            }

            currentSource?.AppendLine(line);
        }

        if (currentStage is not null && currentSource is not null)
        {
            AddStage(stages, currentStage.Value, currentSource.ToString(), path);
        }

        return stages;
    }

    private static EShaderStage ParseStage(string value, string path) =>
        value switch
        {
            "vertex" => EShaderStage.Vertex,
            "fragment" or "pixel" => EShaderStage.Fragment,
            _ => throw new InvalidDataException(
                $"Shader '{path}' contains unknown stage '{value}'.")
        };

    private static void AddStage(
        IDictionary<EShaderStage, string> stages,
        EShaderStage stage,
        string source,
        string path)
    {
        if (!stages.TryAdd(stage, source))
        {
            throw new InvalidDataException(
                $"Shader '{path}' contains duplicate {stage} stage.");
        }
    }

    private enum EShaderStage : byte
    {
        Vertex,
        Fragment
    }
}
