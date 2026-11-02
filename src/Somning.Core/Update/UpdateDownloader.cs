using System.Security.Cryptography;

namespace Somning.Core.Update;

public sealed class UpdateVerificationException(string message) : Exception(message);

/// <summary>Downloads the installer into a private folder and only returns it once size and SHA-256 match the release.</summary>
public sealed class UpdateDownloader
{
    private const int BufferSize = 81_920;
    private static readonly HttpClient DefaultHttpClient = new() { Timeout = TimeSpan.FromMinutes(30) };
    private readonly HttpClient httpClient;

    public UpdateDownloader(HttpClient? httpClient = null) => this.httpClient = httpClient ?? DefaultHttpClient;

    /// <returns>Full path of the verified installer inside <paramref name="directory"/>.</returns>
    public async Task<string> DownloadAsync(UpdateInfo update, string directory, IProgress<double>? progress = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(update);
        Directory.CreateDirectory(directory);
        var target = Path.Combine(directory, update.AssetName);
        var partial = target + ".part";
        try
        {
            using var response = await httpClient.GetAsync(update.DownloadUrl, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
            response.EnsureSuccessStatusCode();
            using var hash = SHA256.Create();
            long total = 0;
            await using (var source = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false))
            await using (var file = new FileStream(partial, FileMode.Create, FileAccess.Write, FileShare.None, BufferSize, useAsync: true))
            {
                var buffer = new byte[BufferSize];
                int read;
                while ((read = await source.ReadAsync(buffer, cancellationToken).ConfigureAwait(false)) > 0)
                {
                    hash.TransformBlock(buffer, 0, read, null, 0);
                    await file.WriteAsync(buffer.AsMemory(0, read), cancellationToken).ConfigureAwait(false);
                    total += read;
                    if (total > update.SizeBytes) throw new UpdateVerificationException("O arquivo baixado é maior do que o esperado.");
                    progress?.Report((double)total / update.SizeBytes);
                }
            }
            hash.TransformFinalBlock([], 0, 0);
            if (total != update.SizeBytes) throw new UpdateVerificationException("O arquivo baixado está incompleto.");
            if (!Convert.ToHexString(hash.Hash!).Equals(update.Sha256, StringComparison.OrdinalIgnoreCase))
                throw new UpdateVerificationException("A verificação SHA-256 do instalador falhou.");
            File.Move(partial, target, overwrite: true);
            return target;
        }
        catch
        {
            try { File.Delete(partial); } catch (IOException) { }
            throw;
        }
    }
}
