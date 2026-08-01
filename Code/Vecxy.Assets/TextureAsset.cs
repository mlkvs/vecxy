using StbImageSharp;

namespace Vecxy.Assets;

public sealed class TextureAsset
{
    public int Width { get; }
    public int Height { get; }
    public byte[] Pixels { get; }

    internal TextureAsset(int width, int height, byte[] pixels)
    {
        if (width <= 0)
            throw new ArgumentOutOfRangeException(nameof(width));
        if (height <= 0)
            throw new ArgumentOutOfRangeException(nameof(height));
        ArgumentNullException.ThrowIfNull(pixels);
        if (pixels.Length != checked(width * height * 4))
        {
            throw new ArgumentException(
                "Texture pixels must contain exactly four channels per pixel.",
                nameof(pixels));
        }

        Width = width;
        Height = height;
        Pixels = pixels;
    }

    /// <summary>
    /// Reads an RGBA pixel. Coordinates use image-space orientation:
    /// (0, 0) is the top-left source pixel.
    /// </summary>
    public Color32 GetPixel(int x, int y)
    {
        if ((uint)x >= (uint)Width)
            throw new ArgumentOutOfRangeException(nameof(x));
        if ((uint)y >= (uint)Height)
            throw new ArgumentOutOfRangeException(nameof(y));

        var index = checked((y * Width + x) * 4);
        return new Color32(
            Pixels[index],
            Pixels[index + 1],
            Pixels[index + 2],
            Pixels[index + 3]);
    }
}

public sealed class TextureAssetImporter : IAssetImporter<TextureAsset>
{
    public IReadOnlyCollection<string> Extensions { get; } =
        [".png", ".jpg", ".jpeg", ".bmp", ".tga", ".ppm"];

    public TextureAsset Import(
        AssetMetadata metadata,
        AssetImportContext context)
    {
        if (!metadata.Path.EndsWith(".ppm", StringComparison.OrdinalIgnoreCase))
        {
            using var stream = new MemoryStream(
                context.ReadAllBytes(metadata.Path),
                writable: false);
            var image = ImageResult.FromStream(
                stream,
                ColorComponents.RedGreenBlueAlpha);
            return new TextureAsset(image.Width, image.Height, image.Data);
        }

        var source = context.ReadAllText(metadata.Path);
        var tokens = Tokenize(source).ToArray();

        if (tokens.Length < 4 || tokens[0] != "P3")
        {
            throw new InvalidDataException(
                $"Texture '{metadata.Path}' is not an ASCII PPM (P3) image.");
        }

        var width = ParsePositive(tokens[1], "width", metadata.Path);
        var height = ParsePositive(tokens[2], "height", metadata.Path);
        var maximum = ParsePositive(tokens[3], "maximum channel value", metadata.Path);

        if (maximum > 255)
        {
            throw new InvalidDataException(
                $"Texture '{metadata.Path}' uses unsupported channel maximum {maximum}.");
        }

        var expectedChannels = checked(width * height * 3);
        if (tokens.Length != expectedChannels + 4)
        {
            throw new InvalidDataException(
                $"Texture '{metadata.Path}' contains {tokens.Length - 4} channels; expected {expectedChannels}.");
        }

        var pixels = new byte[checked(width * height * 4)];
        for (var sourceIndex = 0; sourceIndex < expectedChannels; sourceIndex += 3)
        {
            var destinationIndex = sourceIndex / 3 * 4;
            pixels[destinationIndex] = ScaleChannel(tokens[sourceIndex + 4], maximum, metadata.Path);
            pixels[destinationIndex + 1] = ScaleChannel(tokens[sourceIndex + 5], maximum, metadata.Path);
            pixels[destinationIndex + 2] = ScaleChannel(tokens[sourceIndex + 6], maximum, metadata.Path);
            pixels[destinationIndex + 3] = 255;
        }

        return new TextureAsset(width, height, pixels);
    }

    private static IEnumerable<string> Tokenize(string source)
    {
        using var reader = new StringReader(source);
        while (reader.ReadLine() is { } line)
        {
            var commentStart = line.IndexOf('#');
            if (commentStart >= 0)
            {
                line = line[..commentStart];
            }

            foreach (var token in line.Split(
                         (char[]?)null,
                         StringSplitOptions.RemoveEmptyEntries))
            {
                yield return token;
            }
        }
    }

    private static int ParsePositive(string value, string field, string path)
    {
        if (!int.TryParse(value, out var result) || result <= 0)
        {
            throw new InvalidDataException(
                $"Texture '{path}' has invalid {field}: '{value}'.");
        }

        return result;
    }

    private static byte ScaleChannel(string value, int maximum, string path)
    {
        if (!int.TryParse(value, out var channel) || channel < 0 || channel > maximum)
        {
            throw new InvalidDataException(
                $"Texture '{path}' has invalid channel value: '{value}'.");
        }

        return (byte)(channel * 255 / maximum);
    }
}
