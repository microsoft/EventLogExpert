using EventLogExpert.UI.UnitTests.TestUtils;

namespace EventLogExpert.UI.UnitTests.Models;

public sealed class GitReleaseModelTests
{
    [Fact]
    public void Changes_WhenContainsRawChanges_ShouldRemoveCommitIds()
    {
        var releaseModels = GitHubUtils.CreateGitReleaseModels();

        var result = releaseModels.First().Changes;

        Assert.Equal(16, result.Count);
        Assert.Contains("Updated projects to .NET 8 and updated nuget packages to latest version", result);
        Assert.Contains("Fixed LF issue in App.xaml and added custom width for ultrawide monitors", result);
        Assert.Contains("Updated Azure yml to .NET 8", result);
        Assert.DoesNotContain("f7f7aff67132dc32c92519a1bc250e1a81606e2b", result);
        Assert.DoesNotContain("66b7d6883807a5c518ffcd59f92e07e528a5636a", result);
        Assert.DoesNotContain("5b658a9c294a69cec45a14319d3851b700f0e7a2", result);
    }
}
