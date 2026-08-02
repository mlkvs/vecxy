using System.Globalization;
using System.Xml.Linq;
using StbTrueTypeSharp;
using Vecxy.Assets;
using static StbTrueTypeSharp.StbTrueType;

namespace Vecxy.UI;

public sealed class UiFontAsset : IDisposable
{
    internal AssetRef<TextureAsset>? TextureReference { get; }
    internal TextureAsset? EmbeddedTexture { get; }
    internal IReadOnlyDictionary<int, UiFontGlyph> Glyphs { get; }
    internal IReadOnlyDictionary<long, float> Kernings { get; }
    public string Family { get; }
    public float SourceSize { get; }
    public float LineHeight { get; }
    internal int TextureWidth => EmbeddedTexture?.Width ?? TextureReference?.Value.Width ?? 1;
    internal int TextureHeight => EmbeddedTexture?.Height ?? TextureReference?.Value.Height ?? 1;

    internal UiFontAsset(
        string family,
        float sourceSize,
        float lineHeight,
        AssetRef<TextureAsset> texture,
        IReadOnlyDictionary<int, UiFontGlyph> glyphs,
        IReadOnlyDictionary<long, float> kernings)
    {
        Family = family;
        SourceSize = Math.Max(1.0f, sourceSize);
        LineHeight = Math.Max(1.0f, lineHeight);
        TextureReference = texture;
        Glyphs = glyphs;
        Kernings = kernings;
    }

    internal UiFontAsset(
        string family,
        float sourceSize,
        float lineHeight,
        TextureAsset texture,
        IReadOnlyDictionary<int, UiFontGlyph> glyphs,
        IReadOnlyDictionary<long, float> kernings)
    {
        Family = family;
        SourceSize = Math.Max(1.0f, sourceSize);
        LineHeight = Math.Max(1.0f, lineHeight);
        EmbeddedTexture = texture;
        Glyphs = glyphs;
        Kernings = kernings;
    }

    internal float GetKerning(int first, int second) =>
        Kernings.GetValueOrDefault(((long)first << 32) | (uint)second);

    public void Dispose() => TextureReference?.Dispose();
}

internal readonly record struct UiFontGlyph(
    int Codepoint,
    float X,
    float Y,
    float Width,
    float Height,
    float XOffset,
    float YOffset,
    float XAdvance);

public sealed class UiFontAssetImporter : IAssetImporter<UiFontAsset>
{
    private const float TrueTypeSourceSize = 64.0f;
    private const int TrueTypeAtlasSize = 2048;
    private static readonly (int First, int Count)[] TrueTypeRanges =
    [
        (0x20, 0x7f - 0x20),
        (0xa0, 0xaf - 0xa0),
        (0xb0, 0x100 - 0xb0),
        (0x401, 0x40d - 0x401),
        (0x40e, 0x450 - 0x40e),
        (0x451, 0x45d - 0x451),
        (0x45e, 0x460 - 0x45e),
        (0x490, 0x492 - 0x490),
        (0x2013, 2),
        (0x2018, 3),
        (0x201c, 3),
        (0x2020, 3),
        (0x2026, 1),
        (0x2030, 1),
        (0x2039, 2),
        (0x20ac, 1),
        (0x2116, 1),
        (0x2122, 1)
    ];

    public IReadOnlyCollection<string> Extensions { get; } = [".fnt", ".ttf"];

    public UiFontAsset Import(AssetMetadata metadata, AssetImportContext context)
    {
        if (metadata.Path.EndsWith(".ttf", StringComparison.OrdinalIgnoreCase))
            return ImportTrueType(metadata, context);

        var document = XDocument.Parse(context.ReadAllText(metadata.Path));
        var root = document.Root ?? throw Invalid(metadata.Path, "has no root element");
        var info = root.Element("info") ?? throw Invalid(metadata.Path, "has no info element");
        var common = root.Element("common") ?? throw Invalid(metadata.Path, "has no common element");
        var page = root.Element("pages")?.Elements("page").SingleOrDefault() ??
                   throw Invalid(metadata.Path, "must contain exactly one texture page");
        var pageFile = Required(page, "file", metadata.Path);
        var texturePath = ResolveRelative(metadata.Path, pageFile);
        var texture = context.Load<TextureAsset>(texturePath);

        try
        {
            var glyphs = new Dictionary<int, UiFontGlyph>();
            foreach (var character in root.Element("chars")?.Elements("char") ?? [])
            {
                var glyph = new UiFontGlyph(
                    Integer(character, "id", metadata.Path),
                    Number(character, "x", metadata.Path),
                    Number(character, "y", metadata.Path),
                    Number(character, "width", metadata.Path),
                    Number(character, "height", metadata.Path),
                    Number(character, "xoffset", metadata.Path),
                    Number(character, "yoffset", metadata.Path),
                    Number(character, "xadvance", metadata.Path));
                glyphs[glyph.Codepoint] = glyph;
            }

            var kernings = new Dictionary<long, float>();
            foreach (var kerning in root.Element("kernings")?.Elements("kerning") ?? [])
            {
                var first = Integer(kerning, "first", metadata.Path);
                var second = Integer(kerning, "second", metadata.Path);
                kernings[((long)first << 32) | (uint)second] =
                    Number(kerning, "amount", metadata.Path);
            }

            return new UiFontAsset(
                Required(info, "face", metadata.Path),
                Math.Abs(Number(info, "size", metadata.Path)),
                Number(common, "lineHeight", metadata.Path),
                texture,
                glyphs,
                kernings);
        }
        catch
        {
            texture.Dispose();
            throw;
        }
    }

    private static unsafe UiFontAsset ImportTrueType(
        AssetMetadata metadata,
        AssetImportContext context)
    {
        var fontData = context.ReadAllBytes(metadata.Path);
        var alpha = new byte[TrueTypeAtlasSize * TrueTypeAtlasSize];
        var packedRanges = new List<(int First, stbtt_packedchar[] Characters)>();
        var packContext = new stbtt_pack_context();

        fixed (byte* atlasPointer = alpha)
        fixed (byte* fontPointer = fontData)
        {
            if (StbTrueType.stbtt_PackBegin(
                    packContext,
                    atlasPointer,
                    TrueTypeAtlasSize,
                    TrueTypeAtlasSize,
                    TrueTypeAtlasSize,
                    1,
                    null) == 0)
            {
                throw Invalid(metadata.Path, "could not create a TrueType glyph atlas");
            }

            try
            {
                StbTrueType.stbtt_PackSetSkipMissingCodepoints(packContext, 1);
                foreach (var (first, count) in TrueTypeRanges)
                {
                    var packedCharacters = new stbtt_packedchar[count];
                    fixed (stbtt_packedchar* characterPointer = packedCharacters)
                    {
                        if (StbTrueType.stbtt_PackFontRange(
                                packContext,
                                fontPointer,
                                0,
                                TrueTypeSourceSize,
                                first,
                                count,
                                characterPointer) == 0)
                        {
                            throw Invalid(metadata.Path, "does not fit into the TrueType glyph atlas");
                        }
                    }

                    packedRanges.Add((first, packedCharacters));
                }
            }
            finally
            {
                StbTrueType.stbtt_PackEnd(packContext);
            }
        }

        var fontInfo = StbTrueType.CreateFont(fontData, 0);
        var fontScale = StbTrueType.stbtt_ScaleForPixelHeight(fontInfo, TrueTypeSourceSize);
        int ascent;
        int descent;
        int lineGap;
        StbTrueType.stbtt_GetFontVMetrics(fontInfo, &ascent, &descent, &lineGap);
        var baseline = ascent * fontScale;
        var lineHeight = (ascent - descent + lineGap) * fontScale;

        var glyphs = new Dictionary<int, UiFontGlyph>();
        foreach (var (first, characters) in packedRanges)
        {
            for (var index = 0; index < characters.Length; index++)
            {
                var packed = characters[index];
                var codepoint = first + index;
                if (packed.x0 == packed.x1 && packed.y0 == packed.y1 && packed.xadvance == 0)
                    continue;

                glyphs[codepoint] = new UiFontGlyph(
                    codepoint,
                    packed.x0,
                    packed.y0,
                    packed.x1 - packed.x0,
                    packed.y1 - packed.y0,
                    packed.xoff,
                    baseline + packed.yoff,
                    packed.xadvance);
            }
        }

        var kernings = new Dictionary<long, float>();
        foreach (var first in glyphs.Keys)
        foreach (var second in glyphs.Keys)
        {
            var amount = StbTrueType.stbtt_GetCodepointKernAdvance(fontInfo, first, second) * fontScale;
            if (amount != 0)
                kernings[((long)first << 32) | (uint)second] = amount;
        }

        var rgba = new byte[alpha.Length * 4];
        for (var source = 0; source < alpha.Length; source++)
        {
            var destination = source * 4;
            rgba[destination] = 255;
            rgba[destination + 1] = 255;
            rgba[destination + 2] = 255;
            rgba[destination + 3] = alpha[source];
        }

        return new UiFontAsset(
            Path.GetFileNameWithoutExtension(metadata.Path),
            TrueTypeSourceSize,
            lineHeight,
            TextureAsset.FromRgba(TrueTypeAtlasSize, TrueTypeAtlasSize, rgba),
            glyphs,
            kernings);
    }

    private static string Required(XElement element, string name, string path) =>
        element.Attribute(name)?.Value ?? throw Invalid(path, $"is missing '{name}'");

    private static int Integer(XElement element, string name, string path) =>
        int.TryParse(Required(element, name, path), NumberStyles.Integer, CultureInfo.InvariantCulture, out var value)
            ? value
            : throw Invalid(path, $"contains invalid '{name}'");

    private static float Number(XElement element, string name, string path) =>
        float.TryParse(Required(element, name, path), NumberStyles.Float, CultureInfo.InvariantCulture, out var value)
            ? value
            : throw Invalid(path, $"contains invalid '{name}'");

    private static InvalidDataException Invalid(string path, string message) =>
        new($"Bitmap font '{path}' {message}.");

    private static string ResolveRelative(string owner, string relative)
    {
        var directory = Path.GetDirectoryName(owner) ?? string.Empty;
        return Path.Combine(directory, relative).Replace('\\', '/');
    }
}
