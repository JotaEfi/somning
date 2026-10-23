using System.Buffers;
using System.Buffers.Binary;
using System.Text;

namespace Somning.Platform.Windows.Recording;

public static class WavHeaderRepair
{
    private const int RiffHeaderLength = 12;
    private const int ChunkHeaderLength = 8;

    public static bool EnsureValid(string wavPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(wavPath);
        using var stream = new FileStream(wavPath, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
        if (stream.Length < RiffHeaderLength || stream.Length > uint.MaxValue + 8L)
            return false;

        Span<byte> riffHeader = stackalloc byte[RiffHeaderLength];
        stream.ReadExactly(riffHeader);
        if (!riffHeader[..4].SequenceEqual("RIFF"u8) || !riffHeader[8..12].SequenceEqual("WAVE"u8))
            return false;

        var declaredRiffSize = BinaryPrimitives.ReadUInt32LittleEndian(riffHeader[4..8]);
        var declaredRiffEnd = 8L + declaredRiffSize;
        var fullChunkSequence = IsChunkSequence(stream, RiffHeaderLength, stream.Length, out var hasData, out _);
        if (fullChunkSequence && hasData &&
            TryFindDataChunk(stream, stream.Length, out var completeDataOffset, out var completeDataSize, out var completeBlockAlign) &&
            completeDataOffset + completeDataSize <= stream.Length)
        {
            var alignedDataSize = completeDataSize - completeDataSize % (uint)completeBlockAlign;
            if (alignedDataSize != completeDataSize)
            {
                TruncateDataToFrameBoundary(stream, completeDataOffset, completeDataSize, alignedDataSize);
                return true;
            }

            var actualRiffSize = (uint)(stream.Length - 8);
            if (declaredRiffSize == actualRiffSize)
                return false;

            WriteUInt32At(stream, 4, actualRiffSize);
            stream.Flush(flushToDisk: true);
            return true;
        }

        if (!TryFindDataChunk(stream, stream.Length, out var dataOffset, out var declaredDataSize, out var blockAlign))
            return false;

        var declaredDataEnd = dataOffset + declaredDataSize + (declaredDataSize & 1);
        if (declaredDataEnd < stream.Length && IsChunkSequence(stream, declaredDataEnd, stream.Length, out _, out _))
            return false;
        if (declaredDataEnd < declaredRiffEnd && IsChunkSequence(stream, declaredDataEnd, declaredRiffEnd, out _, out _))
            return false;

        var rawDataLength = declaredDataEnd == stream.Length ? declaredDataSize : stream.Length - dataOffset;
        var dataLength = rawDataLength - rawDataLength % blockAlign;
        if (dataLength > uint.MaxValue || dataOffset + dataLength > uint.MaxValue + 8L)
            return false;

        var repairedLength = dataOffset + dataLength;
        var repairedRiffSize = (uint)(repairedLength - 8);
        var repairedDataSize = (uint)dataLength;
        if (declaredRiffSize == repairedRiffSize && declaredDataSize == repairedDataSize && stream.Length == repairedLength)
            return false;

        Span<byte> size = stackalloc byte[sizeof(uint)];
        BinaryPrimitives.WriteUInt32LittleEndian(size, repairedRiffSize);
        stream.Position = 4;
        stream.Write(size);
        BinaryPrimitives.WriteUInt32LittleEndian(size, repairedDataSize);
        stream.Position = dataOffset - 4;
        stream.Write(size);
        stream.SetLength(repairedLength);
        stream.Flush(flushToDisk: true);
        return true;
    }

    private static bool TryFindDataChunk(FileStream stream, long parseEnd, out long dataOffset, out uint dataSize, out int blockAlign)
    {
        dataOffset = 0;
        dataSize = 0;
        blockAlign = 0;
        var position = (long)RiffHeaderLength;
        Span<byte> chunkHeader = stackalloc byte[ChunkHeaderLength];
        Span<byte> format = stackalloc byte[16];

        while (position + ChunkHeaderLength <= parseEnd)
        {
            stream.Position = position;
            stream.ReadExactly(chunkHeader);
            var chunkId = Encoding.ASCII.GetString(chunkHeader[..4]);
            var chunkSize = BinaryPrimitives.ReadUInt32LittleEndian(chunkHeader[4..]);
            var payloadOffset = position + ChunkHeaderLength;

            if (chunkId == "data")
            {
                dataOffset = payloadOffset;
                dataSize = chunkSize;
                return blockAlign > 0;
            }

            var next = payloadOffset + chunkSize + (chunkSize & 1);
            if (next > parseEnd || next <= position)
                return false;

            if (chunkId == "fmt " && chunkSize >= 16)
            {
                stream.Position = payloadOffset;
                stream.ReadExactly(format);
                blockAlign = BinaryPrimitives.ReadUInt16LittleEndian(format[12..14]);
            }
            position = next;
        }

        return false;
    }

    private static bool IsChunkSequence(FileStream stream, long start, long end, out bool hasData, out bool frameAlignedData)
    {
        hasData = false;
        frameAlignedData = false;
        if (start == end) return true;
        if (start < RiffHeaderLength || end > stream.Length || end < start) return false;

        var position = start;
        Span<byte> chunkHeader = stackalloc byte[ChunkHeaderLength];
        while (position + ChunkHeaderLength <= end)
        {
            stream.Position = position;
            stream.ReadExactly(chunkHeader);
            if (chunkHeader[..4].IndexOfAnyInRange((byte)0, (byte)31) >= 0 || chunkHeader[..4].IndexOfAnyInRange((byte)127, byte.MaxValue) >= 0) return false;
            var chunkSize = BinaryPrimitives.ReadUInt32LittleEndian(chunkHeader[4..]);
            var next = position + ChunkHeaderLength + chunkSize + (chunkSize & 1);
            if (next > end || next <= position) return false;
            if (chunkHeader[..4].SequenceEqual("data"u8))
            {
                hasData = true;
                frameAlignedData = (chunkSize & 1) == 0;
            }
            position = next;
        }
        return position == end;
    }
    private static void TruncateDataToFrameBoundary(FileStream stream, long dataOffset, uint originalDataSize, uint alignedDataSize)
    {
        var oldPaddedEnd = dataOffset + originalDataSize + (originalDataSize & 1);
        var newDataEnd = dataOffset + alignedDataSize;
        var shift = oldPaddedEnd - newDataEnd;
        var sourcePosition = oldPaddedEnd;
        var destinationPosition = newDataEnd;
        var buffer = ArrayPool<byte>.Shared.Rent(81_920);
        try
        {
            while (sourcePosition < stream.Length)
            {
                stream.Position = sourcePosition;
                var count = stream.Read(buffer, 0, (int)Math.Min(buffer.Length, stream.Length - sourcePosition));
                if (count == 0) break;
                stream.Position = destinationPosition;
                stream.Write(buffer.AsSpan(0, count));
                sourcePosition += count;
                destinationPosition += count;
            }
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }

        var repairedLength = stream.Length - shift;
        stream.SetLength(repairedLength);
        WriteUInt32At(stream, dataOffset - 4, alignedDataSize);
        WriteUInt32At(stream, 4, (uint)(repairedLength - 8));
        stream.Flush(flushToDisk: true);
    }

    private static void WriteUInt32At(FileStream stream, long offset, uint value)
    {
        Span<byte> bytes = stackalloc byte[sizeof(uint)];
        BinaryPrimitives.WriteUInt32LittleEndian(bytes, value);
        stream.Position = offset;
        stream.Write(bytes);
    }
}






