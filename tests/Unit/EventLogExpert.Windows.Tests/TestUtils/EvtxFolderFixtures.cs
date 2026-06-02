// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

namespace EventLogExpert.Windows.Tests.TestUtils;

internal static class EvtxFolderFixtures
{
    private const string TempFolderPrefix = "evtx-enumerator-tests-";

    public static string CreateTempTestFolder()
    {
        var folder = Path.Combine(Path.GetTempPath(), TempFolderPrefix + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        return folder;
    }

    public static void TryDeleteFolder(string folder)
    {
        try
        {
            Directory.Delete(folder, recursive: true);
        }
        catch
        {
            // best-effort cleanup
        }
    }

    public static void WriteEmptyFile(string folder, string fileName) =>
        File.WriteAllText(Path.Combine(folder, fileName), string.Empty);
}
