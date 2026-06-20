// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Logging.Abstractions;
using EventLogExpert.Logging.Abstractions.Handlers;
using EventLogExpert.Runtime.Alerts;
using EventLogExpert.Runtime.Common.AppTitle;
using EventLogExpert.Runtime.Common.Versioning;
using EventLogExpert.Runtime.Settings;
using EventLogExpert.Runtime.Tests.TestUtils;
using EventLogExpert.Runtime.Tests.TestUtils.Constants;
using EventLogExpert.Runtime.Update;
using EventLogExpert.Runtime.Update.Deployment;
using NSubstitute;

namespace EventLogExpert.Runtime.Tests.Update;

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
        mockGitHubService.GetReleases().Returns(Task.FromResult(GitHubUtils.CreateGitHubReleases()));

        var updateService = CreateUpdateService(
            mockCurrentVersionProvider,
            mockAppTitleService,
            mockGitHubService);

        // Act
        await updateService.CheckForUpdates(usePreRelease: false);

        // Assert
        mockAppTitleService.Received(1).SetProgressString(null);
    }

    [Fact]
    public async Task CheckForUpdates_AutoScanCalledTwice_ShouldOnlyFetchReleasesOnce()
    {
        // Arrange
        var mockCurrentVersionProvider = Substitute.For<ICurrentVersionProvider>();
        mockCurrentVersionProvider.CurrentVersion.Returns(new Version(Constants.AppInstalledVersion));
        mockCurrentVersionProvider.IsDevBuild.Returns(false);

        var mockGitHubService = Substitute.For<IGitHubService>();
        mockGitHubService.GetReleases().Returns(Task.FromResult(GitHubUtils.CreateGitHubReleases()));

        var updateService = CreateUpdateService(
            mockCurrentVersionProvider,
            gitHubService: mockGitHubService);

        // Act
        await updateService.CheckForUpdates(usePreRelease: false, userInitiated: false);
        await updateService.CheckForUpdates(usePreRelease: false, userInitiated: false);

        // Assert
        await mockGitHubService.Received(1).GetReleases();
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

        mockDeploymentService.When(x => x.RestartNowAndUpdate(Arg.Any<string>(), Arg.Any<bool>()))
            .Do(_ => throw new InvalidOperationException("Deployment failed"));

        var mockGitHubService = Substitute.For<IGitHubService>();
        mockGitHubService.GetReleases().Returns(Task.FromResult(GitHubUtils.CreateGitHubReleases()));

        var updateService = CreateUpdateService(
            mockCurrentVersionProvider,
            gitHubService: mockGitHubService,
            deploymentService: mockDeploymentService,
            alertDialogService: mockAlertDialogService);

        // Act
        await updateService.CheckForUpdates(usePreRelease: false, userInitiated: true);

        // Assert
        await mockAlertDialogService.Received(1)
            .ShowAlert("Update Failure", Arg.Is<string>(s => s.Contains("Deployment failed")), "OK");
    }

    [Fact]
    public async Task CheckForUpdates_DeploymentThrowsExceptionAutoScan_ShouldNotShowAlert()
    {
        // Arrange
        var mockCurrentVersionProvider = Substitute.For<ICurrentVersionProvider>();
        mockCurrentVersionProvider.CurrentVersion.Returns(new Version(Constants.AppInstalledVersion));
        mockCurrentVersionProvider.IsDevBuild.Returns(false);

        var mockAlertDialogService = Substitute.For<IAlertDialogService>();

        // User declines the "Update Available" prompt -> UpdateOnNextRestart is called
        mockAlertDialogService
            .ShowAlert(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>())
            .Returns(Task.FromResult(false));

        var mockDeploymentService = Substitute.For<IDeploymentService>();

        mockDeploymentService.When(x => x.UpdateOnNextRestart(Arg.Any<string>(), Arg.Any<bool>()))
            .Do(_ => throw new InvalidOperationException("Deployment failed"));

        var mockGitHubService = Substitute.For<IGitHubService>();
        mockGitHubService.GetReleases().Returns(Task.FromResult(GitHubUtils.CreateGitHubReleases()));

        var updateService = CreateUpdateService(
            mockCurrentVersionProvider,
            gitHubService: mockGitHubService,
            deploymentService: mockDeploymentService,
            alertDialogService: mockAlertDialogService);

        // Act
        await updateService.CheckForUpdates(usePreRelease: false);

        // Assert
        await mockAlertDialogService.DidNotReceive()
            .ShowAlert("Update Failure", Arg.Any<string>(), "OK");
    }

    [Fact]
    public async Task CheckForUpdates_DevBuildAutoScan_ShouldSkipSilently()
    {
        // Arrange
        var mockCurrentVersionProvider = Substitute.For<ICurrentVersionProvider>();
        mockCurrentVersionProvider.IsDevBuild.Returns(true);

        var mockGitHubService = Substitute.For<IGitHubService>();
        var mockDeploymentService = Substitute.For<IDeploymentService>();
        var mockAlertDialogService = Substitute.For<IAlertDialogService>();

        var updateService = CreateUpdateService(
            mockCurrentVersionProvider,
            gitHubService: mockGitHubService,
            deploymentService: mockDeploymentService,
            alertDialogService: mockAlertDialogService);

        // Act
        await updateService.CheckForUpdates(usePreRelease: false);

        // Assert
        await mockGitHubService.DidNotReceive().GetReleases();
        mockDeploymentService.DidNotReceive().RestartNowAndUpdate(Arg.Any<string>(), Arg.Any<bool>());
        mockDeploymentService.DidNotReceive().UpdateOnNextRestart(Arg.Any<string>(), Arg.Any<bool>());
        await mockAlertDialogService.DidNotReceive().ShowAlert(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>());
    }

    [Fact]
    public async Task CheckForUpdates_DevBuildUserInitiated_ShouldShowAlert()
    {
        // Arrange
        var mockCurrentVersionProvider = Substitute.For<ICurrentVersionProvider>();
        mockCurrentVersionProvider.IsDevBuild.Returns(true);

        var mockGitHubService = Substitute.For<IGitHubService>();
        var mockAlertDialogService = Substitute.For<IAlertDialogService>();

        var updateService = CreateUpdateService(
            mockCurrentVersionProvider,
            gitHubService: mockGitHubService,
            alertDialogService: mockAlertDialogService);

        // Act
        await updateService.CheckForUpdates(usePreRelease: false, userInitiated: true);

        // Assert
        await mockGitHubService.DidNotReceive().GetReleases();
        await mockAlertDialogService.Received(1).ShowAlert(
            "Update Check Unavailable",
            "Update checks are disabled for development builds.",
            "OK");
    }

    [Fact]
    public async Task CheckForUpdates_GetReleasesThrowsException_ShouldShowAlert()
    {
        // Arrange
        var mockCurrentVersionProvider = Substitute.For<ICurrentVersionProvider>();
        mockCurrentVersionProvider.CurrentVersion.Returns(new Version(Constants.AppInstalledVersion));
        mockCurrentVersionProvider.IsDevBuild.Returns(false);

        var mockGitHubService = Substitute.For<IGitHubService>();

        mockGitHubService.GetReleases().Returns<IEnumerable<GitHubRelease>>(_ =>
            throw new HttpRequestException("Network error"));

        var mockAlertDialogService = Substitute.For<IAlertDialogService>();
        var mockDeploymentService = Substitute.For<IDeploymentService>();

        var updateService = CreateUpdateService(
            mockCurrentVersionProvider,
            gitHubService: mockGitHubService,
            deploymentService: mockDeploymentService,
            alertDialogService: mockAlertDialogService);

        // Act
        await updateService.CheckForUpdates(usePreRelease: false, userInitiated: true);

        // Assert
        await mockAlertDialogService.Received(1)
            .ShowAlert("Update Failure", Arg.Is<string>(s => s.Contains("Network error")), "OK");

        mockDeploymentService.DidNotReceive().RestartNowAndUpdate(Arg.Any<string>(), Arg.Any<bool>());
        mockDeploymentService.DidNotReceive().UpdateOnNextRestart(Arg.Any<string>(), Arg.Any<bool>());
    }

    [Fact]
    public async Task CheckForUpdates_GetReleasesThrowsExceptionAutoScan_ShouldNotShowAlert()
    {
        // Arrange
        var mockCurrentVersionProvider = Substitute.For<ICurrentVersionProvider>();
        mockCurrentVersionProvider.CurrentVersion.Returns(new Version(Constants.AppInstalledVersion));
        mockCurrentVersionProvider.IsDevBuild.Returns(false);

        var mockGitHubService = Substitute.For<IGitHubService>();

        mockGitHubService.GetReleases().Returns<IEnumerable<GitHubRelease>>(_ =>
            throw new HttpRequestException("Network error"));

        var mockAlertDialogService = Substitute.For<IAlertDialogService>();
        var mockDeploymentService = Substitute.For<IDeploymentService>();

        var updateService = CreateUpdateService(
            mockCurrentVersionProvider,
            gitHubService: mockGitHubService,
            deploymentService: mockDeploymentService,
            alertDialogService: mockAlertDialogService);

        // Act
        await updateService.CheckForUpdates(usePreRelease: false);

        // Assert
        await mockAlertDialogService.DidNotReceive()
            .ShowAlert(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>());

        mockDeploymentService.DidNotReceive().RestartNowAndUpdate(Arg.Any<string>(), Arg.Any<bool>());
        mockDeploymentService.DidNotReceive().UpdateOnNextRestart(Arg.Any<string>(), Arg.Any<bool>());
    }

    [Fact]
    public async Task CheckForUpdates_GetReleasesThrowsThenAutoScan_ShouldNotRetry()
    {
        // Arrange
        var mockCurrentVersionProvider = Substitute.For<ICurrentVersionProvider>();
        mockCurrentVersionProvider.CurrentVersion.Returns(new Version(Constants.AppInstalledVersion));
        mockCurrentVersionProvider.IsDevBuild.Returns(false);

        var mockGitHubService = Substitute.For<IGitHubService>();

        mockGitHubService.GetReleases().Returns<IEnumerable<GitHubRelease>>(_ =>
            throw new HttpRequestException("Network error"));

        var updateService = CreateUpdateService(
            mockCurrentVersionProvider,
            gitHubService: mockGitHubService);

        // Act
        await updateService.CheckForUpdates(usePreRelease: false);
        await updateService.CheckForUpdates(usePreRelease: false);

        // Assert
        await mockGitHubService.Received(1).GetReleases();
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
        mockGitHubService.GetReleases().Returns(Task.FromResult(GitHubUtils.CreateGitHubReleases()));

        var mockDeploymentService = Substitute.For<IDeploymentService>();

        var updateService = CreateUpdateService(
            mockCurrentVersionProvider,
            gitHubService: mockGitHubService,
            deploymentService: mockDeploymentService,
            alertDialogService: mockAlertDialogService);

        // Act
        await updateService.CheckForUpdates(usePreRelease: false);

        // Assert
        mockDeploymentService.Received(1).RestartNowAndUpdate(Constants.GitHubLatestUri, userInitiated: true);
    }

    [Fact]
    public async Task CheckForUpdates_Latest_ShouldUpdateOnNextRestart()
    {
        // Arrange
        var mockCurrentVersionProvider = Substitute.For<ICurrentVersionProvider>();
        mockCurrentVersionProvider.CurrentVersion.Returns(new Version(Constants.AppInstalledVersion));
        mockCurrentVersionProvider.IsDevBuild.Returns(false);

        var mockGitHubService = Substitute.For<IGitHubService>();
        mockGitHubService.GetReleases().Returns(Task.FromResult(GitHubUtils.CreateGitHubReleases()));

        var mockDeploymentService = Substitute.For<IDeploymentService>();

        var updateService = CreateUpdateService(
            mockCurrentVersionProvider,
            gitHubService: mockGitHubService,
            deploymentService: mockDeploymentService);

        // Act
        await updateService.CheckForUpdates(usePreRelease: false);

        // Assert
        mockDeploymentService.Received(1).UpdateOnNextRestart(Constants.GitHubLatestUri);
    }

    [Fact]
    public async Task CheckForUpdates_ManualThenAutoScan_ShouldRunBoth()
    {
        // Arrange
        var mockCurrentVersionProvider = Substitute.For<ICurrentVersionProvider>();
        mockCurrentVersionProvider.CurrentVersion.Returns(new Version(Constants.AppInstalledVersion));
        mockCurrentVersionProvider.IsDevBuild.Returns(false);

        var mockGitHubService = Substitute.For<IGitHubService>();
        mockGitHubService.GetReleases().Returns(Task.FromResult(GitHubUtils.CreateGitHubReleases()));

        var updateService = CreateUpdateService(
            mockCurrentVersionProvider,
            gitHubService: mockGitHubService);

        // Act
        await updateService.CheckForUpdates(usePreRelease: false, userInitiated: true);
        await updateService.CheckForUpdates(usePreRelease: false);

        // Assert
        await mockGitHubService.Received(2).GetReleases();
    }

    [Fact]
    public async Task CheckForUpdates_NoCompatiblePackageAutoScan_ShouldStaySilentAndNotDeploy()
    {
        // Arrange
        var mockCurrentVersionProvider = Substitute.For<ICurrentVersionProvider>();
        mockCurrentVersionProvider.CurrentVersion.Returns(new Version(Constants.AppInstalledVersion));
        mockCurrentVersionProvider.IsDevBuild.Returns(false);

        var mockGitHubService = Substitute.For<IGitHubService>();
        mockGitHubService.GetReleases().Returns(Task.FromResult(CreateRuntimeOnlyRelease()));

        var mockDeploymentService = Substitute.For<IDeploymentService>();
        var mockAlertDialogService = Substitute.For<IAlertDialogService>();

        var updateService = CreateUpdateService(
            mockCurrentVersionProvider,
            gitHubService: mockGitHubService,
            deploymentService: mockDeploymentService,
            alertDialogService: mockAlertDialogService);

        // Act
        await updateService.CheckForUpdates(usePreRelease: false, userInitiated: false);

        // Assert
        await mockAlertDialogService.DidNotReceive().ShowAlert(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>());
        mockDeploymentService.DidNotReceive().RestartNowAndUpdate(Arg.Any<string>(), Arg.Any<bool>());
        mockDeploymentService.DidNotReceive().UpdateOnNextRestart(Arg.Any<string>(), Arg.Any<bool>());
    }

    [Fact]
    public async Task CheckForUpdates_NoCompatiblePackageUserInitiated_ShouldAlertAndNotDeploy()
    {
        // Arrange
        var mockCurrentVersionProvider = Substitute.For<ICurrentVersionProvider>();
        mockCurrentVersionProvider.CurrentVersion.Returns(new Version(Constants.AppInstalledVersion));
        mockCurrentVersionProvider.IsDevBuild.Returns(false);

        var mockGitHubService = Substitute.For<IGitHubService>();
        mockGitHubService.GetReleases().Returns(Task.FromResult(CreateRuntimeOnlyRelease()));

        var mockDeploymentService = Substitute.For<IDeploymentService>();
        var mockAlertDialogService = Substitute.For<IAlertDialogService>();

        var updateService = CreateUpdateService(
            mockCurrentVersionProvider,
            gitHubService: mockGitHubService,
            deploymentService: mockDeploymentService,
            alertDialogService: mockAlertDialogService);

        // Act
        await updateService.CheckForUpdates(usePreRelease: false, userInitiated: true);

        // Assert
        await mockAlertDialogService.Received(1).ShowAlert("Update Unavailable", Arg.Any<string>(), "OK");
        mockDeploymentService.DidNotReceive().RestartNowAndUpdate(Arg.Any<string>(), Arg.Any<bool>());
        mockDeploymentService.DidNotReceive().UpdateOnNextRestart(Arg.Any<string>(), Arg.Any<bool>());
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
        await updateService.CheckForUpdates(usePreRelease: false, userInitiated: true);

        // Assert
        mockDeploymentService.DidNotReceive().RestartNowAndUpdate(Arg.Any<string>(), Arg.Any<bool>());
        mockDeploymentService.DidNotReceive().UpdateOnNextRestart(Arg.Any<string>(), Arg.Any<bool>());

        await mockAlertDialogService.Received(1)
            .ShowAlert("Update Failure", Arg.Is<string>(s => s.Contains("No releases available")), "OK");
    }

    [Fact]
    public async Task CheckForUpdates_NoReleasesAutoScan_ShouldNotShowAlert()
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
        await updateService.CheckForUpdates(usePreRelease: false);

        // Assert
        mockDeploymentService.DidNotReceive().RestartNowAndUpdate(Arg.Any<string>(), Arg.Any<bool>());
        mockDeploymentService.DidNotReceive().UpdateOnNextRestart(Arg.Any<string>(), Arg.Any<bool>());

        await mockAlertDialogService.DidNotReceive()
            .ShowAlert(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>());
    }

    [Fact]
    public async Task CheckForUpdates_NoUpdatesAvailable_ShouldAlert()
    {
        // Arrange
        var mockCurrentVersionProvider = Substitute.For<ICurrentVersionProvider>();
        mockCurrentVersionProvider.CurrentVersion.Returns(new Version(Constants.GitHubLatestVersion[1..]));
        mockCurrentVersionProvider.IsDevBuild.Returns(false);

        var mockGitHubService = Substitute.For<IGitHubService>();
        mockGitHubService.GetReleases().Returns(Task.FromResult(GitHubUtils.CreateGitHubReleases()));

        var mockAlertDialogService = Substitute.For<IAlertDialogService>();
        var mockDeploymentService = Substitute.For<IDeploymentService>();

        var updateService = CreateUpdateService(
            mockCurrentVersionProvider,
            gitHubService: mockGitHubService,
            deploymentService: mockDeploymentService,
            alertDialogService: mockAlertDialogService);

        // Act
        await updateService.CheckForUpdates(usePreRelease: false, userInitiated: true);

        // Assert
        mockDeploymentService.DidNotReceive().RestartNowAndUpdate(Arg.Any<string>(), Arg.Any<bool>());
        mockDeploymentService.DidNotReceive().UpdateOnNextRestart(Arg.Any<string>(), Arg.Any<bool>());
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
        mockGitHubService.GetReleases().Returns(Task.FromResult(GitHubUtils.CreateGitHubReleases()));

        var mockDeploymentService = Substitute.For<IDeploymentService>();

        var updateService = CreateUpdateService(
            mockCurrentVersionProvider,
            mockAppTitleService,
            mockGitHubService,
            mockDeploymentService);

        // Act
        await updateService.CheckForUpdates(usePreRelease: true);

        // Assert
        mockAppTitleService.Received(1).SetIsPrerelease(true);
        mockDeploymentService.DidNotReceive().RestartNowAndUpdate(Arg.Any<string>(), Arg.Any<bool>());
        mockDeploymentService.DidNotReceive().UpdateOnNextRestart(Arg.Any<string>(), Arg.Any<bool>());
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
        mockGitHubService.GetReleases().Returns(Task.FromResult(GitHubUtils.CreateGitHubReleases()));

        var mockDeploymentService = Substitute.For<IDeploymentService>();

        var updateService = CreateUpdateService(
            mockCurrentVersionProvider,
            gitHubService: mockGitHubService,
            deploymentService: mockDeploymentService,
            alertDialogService: mockAlertDialogService);

        // Act
        await updateService.CheckForUpdates(usePreRelease: true);

        // Assert
        mockDeploymentService.Received(1).RestartNowAndUpdate(Constants.GitHubPrereleaseUri, userInitiated: true);
    }

    [Fact]
    public async Task CheckForUpdates_PreRelease_ShouldUpdateOnNextRestart()
    {
        // Arrange
        var mockCurrentVersionProvider = Substitute.For<ICurrentVersionProvider>();
        mockCurrentVersionProvider.CurrentVersion.Returns(new Version(Constants.AppInstalledVersion));
        mockCurrentVersionProvider.IsDevBuild.Returns(false);

        var mockGitHubService = Substitute.For<IGitHubService>();
        mockGitHubService.GetReleases().Returns(Task.FromResult(GitHubUtils.CreateGitHubReleases()));

        var mockDeploymentService = Substitute.For<IDeploymentService>();

        var updateService = CreateUpdateService(
            mockCurrentVersionProvider,
            gitHubService: mockGitHubService,
            deploymentService: mockDeploymentService);

        // Act
        await updateService.CheckForUpdates(usePreRelease: true);

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
        mockGitHubService.GetReleases().Returns(Task.FromResult(GitHubUtils.CreateGitHubReleases()));

        var mockDeploymentService = Substitute.For<IDeploymentService>();

        var updateService = CreateUpdateService(
            mockCurrentVersionProvider,
            gitHubService: mockGitHubService,
            deploymentService: mockDeploymentService,
            alertDialogService: mockAlertDialogService);

        // Act
        await updateService.CheckForUpdates(usePreRelease: false);

        // Assert
        mockDeploymentService.DidNotReceive().RestartNowAndUpdate(Arg.Any<string>(), Arg.Any<bool>());
        mockDeploymentService.Received(1).UpdateOnNextRestart(Constants.GitHubLatestUri);
    }

    [Fact]
    public async Task CheckForUpdates_UserDeclinesUpdateManualScan_ShouldPropagateUserInitiated()
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
        mockGitHubService.GetReleases().Returns(Task.FromResult(GitHubUtils.CreateGitHubReleases()));

        var mockDeploymentService = Substitute.For<IDeploymentService>();

        var updateService = CreateUpdateService(
            mockCurrentVersionProvider,
            gitHubService: mockGitHubService,
            deploymentService: mockDeploymentService,
            alertDialogService: mockAlertDialogService);

        // Act
        await updateService.CheckForUpdates(usePreRelease: false, userInitiated: true);

        // Assert
        mockDeploymentService.Received(1).UpdateOnNextRestart(Constants.GitHubLatestUri, userInitiated: true);
    }

    [Fact]
    public async Task CheckForUpdates_WhenMalformedReleaseVersion_ShouldSkipAndLogWarning()
    {
        // Arrange
        var mockCurrentVersionProvider = Substitute.For<ICurrentVersionProvider>();
        mockCurrentVersionProvider.CurrentVersion.Returns(new Version(Constants.AppInstalledVersion));
        mockCurrentVersionProvider.IsDevBuild.Returns(false);

        IEnumerable<GitHubRelease> releases =
        [
            new GitHubRelease
            {
                Version = "v23.1.alpha",
                IsPreRelease = false,
                ReleaseDate = DateTime.Now,
                Assets =
                [
                    new GitHubReleaseAsset { Name = "EventLogExpert_alpha_x64.msix", Uri = "https://example/alpha.msix" }
                ],
                RawChanges = "malformed"
            },
            new GitHubRelease
            {
                Version = Constants.GitHubLatestVersion,
                IsPreRelease = false,
                ReleaseDate = DateTime.Now.AddDays(-1),
                Assets =
                [
                    new GitHubReleaseAsset { Name = Constants.GitHubLatestName, Uri = Constants.GitHubLatestUri }
                ],
                RawChanges = Constants.GitHubReleaseNotes
            }
        ];

        var mockGitHubService = Substitute.For<IGitHubService>();
        mockGitHubService.GetReleases().Returns(Task.FromResult(releases));

        var mockTraceLogger = Substitute.For<ITraceLogger>();
        var mockDeploymentService = Substitute.For<IDeploymentService>();

        var updateService = CreateUpdateService(
            mockCurrentVersionProvider,
            gitHubService: mockGitHubService,
            deploymentService: mockDeploymentService,
            traceLogger: mockTraceLogger);

        // Act
        await updateService.CheckForUpdates(usePreRelease: false);

        // Assert
        mockTraceLogger.Received().Warning(
            Arg.Is<WarningLogHandler>(h => h.ToString().Contains("skipping release") && h.ToString().Contains("v23.1.alpha")));

        mockDeploymentService.Received(1).UpdateOnNextRestart(Constants.GitHubLatestUri);
    }

    [Fact]
    public async Task CheckForUpdates_WhenRunningPreReleaseAndChannelNeverEnabled_ShouldEnableChannelAndSuppressRollbackPrompt()
    {
        // Arrange
        var mockCurrentVersionProvider = Substitute.For<ICurrentVersionProvider>();
        mockCurrentVersionProvider.CurrentVersion.Returns(new Version(Constants.GitHubPrereleaseVersion[1..]));
        mockCurrentVersionProvider.IsDevBuild.Returns(false);

        var mockGitHubService = Substitute.For<IGitHubService>();
        mockGitHubService.GetReleases().Returns(Task.FromResult(GitHubUtils.CreateGitHubReleases()));

        var mockAppTitleService = Substitute.For<IAppTitleService>();
        var mockAlertDialogService = Substitute.For<IAlertDialogService>();
        var mockDeploymentService = Substitute.For<IDeploymentService>();

        var mockSettings = Substitute.For<ISettingsService>();
        mockSettings.IsPreReleaseEnabled.Returns(false);
        mockSettings.HasEverEnabledPreRelease.Returns(false);

        var updateService = CreateUpdateService(
            mockCurrentVersionProvider,
            appTitleService: mockAppTitleService,
            gitHubService: mockGitHubService,
            deploymentService: mockDeploymentService,
            alertDialogService: mockAlertDialogService,
            settings: mockSettings);

        // Act
        await updateService.CheckForUpdates(usePreRelease: false);

        // Assert
        mockAppTitleService.Received(1).SetIsPrerelease(true);
        mockSettings.Received(1).IsPreReleaseEnabled = true;

        await mockAlertDialogService.DidNotReceive()
            .ShowAlert("Update Available", Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>());

        mockDeploymentService.DidNotReceive().UpdateOnNextRestart(Arg.Any<string>(), Arg.Any<bool>());
        mockDeploymentService.DidNotReceive().RestartNowAndUpdate(Arg.Any<string>(), Arg.Any<bool>());
    }

    [Fact]
    public async Task CheckForUpdates_WhenRunningPreReleaseAndChannelPreviouslyEnabledThenDisabled_ShouldPromptRollback()
    {
        // Arrange
        var mockCurrentVersionProvider = Substitute.For<ICurrentVersionProvider>();
        mockCurrentVersionProvider.CurrentVersion.Returns(new Version(Constants.GitHubPrereleaseVersion[1..]));
        mockCurrentVersionProvider.IsDevBuild.Returns(false);

        var mockGitHubService = Substitute.For<IGitHubService>();
        mockGitHubService.GetReleases().Returns(Task.FromResult(GitHubUtils.CreateGitHubReleases()));

        var mockAppTitleService = Substitute.For<IAppTitleService>();
        var mockDeploymentService = Substitute.For<IDeploymentService>();

        var mockSettings = Substitute.For<ISettingsService>();
        mockSettings.IsPreReleaseEnabled.Returns(false);
        mockSettings.HasEverEnabledPreRelease.Returns(true);

        var updateService = CreateUpdateService(
            mockCurrentVersionProvider,
            appTitleService: mockAppTitleService,
            gitHubService: mockGitHubService,
            deploymentService: mockDeploymentService,
            settings: mockSettings);

        // Act
        await updateService.CheckForUpdates(usePreRelease: false);

        // Assert
        mockAppTitleService.Received(1).SetIsPrerelease(true);
        mockSettings.DidNotReceive().IsPreReleaseEnabled = Arg.Any<bool>();

        mockDeploymentService.Received(1).UpdateOnNextRestart(Constants.GitHubLatestUri);
    }

    [Fact]
    public async Task GetReleaseNotes_NoCurrentChanges_ShouldShowFailureAlertAndReturnNull()
    {
        // Arrange
        var mockAlertDialogService = Substitute.For<IAlertDialogService>();

        var updateService = CreateUpdateService(alertDialogService: mockAlertDialogService);

        // Act
        var result = await updateService.GetReleaseNotes();

        // Assert
        await mockAlertDialogService.Received(1)
            .ShowAlert(Arg.Is<string>(s => s.Contains("Release Notes")), Arg.Is<string>(s => s.Contains("Failed to get release notes")), "OK");
        Assert.Null(result);
    }

    [Fact]
    public async Task GetReleaseNotes_WhenRunningOlderThanLatest_ShouldReturnNotesForCurrentVersion()
    {
        // Arrange
        const string currentRawChanges = "## Notes for v23.1.1.1\r\n\r\n* abcdef1234567890abcdef1234567890abcdef12 Bug fix for current version";
        const string newerRawChanges = "## Notes for v23.1.1.2\r\n\r\n* 1234567890abcdef1234567890abcdef12345678 Feature A for newer version";

        var mockCurrentVersionProvider = Substitute.For<ICurrentVersionProvider>();
        mockCurrentVersionProvider.CurrentVersion.Returns(new Version(Constants.AppInstalledVersion));
        mockCurrentVersionProvider.IsDevBuild.Returns(false);

        IEnumerable<GitHubRelease> releases =
        [
            new GitHubRelease
            {
                Version = Constants.GitHubLatestVersion,
                IsPreRelease = false,
                ReleaseDate = DateTime.Now,
                Assets =
                [
                    new GitHubReleaseAsset { Name = Constants.GitHubLatestName, Uri = Constants.GitHubLatestUri }
                ],
                RawChanges = newerRawChanges
            },
            new GitHubRelease
            {
                Version = "v" + Constants.AppInstalledVersion,
                IsPreRelease = false,
                ReleaseDate = DateTime.Now.AddDays(-1),
                Assets =
                [
                    new GitHubReleaseAsset
                    {
                        Name = "EventLogExpert_23.1.1.1_x64.msix",
                        Uri = "https://github.com/microsoft/EventLogExpert/releases/download/v23.1.1.1/EventLogExpert_23.1.1.1_x64.msix"
                    }
                ],
                RawChanges = currentRawChanges
            }
        ];

        var mockGitHubService = Substitute.For<IGitHubService>();
        mockGitHubService.GetReleases().Returns(Task.FromResult(releases));

        var mockAlertDialogService = Substitute.For<IAlertDialogService>();
        mockAlertDialogService
            .ShowAlert(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>())
            .Returns(Task.FromResult(false));

        var updateService = CreateUpdateService(
            mockCurrentVersionProvider,
            gitHubService: mockGitHubService,
            alertDialogService: mockAlertDialogService);

        // Act
        await updateService.CheckForUpdates(usePreRelease: false);
        var result = await updateService.GetReleaseNotes();

        // Assert
        Assert.NotNull(result);
        Assert.Contains(Constants.AppInstalledVersion, result.Value.Title);
        Assert.Contains("Bug fix for current version", result.Value.Markdown);
        Assert.DoesNotContain("Feature A for newer version", result.Value.Markdown);

        await mockAlertDialogService.DidNotReceive()
            .ShowAlert("Release Notes Failure", Arg.Any<string>(), "OK");
    }

    [Fact]
    public async Task GetReleaseNotes_WithCurrentChanges_ShouldReturnContent()
    {
        // Arrange
        var mockCurrentVersionProvider = Substitute.For<ICurrentVersionProvider>();
        mockCurrentVersionProvider.CurrentVersion.Returns(new Version(Constants.GitHubLatestVersion[1..]));
        mockCurrentVersionProvider.IsDevBuild.Returns(false);

        var mockGitHubService = Substitute.For<IGitHubService>();
        mockGitHubService.GetReleases().Returns(Task.FromResult(GitHubUtils.CreateGitHubReleases()));

        var mockAlertDialogService = Substitute.For<IAlertDialogService>();

        var updateService = CreateUpdateService(
            mockCurrentVersionProvider,
            gitHubService: mockGitHubService,
            alertDialogService: mockAlertDialogService);

        // Act
        await updateService.CheckForUpdates(usePreRelease: false);
        var result = await updateService.GetReleaseNotes();

        // Assert
        Assert.NotNull(result);
        Assert.Contains("Release notes", result.Value.Title);
        Assert.Contains(Constants.GitHubLatestVersion[1..], result.Value.Title);
        Assert.Contains("Updated Azure yml to .NET 8", result.Value.Markdown);
        Assert.DoesNotContain("66b7d6883807a5c518ffcd59f92e07e528a5636a", result.Value.Markdown);

        await mockAlertDialogService.DidNotReceive()
            .ShowAlert(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>());
    }

    // A release newer than the installed version whose only asset is the WindowsAppRuntime dependency (no
    // EventLogExpert package or bundle), so the bundle-only selector finds no compatible package.
    private static IEnumerable<GitHubRelease> CreateRuntimeOnlyRelease() =>
    [
        new()
        {
            Version = "v23.1.1.2",
            IsPreRelease = false,
            ReleaseDate = DateTime.Now,
            Assets =
            [
                new GitHubReleaseAsset
                {
                    Name = "Microsoft.WindowsAppRuntime.1.7.msix",
                    Uri = "https://github.com/microsoft/EventLogExpert/releases/download/v23.1.1.2/Microsoft.WindowsAppRuntime.1.7.msix"
                }
            ],
            RawChanges = string.Empty
        }
    ];

    private static UpdateService CreateUpdateService(
        ICurrentVersionProvider? currentVersionProvider = null,
        IAppTitleService? appTitleService = null,
        IGitHubService? gitHubService = null,
        IDeploymentService? deploymentService = null,
        ITraceLogger? traceLogger = null,
        IAlertDialogService? alertDialogService = null,
        ISettingsService? settings = null)
    {
        if (settings is null)
        {
            settings = Substitute.For<ISettingsService>();
            settings.HasEverEnabledPreRelease.Returns(true);
        }

        return new UpdateService(
            currentVersionProvider ?? Substitute.For<ICurrentVersionProvider>(),
            appTitleService ?? Substitute.For<IAppTitleService>(),
            gitHubService ?? Substitute.For<IGitHubService>(),
            deploymentService ?? Substitute.For<IDeploymentService>(),
            traceLogger ?? Substitute.For<ITraceLogger>(),
            alertDialogService ?? Substitute.For<IAlertDialogService>(),
            settings);
    }
}
