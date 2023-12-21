using EventLogExpert.UI.Interfaces;
using EventLogExpert.UI.Services;
using EventLogExpert.UI.Tests.TestUtils.Constants;
using NSubstitute;

namespace EventLogExpert.UI.Tests.Services;

public sealed class AppTitleServiceTests
{
    private readonly ICurrentVersionProvider _mockCurrentVersionProvider = Substitute.For<ICurrentVersionProvider>();
    private readonly ITitleProvider _mockTitleProvider = Substitute.For<ITitleProvider>();

    public AppTitleServiceTests()
    {
        _mockCurrentVersionProvider.CurrentVersion.Returns(new Version(Constants.AppInstalledVersion));
    }

    [Fact]
    public void SetIsPrerelease_WhenLogName_ShouldSetTitle()
    {
        AppTitleService titleService = new(_mockCurrentVersionProvider, _mockTitleProvider);

        titleService.SetIsPrerelease(true);
        titleService.SetLogName(Constants.LogName);

        _mockTitleProvider.Received(1)
            .SetTitle($"{Constants.LogName} - {Constants.AppName} (Preview) {Constants.AppInstalledVersion}");
    }

    [Fact]
    public void SetLogName_WhenDevBuild_ShouldSetTitle()
    {
        AppTitleService titleService = new(_mockCurrentVersionProvider, _mockTitleProvider);

        _mockCurrentVersionProvider.IsDevBuild.Returns(true);

        titleService.SetLogName(null);

        _mockTitleProvider.Received(1).SetTitle($"{Constants.AppName} (Development)");
    }

    [Fact]
    public void SetLogName_WhenLogName_ShouldSetTitle()
    {
        AppTitleService titleService = new(_mockCurrentVersionProvider, _mockTitleProvider);

        titleService.SetLogName(Constants.LogName);

        _mockTitleProvider.Received(1)
            .SetTitle($"{Constants.LogName} - {Constants.AppName} {Constants.AppInstalledVersion}");
    }

    [Fact]
    public void SetLogName_WhenNullLogName_ShouldSetTitle()
    {
        AppTitleService titleService = new(_mockCurrentVersionProvider, _mockTitleProvider);

        titleService.SetLogName(null);

        _mockTitleProvider.Received(1).SetTitle($"{Constants.AppName} {Constants.AppInstalledVersion}");
    }

    [Fact]
    public void SetProgressString_WhenNullProgress_ShouldSetTitle()
    {
        AppTitleService titleService = new(_mockCurrentVersionProvider, _mockTitleProvider);

        titleService.SetProgressString(null);

        _mockTitleProvider.Received(1).SetTitle($"{Constants.AppName} {Constants.AppInstalledVersion}");
    }

    [Fact]
    public void SetProgressString_WhenProgress_ShouldSetTitle()
    {
        AppTitleService titleService = new(_mockCurrentVersionProvider, _mockTitleProvider);

        titleService.SetProgressString(Constants.Percentage);

        _mockTitleProvider.Received(1)
            .SetTitle($"{Constants.Percentage} - {Constants.AppName} {Constants.AppInstalledVersion}");
    }
}
