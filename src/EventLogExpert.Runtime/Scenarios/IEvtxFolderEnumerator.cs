// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

namespace EventLogExpert.Runtime.Scenarios;

/// <summary>
///     Lists the <c>.evtx</c> files in a folder (optionally recursing into subfolders), surfacing access and IO
///     failures distinctly.
/// </summary>
public interface IEvtxFolderEnumerator
{
    EvtxFolderScanResult Enumerate(string folderPath, bool includeSubfolders, CancellationToken cancellationToken);
}
