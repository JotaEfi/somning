namespace Somning.Core.Update;

/// <summary>A newer release published on GitHub, with the installer to run and the SHA-256 it must match.</summary>
public sealed record UpdateInfo(Version Version, string Tag, string AssetName, Uri DownloadUrl, string Sha256, long SizeBytes, Uri ReleaseUrl);
