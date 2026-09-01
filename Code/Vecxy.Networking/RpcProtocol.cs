using System.Buffers;
using System.Buffers.Binary;

namespace Vecxy.Networking;

internal static class RpcProtocol
{
    public const int HeaderSize = 18;

    public static PooledPacket Write(
        NetworkObjectId objectId,
        byte behaviourId,
        uint methodId,
        ReadOnlySpan<byte> payload)
    {
        var packet = PooledPacket.Rent(checked(HeaderSize + payload.Length));
        var span = packet.Span;
        span[0] = (byte)NetworkMessageType.Rpc;
        BinaryPrimitives.WriteUInt64LittleEndian(span[1..], objectId.Value);
        span[9] = behaviourId;
        BinaryPrimitives.WriteUInt32LittleEndian(span[10..], methodId);
        BinaryPrimitives.WriteInt32LittleEndian(span[14..], payload.Length);
        payload.CopyTo(span[HeaderSize..]);
        return packet;
    }

    public static bool TryRead(ReadOnlySpan<byte> packet, int maxPayloadSize, out RpcEnvelope envelope)
    {
        envelope = default;
        if (packet.Length < HeaderSize || packet[0] != (byte)NetworkMessageType.Rpc) return false;
        var payloadLength = BinaryPrimitives.ReadInt32LittleEndian(packet[14..]);
        if (payloadLength < 0 || payloadLength > maxPayloadSize || packet.Length != HeaderSize + payloadLength) return false;
        envelope = new RpcEnvelope(
            new NetworkObjectId(BinaryPrimitives.ReadUInt64LittleEndian(packet[1..])),
            packet[9],
            BinaryPrimitives.ReadUInt32LittleEndian(packet[10..]),
            packet.Slice(HeaderSize, payloadLength).ToArray());
        return true;
    }
}

internal readonly record struct RpcEnvelope(
    NetworkObjectId ObjectId,
    byte BehaviourId,
    uint MethodId,
    ReadOnlyMemory<byte> Payload);

internal sealed class PooledPacket : IDisposable
{
    private byte[]? _buffer;
    private PooledPacket(byte[] buffer, int length) { _buffer = buffer; Length = length; }
    public int Length { get; }
    public Span<byte> Span => (_buffer ?? throw new ObjectDisposedException(nameof(PooledPacket))).AsSpan(0, Length);
    public ReadOnlySpan<byte> ReadOnlySpan => Span;
    public static PooledPacket Rent(int length) => new(ArrayPool<byte>.Shared.Rent(length), length);
    public static PooledPacket Copy(ReadOnlySpan<byte> source)
    { var packet = Rent(source.Length); source.CopyTo(packet.Span); return packet; }
    public void Dispose()
    { var buffer = Interlocked.Exchange(ref _buffer, null); if (buffer is not null) ArrayPool<byte>.Shared.Return(buffer); }
}
