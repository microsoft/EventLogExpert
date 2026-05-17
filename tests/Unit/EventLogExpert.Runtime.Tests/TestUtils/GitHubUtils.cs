// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Runtime.Update;
using static EventLogExpert.Runtime.Tests.TestUtils.Constants.Constants;

namespace EventLogExpert.Runtime.Tests.TestUtils;

public static class GitHubUtils
{
    public static IEnumerable<GitHubRelease> CreateGitHubReleases() =>
    [
        new()
        {
            Version = GitHubPrereleaseVersion,
            IsPreRelease = true,
            ReleaseDate = DateTime.Now,
            Assets =
            [
                new GitHubReleaseAsset
                {
                    Name = GitHubPrereleaseName,
                    Uri = GitHubPrereleaseUri,
                }
            ],
            RawChanges = GitHubReleaseNotes
        },
        new()
        {
            Version = GitHubLatestVersion,
            IsPreRelease = false,
            ReleaseDate = DateTime.Now.AddDays(-1),
            Assets =
            [
                new GitHubReleaseAsset
                {
                    Name = GitHubLatestName,
                    Uri = GitHubLatestUri
                }
            ],
            RawChanges = GitHubReleaseNotes
        }
    ];
}
