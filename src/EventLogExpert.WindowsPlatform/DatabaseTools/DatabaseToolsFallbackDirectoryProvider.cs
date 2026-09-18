// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Runtime.DatabaseTools;

namespace EventLogExpert.WindowsPlatform.DatabaseTools;

internal sealed class DatabaseToolsFallbackDirectoryProvider : IDatabaseToolsFallbackDirectoryProvider
{
    public IReadOnlyList<string> GetFallbackDirectories(DatabaseToolsPickRole role) =>
        SelectFallbackDirectories(role, KnownFolders.Downloads, KnownFolders.Documents);

    internal static IReadOnlyList<string> SelectFallbackDirectories(DatabaseToolsPickRole role, string? downloads, string? documents) => role switch
    {
        DatabaseToolsPickRole.Source => NonNull(downloads, documents),
        DatabaseToolsPickRole.Output => NonNull(documents),
        DatabaseToolsPickRole.ExistingDatabase => NonNull(documents),
        _ => throw new ArgumentOutOfRangeException(nameof(role), role, null)
    };

    private static IReadOnlyList<string> NonNull(params string?[] candidates) => [.. candidates.OfType<string>()];
}
