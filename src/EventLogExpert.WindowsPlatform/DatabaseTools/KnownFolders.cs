// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using System.Runtime.InteropServices;

namespace EventLogExpert.WindowsPlatform.DatabaseTools;

internal static partial class KnownFolders
{
    private static readonly Guid s_folderIdDownloads = new("374DE290-123F-4565-9164-39C4925E467B");

    public static string? Documents => Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);

    public static string? Downloads => SHGetKnownFolderPath(s_folderIdDownloads, 0, IntPtr.Zero, out string path) == 0 ? path : null;

    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [LibraryImport("shell32.dll", StringMarshalling = StringMarshalling.Utf16)]
    private static partial int SHGetKnownFolderPath(in Guid rfid, uint dwFlags, IntPtr hToken, out string ppszPath);
}
