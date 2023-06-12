// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Eventing.Helpers;
using EventLogExpert.Test.Services;
using EventLogExpert.UI.Models;
using EventLogExpert.UI.Services;
using Moq;
using Xunit.Abstractions;

namespace EventLogExpert.Test;

public class UpdateServiceTests
{
    private readonly ITestOutputHelper _outputHelper;
    private IAppTitleService _appTitleService;
    private IGitHubService _gitHubService;
    private ITraceLogger _traceLogger;

    public UpdateServiceTests(ITestOutputHelper outputHelper)
    {
        _outputHelper = outputHelper;
        _traceLogger = new TestTraceLogger(outputHelper);

        var appTitleService = new Mock<IAppTitleService>();
        _appTitleService = appTitleService.Object;

        var gitHubService = new Mock<IGitHubService>();
        var releases = new List<GitReleaseModel>
        {
            new()
            {
                Version = "v23.1.1.3",
                IsPrerelease = true,
                ReleaseDate = DateTime.Now,
                Assets = new List<GitReleaseAsset>
                {
                    new()
                    {
                        Name = "EventLogExpert_23.1.1.3_x64.msix",
                        Uri = "https://github.com/microsoft/EventLogExpert/releases/download/v23.1.1.3/EventLogExpert_23.1.1.3_x64.msix"
                    }
                }
            },
            new()
            {
                Version = "v23.1.1.2",
                IsPrerelease = false,
                ReleaseDate = DateTime.Now.AddDays(-1),
                Assets = new List<GitReleaseAsset>
                {
                    new()
                    {
                        Name = "EventLogExpert_23.1.1.2_x64.msix",
                        Uri = "https://github.com/microsoft/EventLogExpert/releases/download/v23.1.1.2/EventLogExpert_23.1.1.2_x64.msix"
                    }
                }
            }
        };

        gitHubService.Setup(g => g.GetReleases()).Returns(Task.FromResult(releases.AsEnumerable()));
        _gitHubService = gitHubService.Object;
    }

    [Fact]
    public async void ShouldUpdateToPrereleaseOnNextRestart()
    {
        var versionProvider = new Mock<ICurrentVersionProvider>();
        versionProvider.Setup(v => v.CurrentVersion).Returns(new Version("23.1.1.1"));
        versionProvider.Setup(v => v.IsDevBuild).Returns(false);

        var titleProvider = new TestTitleProvider();

        var appTitleService = new AppTitleService(versionProvider.Object, titleProvider);

        var mainThreadService = new TestMainThreadService();

        var deploymentService = new Mock<IDeploymentService>();
        var alertService = new Mock<IAlertDialogService>();

        var updateService = new UpdateService(
            versionProvider.Object,
            _appTitleService,
            _gitHubService,
            deploymentService.Object,
            _traceLogger,
            alertService.Object,
            mainThreadService);

        await updateService.CheckForUpdates(prereleaseVersionsEnabled: true, manualScan: false);

        deploymentService.Verify(d =>
            d.UpdateOnNextRestart("https://github.com/microsoft/EventLogExpert/releases/download/v23.1.1.3/EventLogExpert_23.1.1.3_x64.msix"), Times.Once());
    }

    [Fact]
    public async void ShouldUpdateToLatestOnNextRestart()
    {
        var versionProvider = new Mock<ICurrentVersionProvider>();
        versionProvider.Setup(v => v.CurrentVersion).Returns(new Version("23.1.1.1"));
        versionProvider.Setup(v => v.IsDevBuild).Returns(false);

        var titleProvider = new TestTitleProvider();

        var appTitleService = new AppTitleService(versionProvider.Object, titleProvider);

        var mainThreadService = new TestMainThreadService();

        var deploymentService = new Mock<IDeploymentService>();
        var alertService = new Mock<IAlertDialogService>();

        var updateService = new UpdateService(
            versionProvider.Object,
            _appTitleService,
            _gitHubService,
            deploymentService.Object,
            _traceLogger,
            alertService.Object,
            mainThreadService);

        await updateService.CheckForUpdates(prereleaseVersionsEnabled: false, manualScan: false);

        deploymentService.Verify(d =>
            d.UpdateOnNextRestart("https://github.com/microsoft/EventLogExpert/releases/download/v23.1.1.2/EventLogExpert_23.1.1.2_x64.msix"), Times.Once());
    }

    [Fact]
    public async void ShouldUpdateToPrereleaseImmediately()
    {
        var versionProvider = new Mock<ICurrentVersionProvider>();
        versionProvider.Setup(v => v.CurrentVersion).Returns(new Version("23.1.1.1"));
        versionProvider.Setup(v => v.IsDevBuild).Returns(false);

        var titleProvider = new TestTitleProvider();

        var appTitleService = new AppTitleService(versionProvider.Object, titleProvider);

        var mainThreadService = new TestMainThreadService();

        var deploymentService = new Mock<IDeploymentService>();
        var alertService = new Mock<IAlertDialogService>();
        alertService.Setup(a => a.ShowAlert(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>())).ReturnsAsync(true);

        var updateService = new UpdateService(
            versionProvider.Object,
            _appTitleService,
            _gitHubService,
            deploymentService.Object,
            _traceLogger,
            alertService.Object,
            mainThreadService);

        await updateService.CheckForUpdates(prereleaseVersionsEnabled: true, manualScan: false);

        deploymentService.Verify(d =>
            d.RestartNowAndUpdate("https://github.com/microsoft/EventLogExpert/releases/download/v23.1.1.3/EventLogExpert_23.1.1.3_x64.msix"), Times.Once());
    }

    [Fact]
    public async void ShouldUpdateToLatestImmediately()
    {
        var versionProvider = new Mock<ICurrentVersionProvider>();
        versionProvider.Setup(v => v.CurrentVersion).Returns(new Version("23.1.1.1"));
        versionProvider.Setup(v => v.IsDevBuild).Returns(false);

        var titleProvider = new TestTitleProvider();

        var appTitleService = new AppTitleService(versionProvider.Object, titleProvider);

        var mainThreadService = new TestMainThreadService();

        var deploymentService = new Mock<IDeploymentService>();
        var alertService = new Mock<IAlertDialogService>();
        alertService.Setup(a => a.ShowAlert(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>())).ReturnsAsync(true);

        var updateService = new UpdateService(
            versionProvider.Object,
            _appTitleService,
            _gitHubService,
            deploymentService.Object,
            _traceLogger,
            alertService.Object,
            mainThreadService);

        await updateService.CheckForUpdates(prereleaseVersionsEnabled: false, manualScan: false);

        deploymentService.Verify(d =>
            d.RestartNowAndUpdate("https://github.com/microsoft/EventLogExpert/releases/download/v23.1.1.2/EventLogExpert_23.1.1.2_x64.msix"), Times.Once());
    }

    [Fact]
    public async void ShouldAlertNoUpdatesAvailable()
    {
        var versionProvider = new Mock<ICurrentVersionProvider>();
        versionProvider.Setup(v => v.CurrentVersion).Returns(new Version("23.1.1.2"));
        versionProvider.Setup(v => v.IsDevBuild).Returns(false);

        var titleProvider = new TestTitleProvider();

        var appTitleService = new AppTitleService(versionProvider.Object, titleProvider);

        var mainThreadService = new TestMainThreadService();

        var deploymentService = new Mock<IDeploymentService>();
        var alertService = new Mock<IAlertDialogService>();

        var updateService = new UpdateService(
            versionProvider.Object,
            _appTitleService,
            _gitHubService,
            deploymentService.Object,
            _traceLogger,
            alertService.Object,
            mainThreadService);

        await updateService.CheckForUpdates(prereleaseVersionsEnabled: false, manualScan: true);

        deploymentService.Verify(d =>
            d.RestartNowAndUpdate(It.IsAny<string>()), Times.Never);
        deploymentService.Verify(d =>
            d.UpdateOnNextRestart(It.IsAny<string>()), Times.Never);
        alertService.Verify(a =>
            a.ShowAlert(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()), Times.Once());
    }
}
