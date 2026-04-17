// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.UI.Interfaces;
using EventLogExpert.UI.Services;
using EventLogExpert.UI.Tests.TestUtils.Constants;
using NSubstitute;

namespace EventLogExpert.UI.Tests.Services;

public sealed class AppTitleServiceTests
{
    [Fact]
    public void SetIsPrerelease_WhenAdminAndPrerelease_ShouldSetTitle()
    {
        // Arrange
        var mockCurrentVersionProvider = Substitute.For<ICurrentVersionProvider>();
        mockCurrentVersionProvider.CurrentVersion.Returns(new Version(Constants.AppInstalledVersion));
        mockCurrentVersionProvider.IsAdmin.Returns(true);

        var mockTitleProvider = Substitute.For<ITitleProvider>();

        var titleService = CreateAppTitleService(
            mockCurrentVersionProvider,
            mockTitleProvider);

        // Act
        titleService.SetIsPrerelease(true);
        titleService.SetLogName(Constants.LogName);

        // Assert
        mockTitleProvider.Received(1)
            .SetTitle($"{Constants.AppName} (Preview) {Constants.AppInstalledVersion} (Admin) - {Constants.LogName}");
    }

    [Fact]
    public void SetIsPrerelease_WhenLogName_ShouldSetTitle()
    {
        // Arrange
        var mockCurrentVersionProvider = Substitute.For<ICurrentVersionProvider>();
        mockCurrentVersionProvider.CurrentVersion.Returns(new Version(Constants.AppInstalledVersion));

        var mockTitleProvider = Substitute.For<ITitleProvider>();

        var titleService = CreateAppTitleService(
            mockCurrentVersionProvider,
            mockTitleProvider);

        // Act
        titleService.SetIsPrerelease(true);
        titleService.SetLogName(Constants.LogName);

        // Assert
        mockTitleProvider.Received(1)
            .SetTitle($"{Constants.AppName} (Preview) {Constants.AppInstalledVersion} - {Constants.LogName}");
    }

    [Fact]
    public void SetLogName_WhenAdminAndDevBuild_ShouldSetTitle()
    {
        // Arrange
        var mockCurrentVersionProvider = Substitute.For<ICurrentVersionProvider>();
        mockCurrentVersionProvider.CurrentVersion.Returns(new Version(Constants.AppInstalledVersion));
        mockCurrentVersionProvider.IsDevBuild.Returns(true);
        mockCurrentVersionProvider.IsAdmin.Returns(true);

        var mockTitleProvider = Substitute.For<ITitleProvider>();

        var titleService = CreateAppTitleService(
            mockCurrentVersionProvider,
            mockTitleProvider);

        // Act
        titleService.SetLogName(null);

        // Assert
        mockTitleProvider.Received(1).SetTitle($"{Constants.AppName} (Development) (Admin)");
    }

    [Fact]
    public void SetLogName_WhenAdminAndLogName_ShouldSetTitle()
    {
        // Arrange
        var mockCurrentVersionProvider = Substitute.For<ICurrentVersionProvider>();
        mockCurrentVersionProvider.CurrentVersion.Returns(new Version(Constants.AppInstalledVersion));
        mockCurrentVersionProvider.IsAdmin.Returns(true);

        var mockTitleProvider = Substitute.For<ITitleProvider>();

        var titleService = CreateAppTitleService(
            mockCurrentVersionProvider,
            mockTitleProvider);

        // Act
        titleService.SetLogName(Constants.LogName);

        // Assert
        mockTitleProvider.Received(1)
            .SetTitle($"{Constants.AppName} {Constants.AppInstalledVersion} (Admin) - {Constants.LogName}");
    }

    [Fact]
    public void SetLogName_WhenDevBuild_ShouldSetTitle()
    {
        // Arrange
        var mockCurrentVersionProvider = Substitute.For<ICurrentVersionProvider>();
        mockCurrentVersionProvider.CurrentVersion.Returns(new Version(Constants.AppInstalledVersion));
        mockCurrentVersionProvider.IsDevBuild.Returns(true);

        var mockTitleProvider = Substitute.For<ITitleProvider>();

        var titleService = CreateAppTitleService(
            mockCurrentVersionProvider,
            mockTitleProvider);

        // Act
        titleService.SetLogName(null);

        // Assert
        mockTitleProvider.Received(1).SetTitle($"{Constants.AppName} (Development)");
    }

    [Fact]
    public void SetLogName_WhenLogName_ShouldSetTitle()
    {
        // Arrange
        var mockCurrentVersionProvider = Substitute.For<ICurrentVersionProvider>();
        mockCurrentVersionProvider.CurrentVersion.Returns(new Version(Constants.AppInstalledVersion));

        var mockTitleProvider = Substitute.For<ITitleProvider>();

        var titleService = CreateAppTitleService(
            mockCurrentVersionProvider,
            mockTitleProvider);

        // Act
        titleService.SetLogName(Constants.LogName);

        // Assert
        mockTitleProvider.Received(1)
            .SetTitle($"{Constants.AppName} {Constants.AppInstalledVersion} - {Constants.LogName}");
    }

    [Fact]
    public void SetLogName_WhenNullLogName_ShouldSetTitle()
    {
        // Arrange
        var mockCurrentVersionProvider = Substitute.For<ICurrentVersionProvider>();
        mockCurrentVersionProvider.CurrentVersion.Returns(new Version(Constants.AppInstalledVersion));

        var mockTitleProvider = Substitute.For<ITitleProvider>();

        var titleService = CreateAppTitleService(
            mockCurrentVersionProvider,
            mockTitleProvider);

        // Act
        titleService.SetLogName(null);

        // Assert
        mockTitleProvider.Received(1).SetTitle($"{Constants.AppName} {Constants.AppInstalledVersion}");
    }

    [Fact]
    public void SetProgressString_WhenAdminAndProgress_ShouldSetTitle()
    {
        // Arrange
        var mockCurrentVersionProvider = Substitute.For<ICurrentVersionProvider>();
        mockCurrentVersionProvider.CurrentVersion.Returns(new Version(Constants.AppInstalledVersion));
        mockCurrentVersionProvider.IsAdmin.Returns(true);

        var mockTitleProvider = Substitute.For<ITitleProvider>();

        var titleService = CreateAppTitleService(
            mockCurrentVersionProvider,
            mockTitleProvider);

        // Act
        titleService.SetProgressString(Constants.Percentage);

        // Assert
        mockTitleProvider.Received(1)
            .SetTitle($"{Constants.Percentage} - {Constants.AppName} {Constants.AppInstalledVersion} (Admin)");
    }

    [Fact]
    public void SetProgressString_WhenClearedAfterSet_ShouldSetTitleWithoutProgress()
    {
        // Arrange
        var mockCurrentVersionProvider = Substitute.For<ICurrentVersionProvider>();
        mockCurrentVersionProvider.CurrentVersion.Returns(new Version(Constants.AppInstalledVersion));

        var mockTitleProvider = Substitute.For<ITitleProvider>();

        var titleService = CreateAppTitleService(
            mockCurrentVersionProvider,
            mockTitleProvider);

        // Act
        titleService.SetProgressString(Constants.Percentage);
        titleService.SetProgressString(null);

        // Assert
        mockTitleProvider.Received(1)
            .SetTitle($"{Constants.AppName} {Constants.AppInstalledVersion}");
    }

    [Fact]
    public void SetProgressString_WhenNullProgress_ShouldSetTitle()
    {
        // Arrange
        var mockCurrentVersionProvider = Substitute.For<ICurrentVersionProvider>();
        mockCurrentVersionProvider.CurrentVersion.Returns(new Version(Constants.AppInstalledVersion));

        var mockTitleProvider = Substitute.For<ITitleProvider>();

        var titleService = CreateAppTitleService(
            mockCurrentVersionProvider,
            mockTitleProvider);

        // Act
        titleService.SetProgressString(null);

        // Assert
        mockTitleProvider.Received(1).SetTitle($"{Constants.AppName} {Constants.AppInstalledVersion}");
    }

    [Fact]
    public void SetProgressString_WhenProgress_ShouldSetTitle()
    {
        // Arrange
        var mockCurrentVersionProvider = Substitute.For<ICurrentVersionProvider>();
        mockCurrentVersionProvider.CurrentVersion.Returns(new Version(Constants.AppInstalledVersion));

        var mockTitleProvider = Substitute.For<ITitleProvider>();

        var titleService = CreateAppTitleService(
            mockCurrentVersionProvider,
            mockTitleProvider);

        // Act
        titleService.SetProgressString(Constants.Percentage);

        // Assert
        mockTitleProvider.Received(1)
            .SetTitle($"{Constants.Percentage} - {Constants.AppName} {Constants.AppInstalledVersion}");
    }

    [Fact]
    public void SetProgressString_WhenProgressAndLogName_ShouldSetTitleWithBoth()
    {
        // Arrange
        var mockCurrentVersionProvider = Substitute.For<ICurrentVersionProvider>();
        mockCurrentVersionProvider.CurrentVersion.Returns(new Version(Constants.AppInstalledVersion));

        var mockTitleProvider = Substitute.For<ITitleProvider>();

        var titleService = CreateAppTitleService(
            mockCurrentVersionProvider,
            mockTitleProvider);

        // Act
        titleService.SetLogName(Constants.LogName);
        titleService.SetProgressString(Constants.Percentage);

        // Assert
        mockTitleProvider.Received(1)
            .SetTitle(
                $"{Constants.Percentage} - {Constants.AppName} {Constants.AppInstalledVersion} - {Constants.LogName}");
    }

    private static AppTitleService CreateAppTitleService(
        ICurrentVersionProvider? currentVersionProvider = null,
        ITitleProvider? titleProvider = null)
    {
        return new AppTitleService(
            currentVersionProvider ?? Substitute.For<ICurrentVersionProvider>(),
            titleProvider ?? Substitute.For<ITitleProvider>());
    }
}
