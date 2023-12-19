using EventLogExpert.Eventing.Helpers;
using EventLogExpert.UI.Interfaces;
using EventLogExpert.UI.Services;
using EventLogExpert.UI.UnitTests.TestUtils;
using EventLogExpert.UI.UnitTests.TestUtils.Constants;
using NSubstitute;

namespace EventLogExpert.UI.UnitTests.Services;

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
    public async void CheckForUpdates_Latest_ShouldUpdateImmediately()
    {
        _mockCurrentVersionProvider.CurrentVersion.Returns(new Version(Constants.GitHubOldVersion));
        _mockCurrentVersionProvider.IsDevBuild.Returns(false);

        var alertDialogService = Substitute.For<IAlertDialogService>();

        alertDialogService
            .ShowAlert(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>())
            .Returns(Task.FromResult(true));

        var updateService = new UpdateService(
            _mockCurrentVersionProvider,
            _appTitleService,
            _mockGitHubService,
            _mockDeploymentService,
            _mockTraceLogger,
            alertDialogService);

        await updateService.CheckForUpdates(prereleaseVersionsEnabled: false, manualScan: false);

        _mockDeploymentService.Received(1).RestartNowAndUpdate(Constants.GitHubLatestUri);
    }

    [Fact]
    public async void CheckForUpdates_Latest_ShouldUpdateOnNextRestart()
    {
        _mockCurrentVersionProvider.CurrentVersion.Returns(new Version(Constants.GitHubOldVersion));
        _mockCurrentVersionProvider.IsDevBuild.Returns(false);

        var updateService = new UpdateService(
            _mockCurrentVersionProvider,
            _appTitleService,
            _mockGitHubService,
            _mockDeploymentService,
            _mockTraceLogger,
            _mockAlertDialogService);

        await updateService.CheckForUpdates(prereleaseVersionsEnabled: false, manualScan: false);

        _mockDeploymentService.Received(1).UpdateOnNextRestart(Constants.GitHubLatestUri);
    }

    [Fact]
    public async void CheckForUpdates_NoUpdatesAvailable_ShouldAlert()
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

        await updateService.CheckForUpdates(prereleaseVersionsEnabled: false, manualScan: true);

        _mockDeploymentService.DidNotReceive().RestartNowAndUpdate(Arg.Any<string>());
        _mockDeploymentService.DidNotReceive().UpdateOnNextRestart(Arg.Any<string>());
        await _mockAlertDialogService.Received(1).ShowAlert(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>());
    }

    [Fact]
    public async void CheckForUpdates_Prerelease_ShouldUpdateImmediately()
    {
        _mockCurrentVersionProvider.CurrentVersion.Returns(new Version(Constants.GitHubOldVersion));
        _mockCurrentVersionProvider.IsDevBuild.Returns(false);

        var alertDialogService = Substitute.For<IAlertDialogService>();

        alertDialogService
            .ShowAlert(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>())
            .Returns(Task.FromResult(true));

        var updateService = new UpdateService(
            _mockCurrentVersionProvider,
            _appTitleService,
            _mockGitHubService,
            _mockDeploymentService,
            _mockTraceLogger,
            alertDialogService);

        await updateService.CheckForUpdates(prereleaseVersionsEnabled: true, manualScan: false);

        _mockDeploymentService.Received(1).RestartNowAndUpdate(Constants.GitHubPrereleaseUri);
    }

    [Fact]
    public async void CheckForUpdates_Prerelease_ShouldUpdateOnNextRestart()
    {
        _mockCurrentVersionProvider.CurrentVersion.Returns(new Version(Constants.GitHubOldVersion));
        _mockCurrentVersionProvider.IsDevBuild.Returns(false);

        var updateService = new UpdateService(
            _mockCurrentVersionProvider,
            _appTitleService,
            _mockGitHubService,
            _mockDeploymentService,
            _mockTraceLogger,
            _mockAlertDialogService);

        await updateService.CheckForUpdates(prereleaseVersionsEnabled: true, manualScan: false);

        _mockDeploymentService.Received(1).UpdateOnNextRestart(Constants.GitHubPrereleaseUri);
    }
}
