// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

namespace EventLogExpert.UI.Update;

public interface IGitHubService
{
    Task<IEnumerable<GitHubRelease>> GetReleases();
}
