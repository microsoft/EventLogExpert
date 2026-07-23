// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using Xunit;

namespace EventLogExpert.Windows.Tests;

public sealed class PickerSafetyGuardTests
{
    private static readonly string[] s_forbiddenTokens =
    [
        "Windows.Storage.Pickers",           // WinRT FileOpenPicker / FileSavePicker / FolderPicker namespace
        "FilePicker.Default",                // MAUI Microsoft.Maui.Storage.FilePicker default instance
        "Microsoft.Maui.Storage.FilePicker"  // MAUI file picker (fully-qualified reference or using-alias target)
    ];

    // C#, Razor, and XAML sources are scanned: a picker API could be reintroduced via a .razor @using / @code block or
    // a .xaml reference, not only in .cs files.
    private static readonly HashSet<string> s_scannedExtensions =
        new(StringComparer.OrdinalIgnoreCase) { ".cs", ".razor", ".xaml" };

    [Fact]
    public void SourceTree_DoesNotReintroduceElevationUnsafePickers()
    {
        var sourceRoot = ResolveRepoRelativeDirectory("src");
        var offenders = new List<string>();

        foreach (var file in EnumerateScannedFiles(sourceRoot))
        {
            var content = File.ReadAllText(file);

            foreach (var token in s_forbiddenTokens)
            {
                if (content.Contains(token, StringComparison.Ordinal))
                {
                    offenders.Add($"{token}  ->  {file}");
                }
            }
        }

        Assert.True(
            offenders.Count == 0,
            "Elevation-unsafe picker API(s) found in source. Route file/folder picking through IFilePickerService / " +
            "IFolderPickerService (procedural comdlg32 / SHBrowseForFolder) instead of the WinRT/MAUI pickers:" +
            Environment.NewLine + string.Join(Environment.NewLine, offenders));
    }

    private static IEnumerable<string> EnumerateScannedFiles(string root)
    {
        var pending = new Stack<string>();
        pending.Push(root);

        while (pending.Count > 0)
        {
            var directory = pending.Pop();

            foreach (var file in Directory.EnumerateFiles(directory))
            {
                if (s_scannedExtensions.Contains(Path.GetExtension(file)))
                {
                    yield return file;
                }
            }

            foreach (var subdirectory in Directory.EnumerateDirectories(directory))
            {
                var name = Path.GetFileName(subdirectory);

                // Skip obj/bin build outputs, and skip reparse points (directory junctions / symlinks) so a link
                // targeting an ancestor cannot cycle the walk.
                if (name.Equals("obj", StringComparison.OrdinalIgnoreCase)
                    || name.Equals("bin", StringComparison.OrdinalIgnoreCase)
                    || (File.GetAttributes(subdirectory) & FileAttributes.ReparsePoint) != 0)
                {
                    continue;
                }

                pending.Push(subdirectory);
            }
        }
    }

    private static string ResolveRepoRelativeDirectory(params string[] segments)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);

        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "EventLogExpert.slnx")))
        {
            directory = directory.Parent;
        }

        Assert.NotNull(directory);

        var combined = Path.Combine([directory.FullName, .. segments]);
        Assert.True(Directory.Exists(combined), $"Expected directory at {combined} to exist.");

        return combined;
    }
}
