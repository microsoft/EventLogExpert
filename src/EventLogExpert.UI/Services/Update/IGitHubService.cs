// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.UI.Models;

namespace EventLogExpert.UI.Services;

public interface IGitHubService
{
    Task<IEnumerable<GitReleaseModel>> GetReleases();
}
