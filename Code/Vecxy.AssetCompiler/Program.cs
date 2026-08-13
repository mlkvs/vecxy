using System.IO.Compression;
using System.Text;
using System.Text.Json;
using StbImageSharp;

if (args.Length != 2)
{
    Console.Error.WriteLine("Usage: Vecxy.AssetCompiler <assets-directory> <output-directory>");
    return 2;
}

var assetsDirectory = Path.GetFullPath(args[0]);
var outputDirectory = Path.GetFullPath(args[1]);
var jsonOptions = new JsonSerializerOptions
{
    PropertyNameCaseInsensitive = true,
    ReadCommentHandling = JsonCommentHandling.Skip,
    AllowTrailingCommas = true
};
Directory.CreateDirectory(outputDirectory);
foreach (var generatedFile in Directory.EnumerateFiles(outputDirectory, "*", SearchOption.AllDirectories))
    File.Delete(generatedFile);
var excludedSources = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

foreach (var atlasPath in Directory.EnumerateFiles(assetsDirectory, "*.atlas", SearchOption.AllDirectories))
{
    var descriptor = JsonSerializer.Deserialize<AtlasDescriptor>(
        File.ReadAllText(atlasPath),
        jsonOptions) ?? throw new InvalidDataException($"Sprite atlas is empty: {atlasPath}");
    if (descriptor.Sources.Count == 0)
        continue;

    var relativeAtlasPath = Path.GetRelativePath(assetsDirectory, atlasPath);
    var relativeDirectory = Path.GetDirectoryName(relativeAtlasPath) ?? string.Empty;
    var sourceDirectory = Path.GetDirectoryName(atlasPath) ?? assetsDirectory;
    var images = descriptor.Sources.Select(pair =>
    {
        if (string.IsNullOrWhiteSpace(pair.Key) || string.IsNullOrWhiteSpace(pair.Value))
            throw new InvalidDataException($"Sprite atlas contains an empty source: {atlasPath}");
        var path = Path.GetFullPath(Path.Combine(sourceDirectory, pair.Value));
        if (!path.StartsWith(assetsDirectory + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException($"Atlas source is outside Assets: {path}");
        using var stream = File.OpenRead(path);
        var image = ImageResult.FromStream(stream, ColorComponents.RedGreenBlueAlpha);
        excludedSources.Add(Path.GetRelativePath(assetsDirectory, path));
        return new SourceImage(pair.Key, image.Width, image.Height, image.Data);
    }).OrderByDescending(image => image.Height).ThenByDescending(image => image.Width).ToArray();

    var padding = Math.Clamp(descriptor.Padding, 1, 16);
    var width = Math.Clamp(descriptor.Width, 64, 4096);
    var x = padding;
    var y = padding;
    var rowHeight = 0;
    var placements = new List<Placement>();
    foreach (var image in images)
    {
        if (image.Width + padding * 2 > width)
            throw new InvalidDataException($"Sprite '{image.Name}' is wider than atlas {atlasPath}.");
        if (x + image.Width + padding > width)
        {
            x = padding;
            y += rowHeight + padding * 2;
            rowHeight = 0;
        }

        placements.Add(new Placement(image, x, y));
        x += image.Width + padding * 2;
        rowHeight = Math.Max(rowHeight, image.Height);
    }

    var requiredHeight = y + rowHeight + padding;
    var height = 64;
    while (height < requiredHeight && height < 4096)
        height *= 2;
    if (height < requiredHeight)
        throw new InvalidDataException($"Sprite atlas is taller than 4096 pixels: {atlasPath}");

    var pixels = new byte[checked(width * height * 4)];
    var sprites = new Dictionary<string, SpriteDescriptor>(StringComparer.Ordinal);
    foreach (var placement in placements)
    {
        CopyWithExtrusion(placement.Image, pixels, width, placement.X, placement.Y, padding);
        sprites.Add(placement.Image.Name,
            new SpriteDescriptor(placement.X, placement.Y, placement.Image.Width, placement.Image.Height));
    }

    var atlasFileName = Path.GetFileNameWithoutExtension(relativeAtlasPath) + ".png";
    var outputAtlasPath = Path.Combine(outputDirectory, relativeAtlasPath);
    var outputTexturePath = Path.Combine(outputDirectory, relativeDirectory, atlasFileName);
    Directory.CreateDirectory(Path.GetDirectoryName(outputAtlasPath)!);
    PngWriter.Write(outputTexturePath, width, height, pixels);
    File.WriteAllText(outputAtlasPath, JsonSerializer.Serialize(
        new CompiledAtlasDescriptor(atlasFileName, sprites),
        new JsonSerializerOptions { WriteIndented = true }));
    Console.WriteLine($"Compiled {relativeAtlasPath}: {images.Length} sprites, {width}x{height}");
}

File.WriteAllLines(
    Path.Combine(outputDirectory, "excluded-sources.txt"),
    excludedSources.Order(StringComparer.OrdinalIgnoreCase));
return 0;

static void CopyWithExtrusion(SourceImage source, byte[] destination, int destinationWidth, int destinationX,
    int destinationY, int padding)
{
    for (var y = -padding; y < source.Height + padding; y++)
    for (var x = -padding; x < source.Width + padding; x++)
    {
        var sourceX = Math.Clamp(x, 0, source.Width - 1);
        var sourceY = Math.Clamp(y, 0, source.Height - 1);
        var sourceIndex = (sourceY * source.Width + sourceX) * 4;
        var targetIndex = ((destinationY + y) * destinationWidth + destinationX + x) * 4;
        Buffer.BlockCopy(source.Pixels, sourceIndex, destination, targetIndex, 4);
    }
}

sealed class AtlasDescriptor
{
    public Dictionary<string, string> Sources { get; init; } = [];
    public int Width { get; init; } = 1024;
    public int Padding { get; init; } = 2;
}

sealed record SourceImage(string Name, int Width, int Height, byte[] Pixels);

sealed record Placement(SourceImage Image, int X, int Y);

sealed record SpriteDescriptor(int X, int Y, int Width, int Height);

sealed record CompiledAtlasDescriptor(string Texture, Dictionary<string, SpriteDescriptor> Sprites);

static class PngWriter
{
    private static readonly byte[] Signature = [137, 80, 78, 71, 13, 10, 26, 10];

    public static void Write(string path, int width, int height, byte[] rgba)
    {
        using var output = File.Create(path);
        output.Write(Signature);
        Span<byte> header = stackalloc byte[13];
        WriteUInt32(header, 0, (uint)width);
        WriteUInt32(header, 4, (uint)height);
        header[8] = 8;
        header[9] = 6;
        WriteChunk(output, "IHDR", header);

        using var compressed = new MemoryStream();
        using (var zlib = new ZLibStream(compressed, CompressionLevel.SmallestSize, leaveOpen: true))
        {
            var stride = checked(width * 4);
            var row = new byte[stride + 1];
            var candidate = new byte[stride + 1];
            var previous = new byte[stride];
            for (var y = 0; y < height; y++)
            {
                var offset = y * stride;
                var bestScore = long.MaxValue;
                for (byte filter = 0; filter <= 4; filter++)
                {
                    candidate[0] = filter;
                    long score = 0;
                    for (var index = 0; index < stride; index++)
                    {
                        var value = rgba[offset + index];
                        var left = index >= 4 ? rgba[offset + index - 4] : (byte)0;
                        var up = previous[index];
                        var upLeft = index >= 4 ? previous[index - 4] : (byte)0;
                        var predictor = filter switch
                        {
                            0 => 0,
                            1 => left,
                            2 => up,
                            3 => (left + up) / 2,
                            _ => Paeth(left, up, upLeft)
                        };
                        var filtered = unchecked((byte)(value - predictor));
                        candidate[index + 1] = filtered;
                        score += Math.Abs((int)(sbyte)filtered);
                    }

                    if (score < bestScore)
                    {
                        bestScore = score;
                        (row, candidate) = (candidate, row);
                    }
                }

                zlib.Write(row);
                Buffer.BlockCopy(rgba, offset, previous, 0, stride);
            }
        }

        WriteChunk(output, "IDAT", compressed.ToArray());
        WriteChunk(output, "IEND", []);
    }

    private static void WriteChunk(Stream output, string type, ReadOnlySpan<byte> data)
    {
        Span<byte> size = stackalloc byte[4];
        WriteUInt32(size, 0, (uint)data.Length);
        output.Write(size);
        var typeBytes = Encoding.ASCII.GetBytes(type);
        output.Write(typeBytes);
        output.Write(data);
        var crcData = new byte[typeBytes.Length + data.Length];
        typeBytes.CopyTo(crcData, 0);
        data.CopyTo(crcData.AsSpan(typeBytes.Length));
        WriteUInt32(size, 0, Crc32(crcData));
        output.Write(size);
    }

    private static void WriteUInt32(Span<byte> target, int offset, uint value)
    {
        target[offset] = (byte)(value >> 24);
        target[offset + 1] = (byte)(value >> 16);
        target[offset + 2] = (byte)(value >> 8);
        target[offset + 3] = (byte)value;
    }

    private static uint Crc32(ReadOnlySpan<byte> data)
    {
        var crc = 0xffffffffu;
        foreach (var value in data)
        {
            crc ^= value;
            for (var bit = 0; bit < 8; bit++)
                crc = (crc >> 1) ^ (0xedb88320u & (uint)-(int)(crc & 1));
        }

        return ~crc;
    }

    private static int Paeth(int left, int up, int upLeft)
    {
        var prediction = left + up - upLeft;
        var leftDistance = Math.Abs(prediction - left);
        var upDistance = Math.Abs(prediction - up);
        var upLeftDistance = Math.Abs(prediction - upLeft);
        return leftDistance <= upDistance && leftDistance <= upLeftDistance
            ? left
            : upDistance <= upLeftDistance
                ? up
                : upLeft;
    }
}
