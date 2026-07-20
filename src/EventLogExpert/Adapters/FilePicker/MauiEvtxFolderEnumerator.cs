// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Runtime.Scenarios;
using EventLogExpert.WindowsPlatform.Activation;

namespace EventLogExpert.Adapters.FilePicker;

/// <summary>MAUI adapter that maps <see cref="EvtxFolderEnumerator" /> onto the Runtime folder-scan contract.</summary>
internal sealed class MauiEvtxFolderEnumerator : IEvtxFolderEnumerator
{
    public EvtxFolderScanResult EnumerateTopLevel(string folderPath, CancellationToken cancellationToken) =>
        EvtxFolderEnumerator.EnumerateEvtxTopLevel(folderPath, cancellationToken) switch
        {
            EvtxEnumerationResult.Success success => new EvtxFolderScanResult.Files([.. success.Files]),
            EvtxEnumerationResult.Empty => EvtxFolderScanResult.Empty.Instance,
            EvtxEnumerationResult.AccessDenied denied => new EvtxFolderScanResult.AccessDenied(denied.Message),
            EvtxEnumerationResult.IoError ioError => new EvtxFolderScanResult.IoError(ioError.Message),
            _ => new EvtxFolderScanResult.IoError("The folder could not be read.")
        };
}
