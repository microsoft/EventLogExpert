using EventLogExpert.Eventing.Helpers;
using EventLogExpert.UI.Services;
using NSubstitute;

namespace EventLogExpert.UI.Tests.Services;

public sealed class GitHubServiceTests
{
    private readonly ITraceLogger _mockTraceLogger = Substitute.For<ITraceLogger>();

    [Fact]
    public void GetReleases_ShouldReturnContent()
    {
        var gitHubService = new GitHubService(_mockTraceLogger);

        var content = gitHubService.GetReleases();

        Assert.NotNull(content);
    }
}
