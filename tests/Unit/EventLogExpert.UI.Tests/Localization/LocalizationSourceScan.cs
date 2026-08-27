// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using System.Runtime.CompilerServices;

namespace EventLogExpert.UI.Tests.Localization;

/// <summary>
///     The single definition of "production source" for the localization guards. Enumerates every hand-written
///     <c>.cs</c>/<c>.razor</c> file under <c>src/</c> (all producing projects, not just the UI project) with <c>obj/</c>
///     and <c>bin/</c> excluded so a stale generated <c>.g.cs</c> can never keep an orphaned key referenced. Shared by the
///     orphan-reference guard and <c>ProductionSource_NeverPinsThreadCulture</c>.
/// </summary>
internal static class LocalizationSourceScan
{
    public static string RepositoryRoot { get; } = FindRepositoryRoot();

    public static string ResxPath { get; } =
        Path.Combine(RepositoryRoot, "src", "EventLogExpert.Localization", "Resources", "SharedResource.resx");

    public static IEnumerable<string> EnumerateProductionSource() =>
        Directory
            .EnumerateFiles(Path.Combine(RepositoryRoot, "src"), "*.*", SearchOption.AllDirectories)
            .Where(path =>
                (path.EndsWith(".cs", StringComparison.OrdinalIgnoreCase) ||
                    path.EndsWith(".razor", StringComparison.OrdinalIgnoreCase)) &&
                !path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase) &&
                !path.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase));

    private static string FindRepositoryRoot([CallerFilePath] string testFilePath = "")
    {
        for (var directory = new DirectoryInfo(Path.GetDirectoryName(testFilePath)!);
            directory is not null;
            directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, "EventLogExpert.slnx"))) { return directory.FullName; }
        }

        throw new InvalidOperationException("Could not locate the repository root (EventLogExpert.slnx) from the test source path.");
    }
}
