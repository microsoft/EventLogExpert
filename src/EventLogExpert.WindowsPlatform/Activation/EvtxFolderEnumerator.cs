// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

namespace EventLogExpert.WindowsPlatform.Activation;

public static class EvtxFolderEnumerator
{
    private const string EvtxSearchPattern = "*.evtx";
    private const string OpenFolderFailedTitle = "Open Folder Failed";

    public static EvtxEnumerationResult EnumerateEvtxTopLevel(string folderPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(folderPath);

        try
        {
            var files = Directory.EnumerateFiles(folderPath, EvtxSearchPattern, SearchOption.TopDirectoryOnly)
                .ToList();

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
}
