// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Runtime.Common.AppTitle;
using EventLogExpert.Runtime.Common.Versioning;
using EventLogExpert.Runtime.Tests.TestUtils.Constants;
using NSubstitute;

namespace EventLogExpert.Runtime.Tests.Common.AppTitle;

public sealed class AppTitleServiceTests
{
    [Fact]
    public void SetIsPrerelease_ThenSetLogName_ShouldForwardPrereleaseState()
    {
        // Arrange
        var mockCurrentVersionProvider = Substitute.For<ICurrentVersionProvider>();
        mockCurrentVersionProvider.CurrentVersion.Returns(new Version(Constants.AppInstalledVersion));
        mockCurrentVersionProvider.IsAdmin.Returns(true);

        var mockComposer = Substitute.For<IAppTitleTextComposer>();

        var titleService = CreateAppTitleService(mockCurrentVersionProvider, mockComposer);

        // Act
        titleService.SetIsPrerelease(true);
        titleService.SetLogName(Constants.LogName);

        // Assert
        mockComposer.Received(1).Compose(new AppTitleState(
            Progress: null,
            IsDevBuild: false,
            IsPrerelease: true,
            IsAdmin: true,
            Version: Constants.AppInstalledVersion,
            LogName: Constants.LogName));
    }

    [Fact]
    public void SetIsPrerelease_WhenCalledAlone_ShouldNotCompose()
    {
        // Arrange
        var mockComposer = Substitute.For<IAppTitleTextComposer>();

        var titleService = CreateAppTitleService(composer: mockComposer);

        // Act
        titleService.SetIsPrerelease(true);

        // Assert
        mockComposer.DidNotReceive().Compose(Arg.Any<AppTitleState>());
    }

    [Fact]
    public void SetLogName_WhenAdmin_ShouldForwardIsAdmin()
    {
        // Arrange
        var mockCurrentVersionProvider = Substitute.For<ICurrentVersionProvider>();
        mockCurrentVersionProvider.CurrentVersion.Returns(new Version(Constants.AppInstalledVersion));
        mockCurrentVersionProvider.IsAdmin.Returns(true);

        var mockComposer = Substitute.For<IAppTitleTextComposer>();

        var titleService = CreateAppTitleService(mockCurrentVersionProvider, mockComposer);

        // Act
        titleService.SetLogName(Constants.LogName);

        // Assert
        mockComposer.Received(1).Compose(new AppTitleState(
            Progress: null,
            IsDevBuild: false,
            IsPrerelease: false,
            IsAdmin: true,
            Version: Constants.AppInstalledVersion,
            LogName: Constants.LogName));
    }

    [Fact]
    public void SetLogName_WhenCalled_ShouldForwardStateWithVersionAndLogName()
    {
        // Arrange
        var mockCurrentVersionProvider = Substitute.For<ICurrentVersionProvider>();
        mockCurrentVersionProvider.CurrentVersion.Returns(new Version(Constants.AppInstalledVersion));

        var mockComposer = Substitute.For<IAppTitleTextComposer>();

        var titleService = CreateAppTitleService(mockCurrentVersionProvider, mockComposer);

        // Act
        titleService.SetLogName(Constants.LogName);

        // Assert
        mockComposer.Received(1).Compose(new AppTitleState(
            Progress: null,
            IsDevBuild: false,
            IsPrerelease: false,
            IsAdmin: false,
            Version: Constants.AppInstalledVersion,
            LogName: Constants.LogName));
    }

    [Fact]
    public void SetLogName_WhenDevBuild_ShouldForwardIsDevBuild()
    {
        // Arrange
        var mockCurrentVersionProvider = Substitute.For<ICurrentVersionProvider>();
        mockCurrentVersionProvider.CurrentVersion.Returns(new Version(Constants.AppInstalledVersion));
        mockCurrentVersionProvider.IsDevBuild.Returns(true);

        var mockComposer = Substitute.For<IAppTitleTextComposer>();

        var titleService = CreateAppTitleService(mockCurrentVersionProvider, mockComposer);

        // Act
        titleService.SetLogName(null);

        // Assert
        mockComposer.Received(1).Compose(new AppTitleState(
            Progress: null,
            IsDevBuild: true,
            IsPrerelease: false,
            IsAdmin: false,
            Version: Constants.AppInstalledVersion,
            LogName: null));
    }

    [Fact]
    public void SetLogName_WhenNull_ShouldForwardStateWithNullLogName()
    {
        // Arrange
        var mockCurrentVersionProvider = Substitute.For<ICurrentVersionProvider>();
        mockCurrentVersionProvider.CurrentVersion.Returns(new Version(Constants.AppInstalledVersion));

        var mockComposer = Substitute.For<IAppTitleTextComposer>();

        var titleService = CreateAppTitleService(mockCurrentVersionProvider, mockComposer);

        // Act
        titleService.SetLogName(null);

        // Assert
        mockComposer.Received(1).Compose(new AppTitleState(
            Progress: null,
            IsDevBuild: false,
            IsPrerelease: false,
            IsAdmin: false,
            Version: Constants.AppInstalledVersion,
            LogName: null));
    }

    [Fact]
    public void SetProgress_WhenClearedAfterSet_ShouldForwardNullProgress()
    {
        // Arrange
        var mockCurrentVersionProvider = Substitute.For<ICurrentVersionProvider>();
        mockCurrentVersionProvider.CurrentVersion.Returns(new Version(Constants.AppInstalledVersion));

        var mockComposer = Substitute.For<IAppTitleTextComposer>();

        var titleService = CreateAppTitleService(mockCurrentVersionProvider, mockComposer);

        // Act
        titleService.SetProgress(new AppTitleProgress.Installing(50));
        titleService.SetProgress(null);

        // Assert
        mockComposer.Received(1).Compose(new AppTitleState(
            Progress: null,
            IsDevBuild: false,
            IsPrerelease: false,
            IsAdmin: false,
            Version: Constants.AppInstalledVersion,
            LogName: null));
    }

    [Fact]
    public void SetProgress_WhenInstalling_ShouldForwardProgress()
    {
        // Arrange
        var mockCurrentVersionProvider = Substitute.For<ICurrentVersionProvider>();
        mockCurrentVersionProvider.CurrentVersion.Returns(new Version(Constants.AppInstalledVersion));

        var mockComposer = Substitute.For<IAppTitleTextComposer>();

        var titleService = CreateAppTitleService(mockCurrentVersionProvider, mockComposer);

        // Act
        titleService.SetProgress(new AppTitleProgress.Installing(50));

        // Assert
        mockComposer.Received(1).Compose(new AppTitleState(
            Progress: new AppTitleProgress.Installing(50),
            IsDevBuild: false,
            IsPrerelease: false,
            IsAdmin: false,
            Version: Constants.AppInstalledVersion,
            LogName: null));
    }

    [Fact]
    public void SetProgress_WhenProgressAndLogName_ShouldForwardBoth()
    {
        // Arrange
        var mockCurrentVersionProvider = Substitute.For<ICurrentVersionProvider>();
        mockCurrentVersionProvider.CurrentVersion.Returns(new Version(Constants.AppInstalledVersion));

        var mockComposer = Substitute.For<IAppTitleTextComposer>();

        var titleService = CreateAppTitleService(mockCurrentVersionProvider, mockComposer);

        // Act
        titleService.SetLogName(Constants.LogName);
        titleService.SetProgress(new AppTitleProgress.Installing(50));

        // Assert
        mockComposer.Received(1).Compose(new AppTitleState(
            Progress: new AppTitleProgress.Installing(50),
            IsDevBuild: false,
            IsPrerelease: false,
            IsAdmin: false,
            Version: Constants.AppInstalledVersion,
            LogName: Constants.LogName));
    }

    [Fact]
    public void SetProgress_WhenRelaunch_ShouldForwardProgress()
    {
        // Arrange
        var mockCurrentVersionProvider = Substitute.For<ICurrentVersionProvider>();
        mockCurrentVersionProvider.CurrentVersion.Returns(new Version(Constants.AppInstalledVersion));

        var mockComposer = Substitute.For<IAppTitleTextComposer>();

        var titleService = CreateAppTitleService(mockCurrentVersionProvider, mockComposer);

        // Act
        titleService.SetProgress(new AppTitleProgress.RelaunchToApply());

        // Assert
        mockComposer.Received(1).Compose(new AppTitleState(
            Progress: new AppTitleProgress.RelaunchToApply(),
            IsDevBuild: false,
            IsPrerelease: false,
            IsAdmin: false,
            Version: Constants.AppInstalledVersion,
            LogName: null));
    }

    private static AppTitleService CreateAppTitleService(
        ICurrentVersionProvider? currentVersionProvider = null,
        IAppTitleTextComposer? composer = null)
    {
        return new AppTitleService(
            currentVersionProvider ?? Substitute.For<ICurrentVersionProvider>(),
            composer ?? Substitute.For<IAppTitleTextComposer>());
    }
}
