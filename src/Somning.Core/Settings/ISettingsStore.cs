namespace Somning.Core.Settings;

public interface ISettingsStore
{
    string FilePath { get; }
    Task<UserSettings> LoadAsync(CancellationToken cancellationToken = default);
    Task SaveAsync(UserSettings settings, CancellationToken cancellationToken = default);
}
