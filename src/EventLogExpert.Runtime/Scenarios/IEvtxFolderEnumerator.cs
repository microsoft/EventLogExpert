// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

namespace EventLogExpert.Runtime.Scenarios;

/// <summary>Lists the top-level <c>.evtx</c> files in a folder, surfacing access and IO failures distinctly.</summary>
public interface IEvtxFolderEnumerator
{
    EvtxFolderScanResult EnumerateTopLevel(string folderPath);
}
