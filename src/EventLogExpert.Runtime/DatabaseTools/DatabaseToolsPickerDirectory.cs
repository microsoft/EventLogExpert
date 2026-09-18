// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

namespace EventLogExpert.Runtime.DatabaseTools;

public sealed class DatabaseToolsPickerDirectory(
    IDatabaseToolsPickerPreferencesProvider preferencesProvider,
    IDatabaseToolsFallbackDirectoryProvider fallbackDirectoryProvider,
    IDirectoryExistence directoryExistence) : IDatabaseToolsPickerDirectory
{
    private readonly IDirectoryExistence _directoryExistence = directoryExistence;
    private readonly IDatabaseToolsFallbackDirectoryProvider _fallbackDirectoryProvider = fallbackDirectoryProvider;
    private readonly IDatabaseToolsPickerPreferencesProvider _preferencesProvider = preferencesProvider;

    public void RememberDirectory(DatabaseToolsPickRole role, string? pickedPath)
    {
        if (pickedPath is null) { return; }

        string? directory = Path.GetDirectoryName(pickedPath);
        if (directory is not null)
        {
            _preferencesProvider.SetLastDirectory(role, directory);
        }
    }

    public async Task<string?> ResolveInitialDirectoryAsync(DatabaseToolsPickRole role)
    {
        string? persistedDirectory = _preferencesProvider.GetLastDirectory(role);

        if (persistedDirectory is not null && await _directoryExistence.ExistsAsync(persistedDirectory))
        {
            return persistedDirectory;
        }

        foreach (string fallbackDirectory in _fallbackDirectoryProvider.GetFallbackDirectories(role))
        {
            if (await _directoryExistence.ExistsAsync(fallbackDirectory))
            {
                return fallbackDirectory;
            }
        }

        return null;
    }
}
