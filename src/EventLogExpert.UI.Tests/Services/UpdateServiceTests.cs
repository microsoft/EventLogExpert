// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Eventing.Helpers;
using EventLogExpert.UI.Models;
using EventLogExpert.UI.Services;
using EventLogExpert.UI.Tests.TestUtils;
using EventLogExpert.UI.Tests.TestUtils.Constants;
using NSubstitute;

namespace EventLogExpert.UI.Tests.Services;

public sealed class UpdateServiceTests
{
    [Fact]
    public async Task CheckForUpdates_Always_ShouldClearProgressString()
    {
        // Arrange
        var mockCurrentVersionProvider = Substitute.For<ICurrentVersionProvider>();
        mockCurrentVersionProvider.CurrentVersion.Returns(new Version(Constants.AppInstalledVersion));
        mockCurrentVersionProvider.IsDevBuild.Returns(false);

        var mockAppTitleService = Substitute.For<IAppTitleService>();
        var mockGitHubService = Substitute.For<IGitHubService>();
        mockGitHubService.GetReleases().Returns(Task.FromResult(GitHubUtils.CreateGitReleaseModels()));

        var updateService = CreateUpdateService(
            mockCurrentVersionProvider,
            mockAppTitleService,
            mockGitHubService);

        // Act
        await updateService.CheckForUpdates(false, false);

        // Assert
        mockAppTitleService.Received(1).SetProgressString(null);
    }

    [Fact]
    public async Task CheckForUpdates_DeploymentThrowsException_ShouldShowAlert()
    {
        // Arrange
        var mockCurrentVersionProvider = Substitute.For<ICurrentVersionProvider>();
        mockCurrentVersionProvider.CurrentVersion.Returns(new Version(Constants.AppInstalledVersion));
        mockCurrentVersionProvider.IsDevBuild.Returns(false);

        var mockAlertDialogService = Substitute.For<IAlertDialogService>();

        mockAlertDialogService
            .ShowAlert(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>())
            .Returns(Task.FromResult(true));

        var mockDeploymentService = Substitute.For<IDeploymentService>();

        mockDeploymentService.When(x => x.RestartNowAndUpdate(Arg.Any<string>()))
            .Do(_ => throw new InvalidOperationException("Deployment failed"));

        var mockGitHubService = Substitute.For<IGitHubService>();
        mockGitHubService.GetReleases().Returns(Task.FromResult(GitHubUtils.CreateGitReleaseModels()));

        var updateService = CreateUpdateService(
            mockCurrentVersionProvider,
            gitHubService: mockGitHubService,
            deploymentService: mockDeploymentService,
            alertDialogService: mockAlertDialogService);

        // Act
        await updateService.CheckForUpdates(false, false);

        // Assert
        await mockAlertDialogService.Received(1)
            .ShowAlert("Update Failure", Arg.Is<string>(s => s.Contains("Deployment failed")), "Ok");
    }

    [Fact]
    public async Task CheckForUpdates_DevBuild_ShouldSkipUpdateCheck()
    {
        // Arrange
        var mockCurrentVersionProvider = Substitute.For<ICurrentVersionProvider>();
        mockCurrentVersionProvider.IsDevBuild.Returns(true);

        var mockGitHubService = Substitute.For<IGitHubService>();
        var mockDeploymentService = Substitute.For<IDeploymentService>();

        var updateService = CreateUpdateService(
            mockCurrentVersionProvider,
            gitHubService: mockGitHubService,
            deploymentService: mockDeploymentService);

        // Act
        await updateService.CheckForUpdates(false, false);

        // Assert
        await mockGitHubService.DidNotReceive().GetReleases();
        mockDeploymentService.DidNotReceive().RestartNowAndUpdate(Arg.Any<string>());
        mockDeploymentService.DidNotReceive().UpdateOnNextRestart(Arg.Any<string>());
    }

    [Fact]
    public async Task CheckForUpdates_GetReleasesThrowsException_ShouldShowAlert()
    {
        // Arrange
        var mockCurrentVersionProvider = Substitute.For<ICurrentVersionProvider>();
        mockCurrentVersionProvider.CurrentVersion.Returns(new Version(Constants.AppInstalledVersion));
        mockCurrentVersionProvider.IsDevBuild.Returns(false);

        var mockGitHubService = Substitute.For<IGitHubService>();

        mockGitHubService.GetReleases().Returns<IEnumerable<GitReleaseModel>>(_ =>
            throw new HttpRequestException("Network error"));

        var mockAlertDialogService = Substitute.For<IAlertDialogService>();
        var mockDeploymentService = Substitute.For<IDeploymentService>();

        var updateService = CreateUpdateService(
            mockCurrentVersionProvider,
            gitHubService: mockGitHubService,
            deploymentService: mockDeploymentService,
            alertDialogService: mockAlertDialogService);

        // Act
        await updateService.CheckForUpdates(false, false);

        // Assert
        await mockAlertDialogService.Received(1)
            .ShowAlert("Update Failure", Arg.Is<string>(s => s.Contains("Network error")), "Ok");

        mockDeploymentService.DidNotReceive().RestartNowAndUpdate(Arg.Any<string>());
        mockDeploymentService.DidNotReceive().UpdateOnNextRestart(Arg.Any<string>());
    }

    [Fact]
    public async Task CheckForUpdates_Latest_ShouldUpdateImmediately()
    {
        // Arrange
        var mockCurrentVersionProvider = Substitute.For<ICurrentVersionProvider>();
        mockCurrentVersionProvider.CurrentVersion.Returns(new Version(Constants.AppInstalledVersion));
        mockCurrentVersionProvider.IsDevBuild.Returns(false);

        var mockAlertDialogService = Substitute.For<IAlertDialogService>();

        mockAlertDialogService
            .ShowAlert(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>())
            .Returns(Task.FromResult(true));

        var mockGitHubService = Substitute.For<IGitHubService>();
        mockGitHubService.GetReleases().Returns(Task.FromResult(GitHubUtils.CreateGitReleaseModels()));

        var mockDeploymentService = Substitute.For<IDeploymentService>();

        var updateService = CreateUpdateService(
            mockCurrentVersionProvider,
            gitHubService: mockGitHubService,
            deploymentService: mockDeploymentService,
            alertDialogService: mockAlertDialogService);

        // Act
        await updateService.CheckForUpdates(false, false);

        // Assert
        mockDeploymentService.Received(1).RestartNowAndUpdate(Constants.GitHubLatestUri);
    }

    [Fact]
    public async Task CheckForUpdates_Latest_ShouldUpdateOnNextRestart()
    {
        // Arrange
        var mockCurrentVersionProvider = Substitute.For<ICurrentVersionProvider>();
        mockCurrentVersionProvider.CurrentVersion.Returns(new Version(Constants.AppInstalledVersion));
        mockCurrentVersionProvider.IsDevBuild.Returns(false);

        var mockGitHubService = Substitute.For<IGitHubService>();
        mockGitHubService.GetReleases().Returns(Task.FromResult(GitHubUtils.CreateGitReleaseModels()));

        var mockDeploymentService = Substitute.For<IDeploymentService>();

        var updateService = CreateUpdateService(
            mockCurrentVersionProvider,
            gitHubService: mockGitHubService,
            deploymentService: mockDeploymentService);

        // Act
        await updateService.CheckForUpdates(false, false);

        // Assert
        mockDeploymentService.Received(1).UpdateOnNextRestart(Constants.GitHubLatestUri);
    }

    [Fact]
    public async Task CheckForUpdates_NoReleases_ShouldShowAlert()
    {
        // Arrange
        var mockCurrentVersionProvider = Substitute.For<ICurrentVersionProvider>();
        mockCurrentVersionProvider.CurrentVersion.Returns(new Version(Constants.AppInstalledVersion));
        mockCurrentVersionProvider.IsDevBuild.Returns(false);

        var mockGitHubService = Substitute.For<IGitHubService>();
        mockGitHubService.GetReleases().Returns([]);

        var mockAlertDialogService = Substitute.For<IAlertDialogService>();
        var mockDeploymentService = Substitute.For<IDeploymentService>();

        var updateService = CreateUpdateService(
            mockCurrentVersionProvider,
            gitHubService: mockGitHubService,
            deploymentService: mockDeploymentService,
            alertDialogService: mockAlertDialogService);

        // Act
        await updateService.CheckForUpdates(false, false);

        // Assert
        mockDeploymentService.DidNotReceive().RestartNowAndUpdate(Arg.Any<string>());
        mockDeploymentService.DidNotReceive().UpdateOnNextRestart(Arg.Any<string>());

        await mockAlertDialogService.Received(1)
            .ShowAlert("Update Failure", Arg.Is<string>(s => s.Contains("No releases available")), "Ok");
    }

    [Fact]
    public async Task CheckForUpdates_NoUpdatesAvailable_ShouldAlert()
    {
        // Arrange
        var mockCurrentVersionProvider = Substitute.For<ICurrentVersionProvider>();
        mockCurrentVersionProvider.CurrentVersion.Returns(new Version(Constants.GitHubLatestVersion[1..]));
        mockCurrentVersionProvider.IsDevBuild.Returns(false);

        var mockGitHubService = Substitute.For<IGitHubService>();
        mockGitHubService.GetReleases().Returns(Task.FromResult(GitHubUtils.CreateGitReleaseModels()));

        var mockAlertDialogService = Substitute.For<IAlertDialogService>();
        var mockDeploymentService = Substitute.For<IDeploymentService>();

        var updateService = CreateUpdateService(
            mockCurrentVersionProvider,
            gitHubService: mockGitHubService,
            deploymentService: mockDeploymentService,
            alertDialogService: mockAlertDialogService);

        // Act
        await updateService.CheckForUpdates(false, true);

        // Assert
        mockDeploymentService.DidNotReceive().RestartNowAndUpdate(Arg.Any<string>());
        mockDeploymentService.DidNotReceive().UpdateOnNextRestart(Arg.Any<string>());
        await mockAlertDialogService.Received(1).ShowAlert(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>());
    }

    [Fact]
    public async Task CheckForUpdates_OnCurrentPrerelease_ShouldSetPrereleaseFlag()
    {
        // Arrange
        var mockCurrentVersionProvider = Substitute.For<ICurrentVersionProvider>();
        mockCurrentVersionProvider.CurrentVersion.Returns(new Version(Constants.GitHubPrereleaseVersion[1..]));
        mockCurrentVersionProvider.IsDevBuild.Returns(false);

        var mockAppTitleService = Substitute.For<IAppTitleService>();

        var mockGitHubService = Substitute.For<IGitHubService>();
        mockGitHubService.GetReleases().Returns(Task.FromResult(GitHubUtils.CreateGitReleaseModels()));

        var mockDeploymentService = Substitute.For<IDeploymentService>();

        var updateService = CreateUpdateService(
            mockCurrentVersionProvider,
            mockAppTitleService,
            mockGitHubService,
            mockDeploymentService);

        // Act
        await updateService.CheckForUpdates(true, false);

        // Assert
        mockAppTitleService.Received(1).SetIsPrerelease(true);
        mockDeploymentService.DidNotReceive().RestartNowAndUpdate(Arg.Any<string>());
        mockDeploymentService.DidNotReceive().UpdateOnNextRestart(Arg.Any<string>());
    }

    [Fact]
    public async Task CheckForUpdates_Prerelease_ShouldUpdateImmediately()
    {
        // Arrange
        var mockCurrentVersionProvider = Substitute.For<ICurrentVersionProvider>();
        mockCurrentVersionProvider.CurrentVersion.Returns(new Version(Constants.AppInstalledVersion));
        mockCurrentVersionProvider.IsDevBuild.Returns(false);

        var mockAlertDialogService = Substitute.For<IAlertDialogService>();

        mockAlertDialogService
            .ShowAlert(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>())
            .Returns(Task.FromResult(true));

        var mockGitHubService = Substitute.For<IGitHubService>();
        mockGitHubService.GetReleases().Returns(Task.FromResult(GitHubUtils.CreateGitReleaseModels()));

        var mockDeploymentService = Substitute.For<IDeploymentService>();

        var updateService = CreateUpdateService(
            mockCurrentVersionProvider,
            gitHubService: mockGitHubService,
            deploymentService: mockDeploymentService,
            alertDialogService: mockAlertDialogService);

        // Act
        await updateService.CheckForUpdates(true, false);

        // Assert
        mockDeploymentService.Received(1).RestartNowAndUpdate(Constants.GitHubPrereleaseUri);
    }

    [Fact]
    public async Task CheckForUpdates_PreRelease_ShouldUpdateOnNextRestart()
    {
        // Arrange
        var mockCurrentVersionProvider = Substitute.For<ICurrentVersionProvider>();
        mockCurrentVersionProvider.CurrentVersion.Returns(new Version(Constants.AppInstalledVersion));
        mockCurrentVersionProvider.IsDevBuild.Returns(false);

        var mockGitHubService = Substitute.For<IGitHubService>();
        mockGitHubService.GetReleases().Returns(Task.FromResult(GitHubUtils.CreateGitReleaseModels()));

        var mockDeploymentService = Substitute.For<IDeploymentService>();

        var updateService = CreateUpdateService(
            mockCurrentVersionProvider,
            gitHubService: mockGitHubService,
            deploymentService: mockDeploymentService);

        // Act
        await updateService.CheckForUpdates(true, false);

        // Assert
        mockDeploymentService.Received(1).UpdateOnNextRestart(Constants.GitHubPrereleaseUri);
    }

    [Fact]
    public async Task CheckForUpdates_UserDeclinesUpdate_ShouldUpdateOnNextRestart()
    {
        // Arrange
        var mockCurrentVersionProvider = Substitute.For<ICurrentVersionProvider>();
        mockCurrentVersionProvider.CurrentVersion.Returns(new Version(Constants.AppInstalledVersion));
        mockCurrentVersionProvider.IsDevBuild.Returns(false);

        var mockAlertDialogService = Substitute.For<IAlertDialogService>();

        mockAlertDialogService
            .ShowAlert(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>())
            .Returns(Task.FromResult(false)); // User clicks "No"

        var mockGitHubService = Substitute.For<IGitHubService>();
        mockGitHubService.GetReleases().Returns(Task.FromResult(GitHubUtils.CreateGitReleaseModels()));

        var mockDeploymentService = Substitute.For<IDeploymentService>();

        var updateService = CreateUpdateService(
            mockCurrentVersionProvider,
            gitHubService: mockGitHubService,
            deploymentService: mockDeploymentService,
            alertDialogService: mockAlertDialogService);

        // Act
        await updateService.CheckForUpdates(false, false);

        // Assert
        mockDeploymentService.DidNotReceive().RestartNowAndUpdate(Arg.Any<string>());
        mockDeploymentService.Received(1).UpdateOnNextRestart(Constants.GitHubLatestUri);
    }

    [Fact]
    public async Task GetReleaseNotes_NoCurrentChanges_ShouldShowFailureAlert()
    {
        // Arrange
        var mockAlertDialogService = Substitute.For<IAlertDialogService>();

        var updateService = CreateUpdateService(alertDialogService: mockAlertDialogService);

        // Act
        await updateService.GetReleaseNotes();

        // Assert
        await mockAlertDialogService.Received(1)
            .ShowAlert(Arg.Is<string>(s => s.Contains("Release Notes")), Arg.Is<string>(s => s.Contains("Failed to get release notes")), "Ok");
    }

    [Fact]
    public async Task GetReleaseNotes_WithCurrentChanges_ShouldShowAlert()
    {
        // Arrange
        var mockCurrentVersionProvider = Substitute.For<ICurrentVersionProvider>();
        mockCurrentVersionProvider.CurrentVersion.Returns(new Version(Constants.GitHubLatestVersion[1..]));
        mockCurrentVersionProvider.IsDevBuild.Returns(false);

        var mockGitHubService = Substitute.For<IGitHubService>();
        mockGitHubService.GetReleases().Returns(Task.FromResult(GitHubUtils.CreateGitReleaseModels()));

        var mockAlertDialogService = Substitute.For<IAlertDialogService>();

        var updateService = CreateUpdateService(
            mockCurrentVersionProvider,
            gitHubService: mockGitHubService,
            alertDialogService: mockAlertDialogService);

        // Act
        await updateService.CheckForUpdates(false, false);
        await updateService.GetReleaseNotes();

        // Assert
        await mockAlertDialogService.Received(1)
            .ShowAlert(Arg.Is<string>(s => s.Contains("Release notes")), Arg.Any<string>(), "Ok");
    }

    private static UpdateService CreateUpdateService(
        ICurrentVersionProvider? currentVersionProvider = null,
        IAppTitleService? appTitleService = null,
        IGitHubService? gitHubService = null,
        IDeploymentService? deploymentService = null,
        ITraceLogger? traceLogger = null,
        IAlertDialogService? alertDialogService = null)
    {
        return new UpdateService(
            currentVersionProvider ?? Substitute.For<ICurrentVersionProvider>(),
            appTitleService ?? Substitute.For<IAppTitleService>(),
            gitHubService ?? Substitute.For<IGitHubService>(),
            deploymentService ?? Substitute.For<IDeploymentService>(),
            traceLogger ?? Substitute.For<ITraceLogger>(),
            alertDialogService ?? Substitute.For<IAlertDialogService>());
    }
}
