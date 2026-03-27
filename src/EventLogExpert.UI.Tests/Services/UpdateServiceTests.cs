// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Eventing.Helpers;
using EventLogExpert.UI.Interfaces;
using EventLogExpert.UI.Models;
using EventLogExpert.UI.Services;
using EventLogExpert.UI.Tests.TestUtils;
using EventLogExpert.UI.Tests.TestUtils.Constants;
using NSubstitute;

namespace EventLogExpert.UI.Tests.Services;

public sealed class UpdateServiceTests
{
    private readonly IAppTitleService _appTitleService;
    private readonly IAlertDialogService _mockAlertDialogService = Substitute.For<IAlertDialogService>();
    private readonly ICurrentVersionProvider _mockCurrentVersionProvider = Substitute.For<ICurrentVersionProvider>();
    private readonly IDeploymentService _mockDeploymentService = Substitute.For<IDeploymentService>();
    private readonly IGitHubService _mockGitHubService = Substitute.For<IGitHubService>();
    private readonly ITitleProvider _mockTitleProvider = Substitute.For<ITitleProvider>();
    private readonly ITraceLogger _mockTraceLogger = Substitute.For<ITraceLogger>();

    public UpdateServiceTests()
    {
        _mockGitHubService.GetReleases().Returns(Task.FromResult(GitHubUtils.CreateGitReleaseModels()));

        _appTitleService = new AppTitleService(_mockCurrentVersionProvider, _mockTitleProvider);
    }

    [Fact]
    public async Task CheckForUpdates_DeploymentThrowsException_ShouldShowAlert()
    {
        _mockCurrentVersionProvider.CurrentVersion.Returns(new Version(Constants.AppInstalledVersion));
        _mockCurrentVersionProvider.IsDevBuild.Returns(false);

        _mockAlertDialogService
            .ShowAlert(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>())
            .Returns(Task.FromResult(true));

        _mockDeploymentService.When(x => x.RestartNowAndUpdate(Arg.Any<string>()))
            .Do(_ => throw new InvalidOperationException("Deployment failed"));

        var updateService = new UpdateService(
            _mockCurrentVersionProvider,
            _appTitleService,
            _mockGitHubService,
            _mockDeploymentService,
            _mockTraceLogger,
            _mockAlertDialogService);

        await updateService.CheckForUpdates(usePreRelease: false, manualScan: false);

        await _mockAlertDialogService.Received(1)
            .ShowAlert("Update Failure", Arg.Is<string>(s => s.Contains("Deployment failed")), "Ok");
    }

    [Fact]
    public async Task CheckForUpdates_DevBuild_ShouldSkipUpdateCheck()
    {
        _mockCurrentVersionProvider.IsDevBuild.Returns(true);

        var updateService = new UpdateService(
            _mockCurrentVersionProvider,
            _appTitleService,
            _mockGitHubService,
            _mockDeploymentService,
            _mockTraceLogger,
            _mockAlertDialogService);

        await updateService.CheckForUpdates(usePreRelease: false, manualScan: false);

        await _mockGitHubService.DidNotReceive().GetReleases();

        _mockDeploymentService.DidNotReceive().RestartNowAndUpdate(Arg.Any<string>());
        _mockDeploymentService.DidNotReceive().UpdateOnNextRestart(Arg.Any<string>());
    }

    [Fact]
    public async Task CheckForUpdates_GetReleasesThrowsException_ShouldShowAlert()
    {
        _mockCurrentVersionProvider.CurrentVersion.Returns(new Version(Constants.AppInstalledVersion));
        _mockCurrentVersionProvider.IsDevBuild.Returns(false);
        _mockGitHubService.GetReleases().Returns<IEnumerable<GitReleaseModel>>(_ =>
            throw new HttpRequestException("Network error"));

        var updateService = new UpdateService(
            _mockCurrentVersionProvider,
            _appTitleService,
            _mockGitHubService,
            _mockDeploymentService,
            _mockTraceLogger,
            _mockAlertDialogService);

        await updateService.CheckForUpdates(usePreRelease: false, manualScan: false);

        await _mockAlertDialogService.Received(1)
            .ShowAlert("Update Failure", Arg.Is<string>(s => s.Contains("Network error")), "Ok");

        _mockDeploymentService.DidNotReceive().RestartNowAndUpdate(Arg.Any<string>());
        _mockDeploymentService.DidNotReceive().UpdateOnNextRestart(Arg.Any<string>());
    }

    [Fact]
    public async Task CheckForUpdates_Latest_ShouldUpdateImmediately()
    {
        _mockCurrentVersionProvider.CurrentVersion.Returns(new Version(Constants.AppInstalledVersion));
        _mockCurrentVersionProvider.IsDevBuild.Returns(false);

        _mockAlertDialogService
            .ShowAlert(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>())
            .Returns(Task.FromResult(true));

        var updateService = new UpdateService(
            _mockCurrentVersionProvider,
            _appTitleService,
            _mockGitHubService,
            _mockDeploymentService,
            _mockTraceLogger,
            _mockAlertDialogService);

        await updateService.CheckForUpdates(usePreRelease: false, manualScan: false);

        _mockDeploymentService.Received(1).RestartNowAndUpdate(Constants.GitHubLatestUri);
    }

    [Fact]
    public async Task CheckForUpdates_Latest_ShouldUpdateOnNextRestart()
    {
        _mockCurrentVersionProvider.CurrentVersion.Returns(new Version(Constants.AppInstalledVersion));
        _mockCurrentVersionProvider.IsDevBuild.Returns(false);

        var updateService = new UpdateService(
            _mockCurrentVersionProvider,
            _appTitleService,
            _mockGitHubService,
            _mockDeploymentService,
            _mockTraceLogger,
            _mockAlertDialogService);

        await updateService.CheckForUpdates(usePreRelease: false, manualScan: false);

        _mockDeploymentService.Received(1).UpdateOnNextRestart(Constants.GitHubLatestUri);
    }

    [Fact]
    public async Task CheckForUpdates_NoReleases_ShouldShowAlert()
    {
        _mockCurrentVersionProvider.CurrentVersion.Returns(new Version(Constants.AppInstalledVersion));
        _mockCurrentVersionProvider.IsDevBuild.Returns(false);
        _mockGitHubService.GetReleases().Returns([]);

        var updateService = new UpdateService(
            _mockCurrentVersionProvider,
            _appTitleService,
            _mockGitHubService,
            _mockDeploymentService,
            _mockTraceLogger,
            _mockAlertDialogService);

        await updateService.CheckForUpdates(usePreRelease: false, manualScan: false);

        _mockDeploymentService.DidNotReceive().RestartNowAndUpdate(Arg.Any<string>());
        _mockDeploymentService.DidNotReceive().UpdateOnNextRestart(Arg.Any<string>());
        await _mockAlertDialogService.Received(1)
            .ShowAlert("Update Failure", Arg.Is<string>(s => s.Contains("No releases available")), "Ok");
    }

    [Fact]
    public async Task CheckForUpdates_NoUpdatesAvailable_ShouldAlert()
    {
        _mockCurrentVersionProvider.CurrentVersion.Returns(new Version(Constants.GitHubLatestVersion[1..]));
        _mockCurrentVersionProvider.IsDevBuild.Returns(false);

        var updateService = new UpdateService(
            _mockCurrentVersionProvider,
            _appTitleService,
            _mockGitHubService,
            _mockDeploymentService,
            _mockTraceLogger,
            _mockAlertDialogService);

        await updateService.CheckForUpdates(usePreRelease: false, manualScan: true);

        _mockDeploymentService.DidNotReceive().RestartNowAndUpdate(Arg.Any<string>());
        _mockDeploymentService.DidNotReceive().UpdateOnNextRestart(Arg.Any<string>());
        await _mockAlertDialogService.Received(1).ShowAlert(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>());
    }

    [Fact]
    public async Task CheckForUpdates_OnCurrentPrerelease_ShouldSetPrereleaseFlag()
    {
        _mockCurrentVersionProvider.CurrentVersion.Returns(new Version(Constants.GitHubPrereleaseVersion[1..]));
        _mockCurrentVersionProvider.IsDevBuild.Returns(false);

        var mockAppTitleService = Substitute.For<IAppTitleService>();

        var updateService = new UpdateService(
            _mockCurrentVersionProvider,
            mockAppTitleService,
            _mockGitHubService,
            _mockDeploymentService,
            _mockTraceLogger,
            _mockAlertDialogService);

        await updateService.CheckForUpdates(usePreRelease: true, manualScan: false);

        mockAppTitleService.Received(1).SetIsPrerelease(true);
        _mockDeploymentService.DidNotReceive().RestartNowAndUpdate(Arg.Any<string>());
        _mockDeploymentService.DidNotReceive().UpdateOnNextRestart(Arg.Any<string>());
    }

    [Fact]
    public async Task CheckForUpdates_Prerelease_ShouldUpdateImmediately()
    {
        _mockCurrentVersionProvider.CurrentVersion.Returns(new Version(Constants.AppInstalledVersion));
        _mockCurrentVersionProvider.IsDevBuild.Returns(false);

        _mockAlertDialogService
            .ShowAlert(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>())
            .Returns(Task.FromResult(true));

        var updateService = new UpdateService(
            _mockCurrentVersionProvider,
            _appTitleService,
            _mockGitHubService,
            _mockDeploymentService,
            _mockTraceLogger,
            _mockAlertDialogService);

        await updateService.CheckForUpdates(usePreRelease: true, manualScan: false);

        _mockDeploymentService.Received(1).RestartNowAndUpdate(Constants.GitHubPrereleaseUri);
    }

    [Fact]
    public async Task CheckForUpdates_PreRelease_ShouldUpdateOnNextRestart()
    {
        _mockCurrentVersionProvider.CurrentVersion.Returns(new Version(Constants.AppInstalledVersion));
        _mockCurrentVersionProvider.IsDevBuild.Returns(false);

        var updateService = new UpdateService(
            _mockCurrentVersionProvider,
            _appTitleService,
            _mockGitHubService,
            _mockDeploymentService,
            _mockTraceLogger,
            _mockAlertDialogService);

        await updateService.CheckForUpdates(usePreRelease: true, manualScan: false);

        _mockDeploymentService.Received(1).UpdateOnNextRestart(Constants.GitHubPrereleaseUri);
    }

    [Fact]
    public async Task GetReleaseNotes_NoCurrentChanges_ShouldShowFailureAlert()
    {
        var updateService = new UpdateService(
            _mockCurrentVersionProvider,
            _appTitleService,
            _mockGitHubService,
            _mockDeploymentService,
            _mockTraceLogger,
            _mockAlertDialogService);

        await updateService.GetReleaseNotes();

        await _mockAlertDialogService.Received(1).ShowAlert(
            "Release Notes Failure",
            "Failed to get release notes for the current version",
            "Ok");
    }

    [Fact]
    public async Task GetReleaseNotes_WithCurrentChanges_ShouldShowAlert()
    {
        _mockCurrentVersionProvider.CurrentVersion.Returns(new Version(Constants.GitHubLatestVersion[1..]));
        _mockCurrentVersionProvider.IsDevBuild.Returns(false);

        var updateService = new UpdateService(
            _mockCurrentVersionProvider,
            _appTitleService,
            _mockGitHubService,
            _mockDeploymentService,
            _mockTraceLogger,
            _mockAlertDialogService);

        await updateService.CheckForUpdates(usePreRelease: false, manualScan: false);
        await updateService.GetReleaseNotes();

        await _mockAlertDialogService.Received(1)
            .ShowAlert(Arg.Is<string>(s => s.Contains("Release notes")), Arg.Any<string>(), "Ok");
    }
}
