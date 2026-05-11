// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.UI.Update;
using static EventLogExpert.UI.Tests.TestUtils.Constants.Constants;

namespace EventLogExpert.UI.Tests.TestUtils;

public static class GitHubUtils
{
    public static IEnumerable<GitReleaseModel> CreateGitReleaseModels() =>
    [
        new()
        {
            Version = GitHubPrereleaseVersion,
            IsPreRelease = true,
            ReleaseDate = DateTime.Now,
            Assets =
            [
                new GitReleaseAsset
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
                new GitReleaseAsset
                {
                    Name = GitHubLatestName,
                    Uri = GitHubLatestUri
                }
            ],
            RawChanges = GitHubReleaseNotes
        }
    ];
}
