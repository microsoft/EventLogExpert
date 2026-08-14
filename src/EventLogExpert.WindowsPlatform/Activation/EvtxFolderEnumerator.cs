// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

namespace EventLogExpert.WindowsPlatform.Activation;

public static class EvtxFolderEnumerator
{
    private const string EvtxSearchPattern = "*.evtx";
    private const string OpenFolderFailedTitle = "Open Folder Failed";

    public static EvtxEnumerationResult EnumerateEvtx(string folderPath, bool includeSubfolders, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(folderPath);

        try
        {
            cancellationToken.ThrowIfCancellationRequested();

            var files = new List<string>();

            CollectTopLevelEvtx(folderPath, files, cancellationToken);

            if (includeSubfolders)
            {
                var pendingDirectories = new Stack<string>();
                PushSubdirectories(folderPath, pendingDirectories, cancellationToken);

                while (pendingDirectories.Count > 0)
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    var directory = pendingDirectories.Pop();

                    try
                    {
                        CollectTopLevelEvtx(directory, files, cancellationToken);
                        PushSubdirectories(directory, pendingDirectories, cancellationToken);
                    }
                    catch (Exception ex) when (ex is UnauthorizedAccessException or IOException)
                    {
                        // Skip a descendant that became unreadable mid-scan; only a failure reading the selected root surfaces.
                    }
                }
            }

            // Directory enumeration has no overload that observes the token mid-scan, so re-check: a cancel requested while
            // an empty directory was walked surfaces here as OperationCanceledException rather than a normal result.
            cancellationToken.ThrowIfCancellationRequested();

            if (files.Count == 0)
            {
                return EvtxEnumerationResult.Empty.Instance;
            }

            return new EvtxEnumerationResult.Success(files);
        }
        catch (UnauthorizedAccessException ex)
        {
            return new EvtxEnumerationResult.AccessDenied(ex.Message);
        }
        catch (IOException ex)
        {
            return new EvtxEnumerationResult.IoError(ex.Message);
        }
    }

    /// <returns>
    ///     Alert copy for failure variants; <c>null</c> for <see cref="EvtxEnumerationResult.Success" /> and
    ///     <see cref="EvtxEnumerationResult.Empty" />.
    /// </returns>
    public static (string Title, string Message)? ToAlertCopy(EvtxEnumerationResult result) => result switch
    {
        EvtxEnumerationResult.AccessDenied a => (OpenFolderFailedTitle, a.Message),
        EvtxEnumerationResult.IoError i => (OpenFolderFailedTitle, i.Message),
        _ => null,
    };

    private static void CollectTopLevelEvtx(string directory, List<string> files, CancellationToken cancellationToken)
    {
        foreach (var file in Directory.EnumerateFiles(directory, EvtxSearchPattern, SearchOption.TopDirectoryOnly))
        {
            cancellationToken.ThrowIfCancellationRequested();
            files.Add(file);
        }
    }

    private static void PushSubdirectories(string directory, Stack<string> pendingDirectories, CancellationToken cancellationToken)
    {
        foreach (var subdirectory in Directory.EnumerateDirectories(directory))
        {
            cancellationToken.ThrowIfCancellationRequested();

            bool isReparsePoint;

            try
            {
                isReparsePoint = (File.GetAttributes(subdirectory) & FileAttributes.ReparsePoint) != 0;
            }
            catch (Exception ex) when (ex is UnauthorizedAccessException or IOException)
            {
                // A child whose attributes cannot be read (denied, or removed mid-scan) is skipped, not fatal to the scan.
                continue;
            }

            // Do not descend reparse points (junctions / symlinks): they can escape the selected tree or cycle indefinitely.
            if (isReparsePoint) { continue; }

            pendingDirectories.Push(subdirectory);
        }
    }
}
