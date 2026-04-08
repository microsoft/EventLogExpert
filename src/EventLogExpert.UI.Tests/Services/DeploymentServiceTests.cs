// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Eventing.Helpers;
using EventLogExpert.UI.Interfaces;
using EventLogExpert.UI.Options;
using EventLogExpert.UI.Services;
using EventLogExpert.UI.Tests.TestUtils;
using EventLogExpert.UI.Tests.TestUtils.Constants;
using Microsoft.Extensions.Logging;
using NSubstitute;
using Windows.Foundation;

namespace EventLogExpert.UI.Tests.Services;

public sealed class DeploymentServiceTests
{
    [Fact]
    public void Constructor_WhenCalled_ShouldNotThrow()
    {
        // Arrange & Act
        var exception = Record.Exception(() => CreateDeploymentService());

        // Assert
        Assert.Null(exception);
    }

    [Fact]
    public void RestartNowAndUpdate_WhenApplicationRestartFails_ShouldNotCallPackageDeployment()
    {
        // Arrange
        var mockPackageDeploymentService = Substitute.For<IPackageDeploymentService>();
        var mockApplicationRestartService = Substitute.For<IApplicationRestartService>();
        mockApplicationRestartService.RegisterApplicationRestart().Returns(false);

        var deploymentService = CreateDeploymentService(
            applicationRestartService: mockApplicationRestartService,
            packageDeploymentService: mockPackageDeploymentService);

        // Act
        deploymentService.RestartNowAndUpdate(Constants.DownloadPath);

        // Assert
        mockApplicationRestartService.Received(1).RegisterApplicationRestart();

        mockPackageDeploymentService.DidNotReceive()
            .AddPackageAsync(Arg.Any<Uri>(), Arg.Any<PackageDeploymentOptions>());
    }

    [Fact]
    public void RestartNowAndUpdate_WhenApplicationRestartSucceeds_ShouldCallPackageDeployment()
    {
        // Arrange
        var mockDeploymentOperation = new DeploymentUtils.MockDeploymentOperation();
        var mockPackageDeploymentService = Substitute.For<IPackageDeploymentService>();

        mockPackageDeploymentService
            .AddPackageAsync(Arg.Any<Uri>(), Arg.Any<PackageDeploymentOptions>())
            .Returns(mockDeploymentOperation);

        var mockApplicationRestartService = Substitute.For<IApplicationRestartService>();
        mockApplicationRestartService.RegisterApplicationRestart().Returns(true);

        var deploymentService = CreateDeploymentService(
            applicationRestartService: mockApplicationRestartService,
            packageDeploymentService: mockPackageDeploymentService);

        // Act
        deploymentService.RestartNowAndUpdate(Constants.DownloadPath);

        // Assert
        mockApplicationRestartService.Received(1).RegisterApplicationRestart();

        mockPackageDeploymentService.Received(1)
            .AddPackageAsync(
                Arg.Is<Uri>(uri => uri.LocalPath == Constants.DownloadPath),
                Arg.Is<PackageDeploymentOptions>(opt =>
                    opt.ForceUpdateFromAnyVersion == true &&
                    opt.ForceTargetAppShutdown == true &&
                    opt.DeferRegistrationWhenPackagesAreInUse == false));
    }

    [Fact]
    public void RestartNowAndUpdate_WhenCalled_ShouldTraceMessages()
    {
        // Arrange
        var mockDeploymentOperation = new DeploymentUtils.MockDeploymentOperation();
        var mockPackageDeploymentService = Substitute.For<IPackageDeploymentService>();

        mockPackageDeploymentService
            .AddPackageAsync(Arg.Any<Uri>(), Arg.Any<PackageDeploymentOptions>())
            .Returns(mockDeploymentOperation);

        var mockApplicationRestartService = Substitute.For<IApplicationRestartService>();
        mockApplicationRestartService.RegisterApplicationRestart().Returns(true);

        var mockTraceLogger = Substitute.For<ITraceLogger>();

        var deploymentService = CreateDeploymentService(
            mockTraceLogger,
            applicationRestartService: mockApplicationRestartService,
            packageDeploymentService: mockPackageDeploymentService);

        // Act
        deploymentService.RestartNowAndUpdate(Constants.DownloadPath);

        // Assert
        mockTraceLogger.Received(2).Trace(Arg.Any<string>(), Arg.Any<LogLevel>());
    }

    [Theory]
    [InlineData(AsyncStatus.Canceled)]
    [InlineData(AsyncStatus.Started)]
    public async Task RestartNowAndUpdate_WhenDeploymentCanceledOrStarted_ShouldClearProgress(AsyncStatus status)
    {
        // Arrange
        var mockDeploymentOperation = new DeploymentUtils.MockDeploymentOperation();
        var mockPackageDeploymentService = Substitute.For<IPackageDeploymentService>();

        mockPackageDeploymentService
            .AddPackageAsync(Arg.Any<Uri>(), Arg.Any<PackageDeploymentOptions>())
            .Returns(mockDeploymentOperation);

        var mockApplicationRestartService = Substitute.For<IApplicationRestartService>();
        mockApplicationRestartService.RegisterApplicationRestart().Returns(true);

        var mockAppTitleService = Substitute.For<IAppTitleService>();
        var mockMainThreadService = Substitute.For<IMainThreadService>();

        mockMainThreadService.InvokeOnMainThread(Arg.Any<Action>()).Returns(callInfo =>
        {
            callInfo.Arg<Action>().Invoke();
            return Task.CompletedTask;
        });

        var deploymentService = CreateDeploymentService(
            appTitleService: mockAppTitleService,
            mainThreadService: mockMainThreadService,
            applicationRestartService: mockApplicationRestartService,
            packageDeploymentService: mockPackageDeploymentService);

        // Act
        deploymentService.RestartNowAndUpdate(Constants.DownloadPath);
        mockDeploymentOperation.SimulateCompleted(status);

        // Assert
        await mockMainThreadService.Received(1).InvokeOnMainThread(Arg.Any<Action>());
        mockAppTitleService.Received(1).SetProgressString(null);
    }

    [Fact]
    public async Task RestartNowAndUpdate_WhenDeploymentCompleted_ShouldSetRelaunchMessage()
    {
        // Arrange
        var mockDeploymentOperation = new DeploymentUtils.MockDeploymentOperation();
        var mockPackageDeploymentService = Substitute.For<IPackageDeploymentService>();

        mockPackageDeploymentService
            .AddPackageAsync(Arg.Any<Uri>(), Arg.Any<PackageDeploymentOptions>())
            .Returns(mockDeploymentOperation);

        var mockApplicationRestartService = Substitute.For<IApplicationRestartService>();
        mockApplicationRestartService.RegisterApplicationRestart().Returns(true);

        var mockAppTitleService = Substitute.For<IAppTitleService>();
        var mockMainThreadService = Substitute.For<IMainThreadService>();

        mockMainThreadService.InvokeOnMainThread(Arg.Any<Action>()).Returns(callInfo =>
        {
            callInfo.Arg<Action>().Invoke();
            return Task.CompletedTask;
        });

        var deploymentService = CreateDeploymentService(
            appTitleService: mockAppTitleService,
            mainThreadService: mockMainThreadService,
            applicationRestartService: mockApplicationRestartService,
            packageDeploymentService: mockPackageDeploymentService);

        // Act
        deploymentService.RestartNowAndUpdate(Constants.DownloadPath);
        mockDeploymentOperation.SimulateCompleted(AsyncStatus.Completed);

        // Assert
        await mockMainThreadService.Received(1).InvokeOnMainThread(Arg.Any<Action>());
        mockAppTitleService.Received(1).SetProgressString(Constants.RelaunchMessage);
    }

    [Fact]
    public async Task RestartNowAndUpdate_WhenDeploymentFails_ShouldShowAlertAndClearProgress()
    {
        // Arrange
        var testException = new Exception("Test deployment error");
        var mockDeploymentOperation = new DeploymentUtils.MockDeploymentOperation();
        var mockPackageDeploymentService = Substitute.For<IPackageDeploymentService>();

        mockPackageDeploymentService
            .AddPackageAsync(Arg.Any<Uri>(), Arg.Any<PackageDeploymentOptions>())
            .Returns(mockDeploymentOperation);

        var mockApplicationRestartService = Substitute.For<IApplicationRestartService>();
        mockApplicationRestartService.RegisterApplicationRestart().Returns(true);

        var mockAppTitleService = Substitute.For<IAppTitleService>();
        var mockAlertDialogService = Substitute.For<IAlertDialogService>();
        var mockMainThreadService = Substitute.For<IMainThreadService>();

        mockMainThreadService.InvokeOnMainThread(Arg.Any<Action>()).Returns(callInfo =>
        {
            callInfo.Arg<Action>().Invoke();
            return Task.CompletedTask;
        });

        var deploymentService = CreateDeploymentService(
            appTitleService: mockAppTitleService,
            mainThreadService: mockMainThreadService,
            alertDialogService: mockAlertDialogService,
            applicationRestartService: mockApplicationRestartService,
            packageDeploymentService: mockPackageDeploymentService);

        // Act
        deploymentService.RestartNowAndUpdate(Constants.DownloadPath);
        mockDeploymentOperation.SimulateCompleted(AsyncStatus.Error, testException);

        // Assert
        await mockMainThreadService.Received(1).InvokeOnMainThread(Arg.Any<Action>());
        mockAlertDialogService.Received(1).ShowAlert(
            Constants.UpdateFailureTitle,
            Arg.Is<string>(msg => msg.Contains(testException.ToString())),
            Constants.UpdateFailureOk);

        mockAppTitleService.Received(1).SetProgressString(null);
    }

    [Fact]
    public async Task RestartNowAndUpdate_WhenProgressReported_ShouldUpdateAppTitle()
    {
        // Arrange
        var mockDeploymentOperation = new DeploymentUtils.MockDeploymentOperation();
        var mockPackageDeploymentService = Substitute.For<IPackageDeploymentService>();

        mockPackageDeploymentService
            .AddPackageAsync(Arg.Any<Uri>(), Arg.Any<PackageDeploymentOptions>())
            .Returns(mockDeploymentOperation);

        var mockApplicationRestartService = Substitute.For<IApplicationRestartService>();
        mockApplicationRestartService.RegisterApplicationRestart().Returns(true);

        var mockAppTitleService = Substitute.For<IAppTitleService>();
        var mockMainThreadService = Substitute.For<IMainThreadService>();

        mockMainThreadService.InvokeOnMainThread(Arg.Any<Action>()).Returns(callInfo =>
        {
            callInfo.Arg<Action>().Invoke();
            return Task.CompletedTask;
        });

        var deploymentService = CreateDeploymentService(
            appTitleService: mockAppTitleService,
            mainThreadService: mockMainThreadService,
            applicationRestartService: mockApplicationRestartService,
            packageDeploymentService: mockPackageDeploymentService);

        // Act
        deploymentService.RestartNowAndUpdate(Constants.DownloadPath);
        mockDeploymentOperation.SimulateProgress(50);

        // Assert
        await mockMainThreadService.Received(1).InvokeOnMainThread(Arg.Any<Action>());
        mockAppTitleService.Received(1).SetProgressString(Constants.ProgressString50);
    }

    [Fact]
    public void UpdateOnNextRestart_WhenCalled_ShouldCallPackageDeploymentWithDeferredRegistration()
    {
        // Arrange
        var mockDeploymentOperation = new DeploymentUtils.MockDeploymentOperation();
        var mockPackageDeploymentService = Substitute.For<IPackageDeploymentService>();

        mockPackageDeploymentService
            .AddPackageAsync(Arg.Any<Uri>(), Arg.Any<PackageDeploymentOptions>())
            .Returns(mockDeploymentOperation);

        var deploymentService = CreateDeploymentService(packageDeploymentService: mockPackageDeploymentService);

        // Act
        deploymentService.UpdateOnNextRestart(Constants.DownloadPath);

        // Assert
        mockPackageDeploymentService.Received(1)
            .AddPackageAsync(
                Arg.Is<Uri>(uri => uri.LocalPath == Constants.DownloadPath),
                Arg.Is<PackageDeploymentOptions>(opt =>
                    opt.ForceUpdateFromAnyVersion == true &&
                    opt.ForceTargetAppShutdown == false &&
                    opt.DeferRegistrationWhenPackagesAreInUse == true));
    }

    [Fact]
    public void UpdateOnNextRestart_WhenCalled_ShouldNotCallApplicationRestart()
    {
        // Arrange
        var mockDeploymentOperation = new DeploymentUtils.MockDeploymentOperation();
        var mockPackageDeploymentService = Substitute.For<IPackageDeploymentService>();

        mockPackageDeploymentService
            .AddPackageAsync(Arg.Any<Uri>(), Arg.Any<PackageDeploymentOptions>())
            .Returns(mockDeploymentOperation);

        var mockApplicationRestartService = Substitute.For<IApplicationRestartService>();

        var deploymentService = CreateDeploymentService(
            applicationRestartService: mockApplicationRestartService,
            packageDeploymentService: mockPackageDeploymentService);

        // Act
        deploymentService.UpdateOnNextRestart(Constants.DownloadPath);

        // Assert
        mockApplicationRestartService.DidNotReceive().RegisterApplicationRestart();
    }

    [Fact]
    public void UpdateOnNextRestart_WhenCalled_ShouldTraceMessage()
    {
        // Arrange
        var mockDeploymentOperation = new DeploymentUtils.MockDeploymentOperation();
        var mockPackageDeploymentService = Substitute.For<IPackageDeploymentService>();

        mockPackageDeploymentService
            .AddPackageAsync(Arg.Any<Uri>(), Arg.Any<PackageDeploymentOptions>())
            .Returns(mockDeploymentOperation);

        var mockTraceLogger = Substitute.For<ITraceLogger>();

        var deploymentService = CreateDeploymentService(
            mockTraceLogger,
            packageDeploymentService: mockPackageDeploymentService);

        // Act
        deploymentService.UpdateOnNextRestart(Constants.DownloadPath);

        // Assert
        mockTraceLogger.Received(1).Trace(Arg.Any<string>(), Arg.Any<LogLevel>());
    }

    [Fact]
    public async Task UpdateOnNextRestart_WhenDeploymentCompleted_ShouldSetRelaunchMessage()
    {
        // Arrange
        var mockDeploymentOperation = new DeploymentUtils.MockDeploymentOperation();
        var mockPackageDeploymentService = Substitute.For<IPackageDeploymentService>();

        mockPackageDeploymentService
            .AddPackageAsync(Arg.Any<Uri>(), Arg.Any<PackageDeploymentOptions>())
            .Returns(mockDeploymentOperation);

        var mockAppTitleService = Substitute.For<IAppTitleService>();
        var mockMainThreadService = Substitute.For<IMainThreadService>();

        mockMainThreadService.InvokeOnMainThread(Arg.Any<Action>()).Returns(callInfo =>
        {
            callInfo.Arg<Action>().Invoke();
            return Task.CompletedTask;
        });

        var deploymentService = CreateDeploymentService(
            appTitleService: mockAppTitleService,
            mainThreadService: mockMainThreadService,
            packageDeploymentService: mockPackageDeploymentService);

        // Act
        deploymentService.UpdateOnNextRestart(Constants.DownloadPath);
        mockDeploymentOperation.SimulateCompleted(AsyncStatus.Completed);

        // Assert
        await mockMainThreadService.Received(1).InvokeOnMainThread(Arg.Any<Action>());
        mockAppTitleService.Received(1).SetProgressString(Constants.RelaunchMessage);
    }

    [Fact]
    public async Task UpdateOnNextRestart_WhenDeploymentFails_ShouldShowAlertAndClearProgress()
    {
        // Arrange
        var testException = new Exception("Test deployment error");
        var mockDeploymentOperation = new DeploymentUtils.MockDeploymentOperation();
        var mockPackageDeploymentService = Substitute.For<IPackageDeploymentService>();

        mockPackageDeploymentService
            .AddPackageAsync(Arg.Any<Uri>(), Arg.Any<PackageDeploymentOptions>())
            .Returns(mockDeploymentOperation);

        var mockAppTitleService = Substitute.For<IAppTitleService>();
        var mockAlertDialogService = Substitute.For<IAlertDialogService>();
        var mockMainThreadService = Substitute.For<IMainThreadService>();

        mockMainThreadService.InvokeOnMainThread(Arg.Any<Action>()).Returns(callInfo =>
        {
            callInfo.Arg<Action>().Invoke();
            return Task.CompletedTask;
        });

        var deploymentService = CreateDeploymentService(
            appTitleService: mockAppTitleService,
            mainThreadService: mockMainThreadService,
            alertDialogService: mockAlertDialogService,
            packageDeploymentService: mockPackageDeploymentService);

        // Act
        deploymentService.UpdateOnNextRestart(Constants.DownloadPath);
        mockDeploymentOperation.SimulateCompleted(AsyncStatus.Error, testException);

        // Assert
        await mockMainThreadService.Received(1).InvokeOnMainThread(Arg.Any<Action>());
        mockAlertDialogService.Received(1).ShowAlert(
            Constants.UpdateFailureTitle,
            Arg.Is<string>(msg => msg.Contains(testException.ToString())),
            Constants.UpdateFailureOk);

        mockAppTitleService.Received(1).SetProgressString(null);
    }

    [Fact]
    public async Task UpdateOnNextRestart_WhenMultipleProgressReports_ShouldUpdateAppTitleEachTime()
    {
        // Arrange
        var mockDeploymentOperation = new DeploymentUtils.MockDeploymentOperation();
        var mockPackageDeploymentService = Substitute.For<IPackageDeploymentService>();

        mockPackageDeploymentService
            .AddPackageAsync(Arg.Any<Uri>(), Arg.Any<PackageDeploymentOptions>())
            .Returns(mockDeploymentOperation);

        var mockAppTitleService = Substitute.For<IAppTitleService>();
        var mockMainThreadService = Substitute.For<IMainThreadService>();

        mockMainThreadService.InvokeOnMainThread(Arg.Any<Action>()).Returns(callInfo =>
        {
            callInfo.Arg<Action>().Invoke();
            return Task.CompletedTask;
        });

        var deploymentService = CreateDeploymentService(
            appTitleService: mockAppTitleService,
            mainThreadService: mockMainThreadService,
            packageDeploymentService: mockPackageDeploymentService);

        // Act
        deploymentService.UpdateOnNextRestart(Constants.DownloadPath);
        mockDeploymentOperation.SimulateProgress(25);
        mockDeploymentOperation.SimulateProgress(50);
        mockDeploymentOperation.SimulateProgress(100);

        // Assert
        await mockMainThreadService.Received(3).InvokeOnMainThread(Arg.Any<Action>());
        mockAppTitleService.Received(1).SetProgressString(Constants.ProgressString25);
        mockAppTitleService.Received(1).SetProgressString(Constants.ProgressString50);
        mockAppTitleService.Received(1).SetProgressString(Constants.ProgressString100);
    }

    [Fact]
    public async Task UpdateOnNextRestart_WhenProgressReported_ShouldUpdateAppTitle()
    {
        // Arrange
        var mockDeploymentOperation = new DeploymentUtils.MockDeploymentOperation();
        var mockPackageDeploymentService = Substitute.For<IPackageDeploymentService>();

        mockPackageDeploymentService
            .AddPackageAsync(Arg.Any<Uri>(), Arg.Any<PackageDeploymentOptions>())
            .Returns(mockDeploymentOperation);

        var mockAppTitleService = Substitute.For<IAppTitleService>();
        var mockMainThreadService = Substitute.For<IMainThreadService>();

        mockMainThreadService.InvokeOnMainThread(Arg.Any<Action>()).Returns(callInfo =>
        {
            callInfo.Arg<Action>().Invoke();
            return Task.CompletedTask;
        });

        var deploymentService = CreateDeploymentService(
            appTitleService: mockAppTitleService,
            mainThreadService: mockMainThreadService,
            packageDeploymentService: mockPackageDeploymentService);

        // Act
        deploymentService.UpdateOnNextRestart(Constants.DownloadPath);
        mockDeploymentOperation.SimulateProgress(25);

        // Assert
        await mockMainThreadService.Received(1).InvokeOnMainThread(Arg.Any<Action>());
        mockAppTitleService.Received(1).SetProgressString(Constants.ProgressString25);
    }

    private static DeploymentService CreateDeploymentService(
        ITraceLogger? traceLogger = null,
        IAppTitleService? appTitleService = null,
        IMainThreadService? mainThreadService = null,
        IAlertDialogService? alertDialogService = null,
        IApplicationRestartService? applicationRestartService = null,
        IPackageDeploymentService? packageDeploymentService = null)
    {
        return new DeploymentService(
            traceLogger ?? Substitute.For<ITraceLogger>(),
            appTitleService ?? Substitute.For<IAppTitleService>(),
            mainThreadService ?? Substitute.For<IMainThreadService>(),
            alertDialogService ?? Substitute.For<IAlertDialogService>(),
            applicationRestartService ?? Substitute.For<IApplicationRestartService>(),
            packageDeploymentService ?? Substitute.For<IPackageDeploymentService>());
    }
}
