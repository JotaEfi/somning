using Somning.Core.Recording;
using Somning.Platform.Windows.Recording;
using Xunit;

namespace Somning.Integration.Tests.Audio;

public sealed class SidecarStoreTests
{
    [Fact]
    public async Task Concurrent_saves_use_independent_atomic_temporary_files()
    {
        var root = Path.Combine(Path.GetTempPath(), "somning-sidecar", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var store = new SidecarStore(root);
            var values = Enumerable.Range(0, 32).Select(index => $"text-{index}").ToArray();
            await Task.WhenAll(values.Select(value => Task.Run(() => store.Save(new RecordingSidecar(
                "shared-id", DateTimeOffset.UtcNow, RecordingStage.Transcribed, "harness", value,
                "text", null, 0, null)))));

            var saved = store.Load("shared-id");
            Assert.NotNull(saved);
            Assert.Contains(saved.Text, values);
            Assert.Empty(Directory.EnumerateFiles(root, "*.tmp"));
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }
}
