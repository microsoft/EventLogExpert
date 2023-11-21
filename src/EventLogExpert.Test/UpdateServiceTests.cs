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
    private static string MockChanges =>
        "\r\n\r\n## Changes:\r\n\r\n* f7f7aff67132dc32c92519a1bc250e1a81606e2b Fixed LF issue in App.xaml and added custom width for ultrawide monitors\r\n* 66b7d6883807a5c518ffcd59f92e07e528a5636a Updated Azure yml to .NET 8\r\n* 5b658a9c294a69cec45a14319d3851b700f0e7a2 Updated projects to .NET 8 and updated nuget packages to latest version\r\n* 3237f018153becbd80b1bc2bf7c3b30cc6dfb94f Added feature to sort events by column\r\n* d0fd869d9f9e02b6a9057052fad76d2e54c8edf1 Don't crash if filter is applied with no logs loaded\r\n* b0d34d6eb79405750f6b51cb687ccbe5dbddf40e Updated filters to run in parallel\r\n* a88d718213a62322a8e30787677c65bb050c9488 Basic filter comparison works more like advanced filter does and reduces filter creation complexity\r\n* c3b5da764c0a0e4301b2d568d373c47642159a63 Fixed issue where editing the advanced filter was not updating the event table\r\n* 3653bd8c6c213d13fcc2a399913b35dc762d19bc CombineLogs doesn't need to sort single logs\r\n* f97987ca4f69c6dfec7e56f0a586017458799036 Set TabPane to adjust max width based on the number of tabs open to prevent overflow\r\n<details><summary><b>See More</b></summary>\r\n\r\n* aa502e321a5fd5eb16c4b9e95e1da63845daef80 Reduce unnecessary sorting\r\n* c1d075cdfa6b3f3040c1ccdae5196a54ba6ad451 Updated Severity Level to support different bytes that may resolve to the same level name\r\n* 6ae91e064cec8580d03de40495a54e359943a221 Fixed sorting issue when multiple logs are first loaded with no filters applied\r\n* d549a1693896692d961f54ffcf68e2c3555e6fd9 Refactored filtering in preparation for sorting by different event fields\r\n* 3168fa7807fff0229308b01c321e05c382866a4e Removed additional columns from view menu and added context menu for enabling/disabling columns\r\n* ee5fa7a2ff56bcc147d3bd59ee0f1e1f546a5815 Changed context menu filtering options to have include/exclude as the parent option\r\n\r\nThis list of changes was [auto generated](https://dev.azure.com/CSS-Exchange-Tools/EventLogExpert/_build/results?buildId=4927&view=logs).</details>";

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
                },
                RawChanges = MockChanges
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
                },
                RawChanges = MockChanges
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
            alertService.Object);

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
            alertService.Object);

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
            alertService.Object);

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
            alertService.Object);

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
            alertService.Object);

        await updateService.CheckForUpdates(prereleaseVersionsEnabled: false, manualScan: true);

        deploymentService.Verify(d =>
            d.RestartNowAndUpdate(It.IsAny<string>()), Times.Never);
        deploymentService.Verify(d =>
            d.UpdateOnNextRestart(It.IsAny<string>()), Times.Never);
        alertService.Verify(a =>
            a.ShowAlert(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()), Times.Once());
    }

    [Fact]
    public void ParseChanges_ShouldRemoveCommitIds()
    {
        GitReleaseModel release = new() { RawChanges = MockChanges };

        var result = release.Changes;

        Assert.Equal(16, result.Count);
        Assert.Contains("Updated projects to .NET 8 and updated nuget packages to latest version", result);
        Assert.Contains("Fixed LF issue in App.xaml and added custom width for ultrawide monitors", result);
        Assert.Contains("Updated Azure yml to .NET 8", result);
        Assert.DoesNotContain("f7f7aff67132dc32c92519a1bc250e1a81606e2b", result);
        Assert.DoesNotContain("66b7d6883807a5c518ffcd59f92e07e528a5636a", result);
        Assert.DoesNotContain("5b658a9c294a69cec45a14319d3851b700f0e7a2", result);
    }
}
