using System.Buffers.Binary;
using NAudio.Wave;
using Somning.Platform.Windows.Recording;
using Xunit;

namespace Somning.Integration.Tests.Audio;

public sealed class SpoolDurabilityTests
{
    [Fact]
    public void Repairs_abandoned_spool_after_writer_handle_closes()
    {
        var root = Path.Combine(Path.GetTempPath(), "somning-spool-recovery", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var path = Path.Combine(root, "abandoned.wav");
        try
        {
            var writer = new SpoolWriter(path);
            var pcm = new byte[16_000 * 2 * 2 + 16_000];
            writer.Write(pcm.AsSpan(0, 32_000));
            writer.Write(pcm.AsSpan(32_000, 32_000));
            writer.Write(pcm.AsSpan(64_000));
            writer.AbandonForTest();

            Assert.True(WavHeaderRepair.EnsureValid(path));
            using var reader = new WaveFileReader(path);
            Assert.Equal(16_000, reader.WaveFormat.SampleRate);
            Assert.InRange(reader.TotalTime.TotalSeconds, 1.5, 3.0);
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void Repairs_data_length_from_file_and_leaves_consistent_files_untouched()
    {
        var root = Path.Combine(Path.GetTempPath(), "somning-wav-header", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var path = Path.Combine(root, "header.wav");
        try
        {
            WritePcmWave(path, declaredDataBytes: 0, actualDataBytes: 64_000);
            Assert.True(WavHeaderRepair.EnsureValid(path));
            Assert.Equal(64_000, BinaryPrimitives.ReadInt32LittleEndian(File.ReadAllBytes(path).AsSpan(40, 4)));

            var consistent = Path.Combine(root, "consistent.wav");
            WritePcmWave(consistent, declaredDataBytes: 64_000, actualDataBytes: 64_000);
            var before = File.ReadAllBytes(consistent);
            Assert.False(WavHeaderRepair.EnsureValid(consistent));
            Assert.Equal(before, File.ReadAllBytes(consistent));
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void Truncates_partial_audio_frame_without_counting_riff_padding_as_samples()
    {
        var root = Path.Combine(Path.GetTempPath(), "somning-wav-partial-frame", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var path = Path.Combine(root, "partial-frame.wav");
        try
        {
            WritePcmWave(path, declaredDataBytes: 3, actualDataBytes: 3, channels: 1);

            Assert.True(WavHeaderRepair.EnsureValid(path));
            using var reader = new WaveFileReader(path);
            Assert.Equal(2, reader.Length);
            Assert.Equal(2, BinaryPrimitives.ReadInt32LittleEndian(File.ReadAllBytes(path).AsSpan(40, 4)));
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }
    [Fact]
    public void Repairs_riff_size_that_ends_before_the_data_chunk()
    {
        var root = Path.Combine(Path.GetTempPath(), "somning-wav-short-riff", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var path = Path.Combine(root, "short-riff.wav");
        try
        {
            WritePcmWave(path, declaredDataBytes: 64_000, actualDataBytes: 64_000);
            using (var stream = new FileStream(path, FileMode.Open, FileAccess.Write, FileShare.None))
            {
                stream.Position = 4;
                stream.Write(new byte[4]);
            }

            Assert.True(WavHeaderRepair.EnsureValid(path));
            using var reader = new WaveFileReader(path);
            Assert.Equal(64_000, reader.Length);
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }
    [Fact]
    public void Repairs_audio_after_a_list_chunk_before_data()
    {
        var root = Path.Combine(Path.GetTempPath(), "somning-wav-list", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var path = Path.Combine(root, "list.wav");
        try
        {
            WritePcmWave(path, declaredDataBytes: 0, actualDataBytes: 64_000, includeListChunk: true);

            Assert.True(WavHeaderRepair.EnsureValid(path));
            using var reader = new WaveFileReader(path);
            Assert.Equal(2, reader.WaveFormat.Channels);
            Assert.Equal(4, reader.WaveFormat.BlockAlign);
            Assert.Equal(1, reader.TotalTime.TotalSeconds, precision: 3);
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }
    [Fact]
    public void Leaves_consistent_list_chunk_after_data_untouched()
    {
        var root = Path.Combine(Path.GetTempPath(), "somning-wav-trailing-list", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var path = Path.Combine(root, "trailing-list.wav");
        try
        {
            WritePcmWave(path, declaredDataBytes: 64_000, actualDataBytes: 64_000, includeTrailingList: true);
            var before = File.ReadAllBytes(path);

            Assert.False(WavHeaderRepair.EnsureValid(path));
            Assert.Equal(before, File.ReadAllBytes(path));
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }
    private static void WritePcmWave(string path, int declaredDataBytes, int actualDataBytes, bool includeListChunk = false, bool includeTrailingList = false, int channels = 2)
    {
        var listBytes = includeListChunk ? 12 : 0;
        var trailingListBytes = includeTrailingList ? 12 : 0;
        var dataHeaderOffset = 36 + listBytes;
        var blockAlign = channels * 2;
        var paddingBytes = actualDataBytes & 1;
        var bytes = new byte[44 + listBytes + actualDataBytes + paddingBytes + trailingListBytes];
        "RIFF"u8.CopyTo(bytes);
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(4), 36 + listBytes + declaredDataBytes + (declaredDataBytes & 1) + trailingListBytes);
        "WAVEfmt "u8.CopyTo(bytes.AsSpan(8));
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(16), 16);
        BinaryPrimitives.WriteInt16LittleEndian(bytes.AsSpan(20), 1);
        BinaryPrimitives.WriteInt16LittleEndian(bytes.AsSpan(22), (short)channels);
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(24), 16_000);
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(28), 16_000 * blockAlign);
        BinaryPrimitives.WriteInt16LittleEndian(bytes.AsSpan(32), (short)blockAlign);
        BinaryPrimitives.WriteInt16LittleEndian(bytes.AsSpan(34), 16);
        if (includeListChunk)
        {
            "LIST"u8.CopyTo(bytes.AsSpan(36));
            BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(40), 4);
            "INFO"u8.CopyTo(bytes.AsSpan(44));
        }
        "data"u8.CopyTo(bytes.AsSpan(dataHeaderOffset));
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(dataHeaderOffset + 4), declaredDataBytes);
        if (includeTrailingList)
        {
            var trailingOffset = dataHeaderOffset + 8 + actualDataBytes + paddingBytes;
            "LIST"u8.CopyTo(bytes.AsSpan(trailingOffset));
            BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(trailingOffset + 4), 4);
            "INFO"u8.CopyTo(bytes.AsSpan(trailingOffset + 8));
        }
        File.WriteAllBytes(path, bytes);
    }
}









