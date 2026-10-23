namespace Somning.Core.Model;

/// <summary>Snapshot reported while a model file downloads (STT-07, APP-05).</summary>
/// <param name="BytesDownloaded">Bytes already stored in the <c>.part</c> file, including a resumed prefix.</param>
/// <param name="TotalBytes">Expected final size, or <c>0</c> when the server did not disclose it.</param>
/// <param name="Percent">Completion in <c>[0, 100]</c>; <c>0</c> while the total size is unknown.</param>
/// <param name="SpeedBps">Transfer rate of the current attempt in bytes per second.</param>
public sealed record ModelDownloadProgress(long BytesDownloaded, long TotalBytes, double Percent, long SpeedBps);
