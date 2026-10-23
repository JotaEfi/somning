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
            writer.Write(new byte[16_000 * 2 * 2 + 32000]);
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

    private static void WritePcmWave(string path, int declaredDataBytes, int actualDataBytes)
    {
        var bytes = new byte[44 + actualDataBytes];
        "RIFF"u8.CopyTo(bytes);
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(4), 36 + declaredDataBytes);
        "WAVEfmt "u8.CopyTo(bytes.AsSpan(8));
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(16), 16);
        BinaryPrimitives.WriteInt16LittleEndian(bytes.AsSpan(20), 1);
        BinaryPrimitives.WriteInt16LittleEndian(bytes.AsSpan(22), 1);
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(24), 16_000);
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(28), 32_000);
        BinaryPrimitives.WriteInt16LittleEndian(bytes.AsSpan(32), 2);
        BinaryPrimitives.WriteInt16LittleEndian(bytes.AsSpan(34), 16);
        "data"u8.CopyTo(bytes.AsSpan(36));
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(40), declaredDataBytes);
        File.WriteAllBytes(path, bytes);
    }
}
