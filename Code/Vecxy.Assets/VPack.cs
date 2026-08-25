using System.Collections.Frozen;
using System.Text;
using K4os.Compression.LZ4;
using ZstdSharp;

namespace Vecxy.Assets;

public static class VPackFormat
{
    public const uint Magic = 0x4B505856; // VXPK, little-endian
    public const ushort Version = 1;
    public const ushort HeaderSize = 96;
}

public interface IVPackCompressionCodec
{
    VPackCompressionAlgorithm Algorithm { get; }
    byte[] Compress(ReadOnlySpan<byte> source, int level);
    byte[] Decompress(ReadOnlySpan<byte> source, int uncompressedSize);
}

public static class VPackCompressionCodecs
{
    private static readonly FrozenDictionary<VPackCompressionAlgorithm, IVPackCompressionCodec> Codecs =
        new IVPackCompressionCodec[] { new NoneVPackCodec(), new Lz4VPackCodec(), new ZstdVPackCodec() }
            .ToFrozenDictionary(x => x.Algorithm);

    public static IVPackCompressionCodec Get(VPackCompressionAlgorithm algorithm) =>
        Codecs.TryGetValue(algorithm, out var codec)
            ? codec
            : throw new InvalidDataException($"Unsupported VPack compression codec: {algorithm}.");
}

public sealed class NoneVPackCodec : IVPackCompressionCodec
{
    public VPackCompressionAlgorithm Algorithm => VPackCompressionAlgorithm.None;
    public byte[] Compress(ReadOnlySpan<byte> source, int level) => source.ToArray();
    public byte[] Decompress(ReadOnlySpan<byte> source, int uncompressedSize) =>
        source.Length == uncompressedSize ? source.ToArray() : throw new InvalidDataException("Invalid raw VPack block size.");
}

public sealed class Lz4VPackCodec : IVPackCompressionCodec
{
    public VPackCompressionAlgorithm Algorithm => VPackCompressionAlgorithm.Lz4;
    public byte[] Compress(ReadOnlySpan<byte> source, int level)
    {
        var output = new byte[LZ4Codec.MaximumOutputSize(source.Length)];
        var length = LZ4Codec.Encode(source, output, level > 0 ? LZ4Level.L09_HC : LZ4Level.L00_FAST);
        return output.AsSpan(0, length).ToArray();
    }
    public byte[] Decompress(ReadOnlySpan<byte> source, int uncompressedSize)
    {
        var output = new byte[uncompressedSize];
        if (LZ4Codec.Decode(source, output) != uncompressedSize) throw new InvalidDataException("Corrupt LZ4 VPack block.");
        return output;
    }
}

public sealed class ZstdVPackCodec : IVPackCompressionCodec
{
    public VPackCompressionAlgorithm Algorithm => VPackCompressionAlgorithm.Zstd;
    public byte[] Compress(ReadOnlySpan<byte> source, int level)
    {
        using var compressor = new Compressor(level);
        return compressor.Wrap(source.ToArray()).ToArray();
    }
    public byte[] Decompress(ReadOnlySpan<byte> source, int uncompressedSize)
    {
        using var decompressor = new Decompressor();
        var result = decompressor.Unwrap(source.ToArray(), uncompressedSize).ToArray();
        return result.Length == uncompressedSize ? result : throw new InvalidDataException("Corrupt Zstd VPack block.");
    }
}

public sealed record VPackAssetSource(AssetId Id, string AssetType, ReadOnlyMemory<byte> Data, bool PreferRaw = false);
public sealed record VPackBuildResult(long RawSize, long PackedSize, int AssetCount, int BlockCount);
public sealed record VPackAssetIndexEntry(AssetId Id, string AssetType, int BlockIndex, int Offset, int StoredSize, int UncompressedSize, uint Flags);
public sealed record VPackBlockInfo(long Offset, int StoredSize, int UncompressedSize, VPackCompressionAlgorithm Algorithm);

public static class VPackWriter
{
    public static async Task<VPackBuildResult> WriteAsync(
        Stream output, PackageId package, VPackPlatform platform, IReadOnlyCollection<PackageId> dependencies,
        IReadOnlyCollection<VPackAssetSource> assets, VPackCompressionSettings compression,
        CancellationToken cancellationToken = default)
    {
        if (!output.CanSeek || !output.CanWrite) throw new ArgumentException("VPack output must be writable and seekable.", nameof(output));
        if (compression.BlockSize <= 0) throw new ArgumentOutOfRangeException(nameof(compression), "Block size must be positive.");
        var blocks = BuildBlocks(assets, compression.BlockSize);
        var encoded = new List<(byte[] Data, int RawSize, VPackCompressionAlgorithm Codec)>(blocks.Count);
        foreach (var block in blocks)
        {
            var raw = block.Data.ToArray();
            var codec = block.PreferRaw ? VPackCompressionAlgorithm.None : compression.Algorithm;
            var packed = VPackCompressionCodecs.Get(codec).Compress(raw, compression.Level);
            if (codec != VPackCompressionAlgorithm.None && packed.Length >= raw.Length * 0.98)
            {
                codec = VPackCompressionAlgorithm.None;
                packed = raw;
            }
            encoded.Add((packed, raw.Length, codec));
        }

        output.SetLength(0);
        output.Position = VPackFormat.HeaderSize;
        var indexOffset = output.Position;
        using (var writer = new BinaryWriter(output, Encoding.UTF8, leaveOpen: true))
        {
            writer.Write(assets.Count);
            foreach (var item in blocks.SelectMany((block, blockIndex) => block.Assets.Select(asset => (asset, blockIndex))))
            {
                writer.Write(item.asset.Source.Id.Value.ToByteArray());
                WriteString(writer, item.asset.Source.AssetType);
                writer.Write(item.blockIndex);
                writer.Write(item.asset.Offset);
                writer.Write(encoded[item.blockIndex].Data.Length);
                writer.Write(item.asset.Source.Data.Length);
                writer.Write(item.asset.Source.PreferRaw ? 1u : 0u);
            }
        }
        var indexSize = output.Position - indexOffset;
        var dependencyOffset = output.Position;
        using (var writer = new BinaryWriter(output, Encoding.UTF8, leaveOpen: true))
        {
            writer.Write(dependencies.Count);
            foreach (var dependency in dependencies) writer.Write(dependency.Value.ToByteArray());
        }
        var dependencySize = output.Position - dependencyOffset;
        var blockTableOffset = output.Position;
        var blockTableSize = 4L + encoded.Count * 24L;
        output.Position += blockTableSize;
        var dataOffset = output.Position;
        var infos = new List<VPackBlockInfo>(encoded.Count);
        foreach (var block in encoded)
        {
            var offset = output.Position;
            await output.WriteAsync(block.Data, cancellationToken);
            infos.Add(new VPackBlockInfo(offset, block.Data.Length, block.RawSize, block.Codec));
        }
        var end = output.Position;
        output.Position = blockTableOffset;
        using (var writer = new BinaryWriter(output, Encoding.UTF8, leaveOpen: true))
        {
            writer.Write(infos.Count);
            foreach (var info in infos)
            {
                writer.Write(info.Offset); writer.Write(info.StoredSize); writer.Write(info.UncompressedSize);
                writer.Write((byte)info.Algorithm); writer.Write(new byte[7]);
            }
        }
        output.Position = 0;
        using (var writer = new BinaryWriter(output, Encoding.UTF8, leaveOpen: true))
        {
            writer.Write(VPackFormat.Magic); writer.Write(VPackFormat.Version); writer.Write(VPackFormat.HeaderSize);
            writer.Write(package.Value.ToByteArray()); writer.Write((byte)platform); writer.Write(new byte[7]);
            writer.Write(indexOffset); writer.Write(indexSize); writer.Write(dependencyOffset); writer.Write(dependencySize);
            writer.Write(blockTableOffset); writer.Write(blockTableSize); writer.Write(dataOffset); writer.Write(end - dataOffset);
        }
        output.Position = end;
        await output.FlushAsync(cancellationToken);
        return new VPackBuildResult(blocks.Sum(x => (long)x.Data.Count), end, assets.Count, blocks.Count);
    }

    private static List<BuildBlock> BuildBlocks(IReadOnlyCollection<VPackAssetSource> assets, int blockSize)
    {
        var result = new List<BuildBlock>();
        BuildBlock? current = null;
        foreach (var source in assets.OrderBy(x => x.Id.Value))
        {
            if (current is null || current.PreferRaw != source.PreferRaw || current.Data.Count + source.Data.Length > blockSize)
            {
                current = new BuildBlock(source.PreferRaw); result.Add(current);
            }
            var offset = current.Data.Count;
            current.Data.AddRange(source.Data.ToArray());
            current.Assets.Add(new BuildAsset(source, offset));
        }
        return result;
    }
    private static void WriteString(BinaryWriter writer, string value) { var bytes = Encoding.UTF8.GetBytes(value); writer.Write((ushort)bytes.Length); writer.Write(bytes); }
    private sealed class BuildBlock(bool preferRaw) { public bool PreferRaw { get; } = preferRaw; public List<byte> Data { get; } = []; public List<BuildAsset> Assets { get; } = []; }
    private sealed record BuildAsset(VPackAssetSource Source, int Offset);
}

public sealed class VPackReader : IAsyncDisposable
{
    private readonly Stream _stream;
    private readonly SemaphoreSlim _io = new(1, 1);
    private readonly FrozenDictionary<AssetId, VPackAssetIndexEntry> _assets;
    private readonly VPackBlockInfo[] _blocks;
    public PackageId Package { get; }
    public VPackPlatform Platform { get; }
    public IReadOnlyList<PackageId> Dependencies { get; }
    public IReadOnlyCollection<VPackAssetIndexEntry> Assets => _assets.Values;

    private VPackReader(Stream stream, PackageId package, VPackPlatform platform, IEnumerable<PackageId> dependencies,
        IEnumerable<VPackAssetIndexEntry> assets, VPackBlockInfo[] blocks)
    {
        _stream = stream; Package = package; Platform = platform; Dependencies = dependencies.ToArray(); _blocks = blocks;
        var entries = assets.ToArray();
        foreach (var asset in entries)
            if (asset.BlockIndex < 0 || asset.BlockIndex >= blocks.Length || asset.Offset < 0 ||
                asset.UncompressedSize < 0 || asset.Offset > blocks[asset.BlockIndex].UncompressedSize - asset.UncompressedSize)
                throw new InvalidDataException($"Invalid VPack index entry for asset {asset.Id}.");
        _assets = entries.ToFrozenDictionary(x => x.Id);
    }

    public static async Task<VPackReader> OpenAsync(Stream stream, CancellationToken cancellationToken = default)
    {
        if (!stream.CanRead || !stream.CanSeek) throw new ArgumentException("VPack stream must be readable and seekable.", nameof(stream));
        try
        {
        var header = new byte[VPackFormat.HeaderSize]; await ReadAtAsync(stream, 0, header, cancellationToken);
        using var headerReader = new BinaryReader(new MemoryStream(header), Encoding.UTF8);
        if (headerReader.ReadUInt32() != VPackFormat.Magic) throw new InvalidDataException("Invalid VPack magic.");
        var version = headerReader.ReadUInt16(); if (version != VPackFormat.Version) throw new NotSupportedException($"Unsupported VPack version {version}.");
        if (headerReader.ReadUInt16() != VPackFormat.HeaderSize) throw new InvalidDataException("Invalid VPack header size.");
        var package = new PackageId(new Guid(headerReader.ReadBytes(16))); var platform = (VPackPlatform)headerReader.ReadByte(); headerReader.ReadBytes(7);
        var indexOffset = headerReader.ReadInt64(); var indexSize = headerReader.ReadInt64();
        var dependencyOffset = headerReader.ReadInt64(); var dependencySize = headerReader.ReadInt64();
        var blockOffset = headerReader.ReadInt64(); var blockSize = headerReader.ReadInt64();
        headerReader.ReadInt64(); headerReader.ReadInt64();
        ValidateRegion(stream, indexOffset, indexSize); ValidateRegion(stream, dependencyOffset, dependencySize); ValidateRegion(stream, blockOffset, blockSize);
        var index = await ReadRegionAsync(stream, indexOffset, indexSize, cancellationToken);
        var dependencies = await ReadRegionAsync(stream, dependencyOffset, dependencySize, cancellationToken);
        var blockTable = await ReadRegionAsync(stream, blockOffset, blockSize, cancellationToken);
        return new VPackReader(stream, package, platform, ReadDependencies(dependencies), ReadIndex(index), ReadBlocks(blockTable, stream.Length));
        }
        catch { await stream.DisposeAsync(); throw; }
    }

    public async ValueTask<ReadOnlyMemory<byte>> ReadAssetAsync(AssetId id, CancellationToken cancellationToken = default)
    {
        if (!_assets.TryGetValue(id, out var asset)) throw new KeyNotFoundException($"Asset {id} is not in package {Package}.");
        var block = _blocks[asset.BlockIndex]; var packed = new byte[block.StoredSize];
        await _io.WaitAsync(cancellationToken);
        try { await ReadAtAsync(_stream, block.Offset, packed, cancellationToken); }
        finally { _io.Release(); }
        var raw = VPackCompressionCodecs.Get(block.Algorithm).Decompress(packed, block.UncompressedSize);
        if (asset.Offset < 0 || asset.UncompressedSize < 0 || asset.Offset + asset.UncompressedSize > raw.Length) throw new InvalidDataException("Invalid VPack asset index range.");
        return raw.AsMemory(asset.Offset, asset.UncompressedSize);
    }

    public async ValueTask DisposeAsync() { _io.Dispose(); await _stream.DisposeAsync(); }
    private static IEnumerable<VPackAssetIndexEntry> ReadIndex(byte[] bytes) { using var r = new BinaryReader(new MemoryStream(bytes), Encoding.UTF8); var count = CheckedCount(r.ReadInt32()); if(count>(bytes.Length-4)/38)throw new InvalidDataException("Invalid VPack asset count."); for (var i=0;i<count;i++) { var id=new AssetId(new Guid(r.ReadBytes(16))); var type=ReadString(r); yield return new(id,type,r.ReadInt32(),r.ReadInt32(),r.ReadInt32(),r.ReadInt32(),r.ReadUInt32()); } if(r.BaseStream.Position!=r.BaseStream.Length) throw new InvalidDataException("Trailing VPack index data."); }
    private static IEnumerable<PackageId> ReadDependencies(byte[] bytes) { using var r=new BinaryReader(new MemoryStream(bytes)); var count=CheckedCount(r.ReadInt32()); if(count!=(bytes.Length-4)/16)throw new InvalidDataException("Invalid VPack dependency table."); for(var i=0;i<count;i++) yield return new PackageId(new Guid(r.ReadBytes(16))); }
    private static VPackBlockInfo[] ReadBlocks(byte[] bytes, long length) { using var r=new BinaryReader(new MemoryStream(bytes)); var count=CheckedCount(r.ReadInt32()); if(count!=(bytes.Length-4)/24)throw new InvalidDataException("Invalid VPack block table."); var result=new VPackBlockInfo[count]; for(var i=0;i<count;i++){var info=new VPackBlockInfo(r.ReadInt64(),r.ReadInt32(),r.ReadInt32(),(VPackCompressionAlgorithm)r.ReadByte());r.ReadBytes(7);ValidateRegion(length,info.Offset,info.StoredSize);VPackCompressionCodecs.Get(info.Algorithm);result[i]=info;} return result; }
    private static string ReadString(BinaryReader r) => Encoding.UTF8.GetString(r.ReadBytes(r.ReadUInt16()));
    private static int CheckedCount(int value) => value is >=0 and <=10_000_000 ? value : throw new InvalidDataException("Invalid VPack item count.");
    private static void ValidateRegion(Stream stream,long offset,long size)=>ValidateRegion(stream.Length,offset,size);
    private static void ValidateRegion(long length,long offset,long size) { if(offset<0||size<0||offset>length-size) throw new InvalidDataException("VPack region is outside the file."); }
    private static async Task<byte[]> ReadRegionAsync(Stream s,long o,long n,CancellationToken ct){if(n>int.MaxValue)throw new InvalidDataException("VPack metadata is too large.");var b=new byte[(int)n];await ReadAtAsync(s,o,b,ct);return b;}
    private static async Task ReadAtAsync(Stream s,long o,Memory<byte> b,CancellationToken ct){s.Position=o;var read=0;while(read<b.Length){var n=await s.ReadAsync(b[read..],ct);if(n==0)throw new EndOfStreamException("Unexpected end of VPack.");read+=n;}}
}
