using EventLogExpert.UI.Models;
using static EventLogExpert.UI.UnitTests.TestUtils.Constants.Constants;

namespace EventLogExpert.UI.UnitTests.TestUtils;

public static class GitHubUtils
{
    public static IEnumerable<GitReleaseModel> CreateGitReleaseModels() =>
    [
        new GitReleaseModel
        {
            Version = GitHubPrereleaseVersion,
            IsPrerelease = true,
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
        new GitReleaseModel
        {
            Version = GitHubLatestVersion,
            IsPrerelease = false,
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
