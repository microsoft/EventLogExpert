// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Logging.Abstractions;
using EventLogExpert.Logging.Abstractions.Handlers;
using EventLogExpert.Runtime.Alerts;
using EventLogExpert.Runtime.Common.AppTitle;
using EventLogExpert.Runtime.Common.Restart;
using EventLogExpert.Runtime.Common.Threading;
using EventLogExpert.Runtime.Tests.TestUtils;
using EventLogExpert.Runtime.Tests.TestUtils.Constants;
using EventLogExpert.Runtime.Update.Deployment;
using NSubstitute;
using Windows.Foundation;

namespace EventLogExpert.Runtime.Tests.Update.Deployment;

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
    public void RestartNowAndUpdate_WhenCalled_ShouldStageUpdateWithDeferredRegistration()
    {
        // Arrange
        var mockDeploymentOperation = new DeploymentUtils.MockDeploymentOperation();
        var mockPackageDeploymentService = Substitute.For<IPackageDeploymentService>();

        mockPackageDeploymentService
            .AddPackageAsync(Arg.Any<Uri>(), Arg.Any<PackageDeploymentOptions>())
            .Returns(mockDeploymentOperation);

        var deploymentService = CreateDeploymentService(packageDeploymentService: mockPackageDeploymentService);

        // Act
        deploymentService.RestartNowAndUpdate(Constants.DownloadPath, true);

        // Assert
        // Force shutdown must stay disabled: it makes the OS quiesce/kill the running app and log an AppHang.
        // NSubstitute's Received() doesn't reliably return configured values for IAsyncOperationWithProgress
        _ = mockPackageDeploymentService.Received(1)
            .AddPackageAsync(
                Arg.Is<Uri>(uri => uri != null && uri.LocalPath == Constants.DownloadPath),
                Arg.Is<PackageDeploymentOptions>(opt => opt != null &&
                    opt.ForceUpdateFromAnyVersion == true &&
                    opt.ForceTargetAppShutdown == false &&
                    opt.DeferRegistrationWhenPackagesAreInUse == true));
    }

    [Fact]
    public void RestartNowAndUpdate_WhenCalled_ShouldTraceMessage()
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
        deploymentService.RestartNowAndUpdate(Constants.DownloadPath, true);

        // Assert
        mockTraceLogger.Received(1).Debug(Arg.Any<DebugLogHandler>());
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

        var mockAppTitleService = Substitute.For<IAppTitleService>();
        var mockMainThreadService = Substitute.For<IMainThreadService>();

        mockMainThreadService.InvokeOnMainThread(Arg.Any<Action>()).Returns(callInfo =>
        {
            callInfo.ArgAt<Action>(0).Invoke();
            return Task.CompletedTask;
        });

        mockMainThreadService.InvokeOnMainThreadAsync(Arg.Any<Func<Task>>()).Returns(callInfo => callInfo.ArgAt<Func<Task>>(0)());

        var deploymentService = CreateDeploymentService(
            appTitleService: mockAppTitleService,
            mainThreadService: mockMainThreadService,
            applicationRestartService: mockApplicationRestartService,
            packageDeploymentService: mockPackageDeploymentService);

        // Act
        deploymentService.RestartNowAndUpdate(Constants.DownloadPath, true);
        mockDeploymentOperation.SimulateCompleted(status);

        // Assert
        await mockMainThreadService.Received(1).InvokeOnMainThreadAsync(Arg.Any<Func<Task>>());
        mockAppTitleService.Received(1).SetProgress(null);
    }

    [Fact]
    public async Task RestartNowAndUpdate_WhenDeploymentCompleted_ShouldRequestGracefulRestart()
    {
        // Arrange
        var mockDeploymentOperation = new DeploymentUtils.MockDeploymentOperation();
        var mockPackageDeploymentService = Substitute.For<IPackageDeploymentService>();

        mockPackageDeploymentService
            .AddPackageAsync(Arg.Any<Uri>(), Arg.Any<PackageDeploymentOptions>())
            .Returns(mockDeploymentOperation);

        var mockApplicationRestartService = Substitute.For<IApplicationRestartService>();
        mockApplicationRestartService.TryRestartAsync(Arg.Any<string>()).Returns(true);

        var mockAppTitleService = Substitute.For<IAppTitleService>();
        var mockMainThreadService = Substitute.For<IMainThreadService>();

        mockMainThreadService.InvokeOnMainThread(Arg.Any<Action>()).Returns(callInfo =>
        {
            callInfo.ArgAt<Action>(0).Invoke();
            return Task.CompletedTask;
        });

        mockMainThreadService.InvokeOnMainThreadAsync(Arg.Any<Func<Task>>()).Returns(callInfo => callInfo.ArgAt<Func<Task>>(0)());

        var deploymentService = CreateDeploymentService(
            appTitleService: mockAppTitleService,
            mainThreadService: mockMainThreadService,
            applicationRestartService: mockApplicationRestartService,
            packageDeploymentService: mockPackageDeploymentService);

        // Act
        deploymentService.RestartNowAndUpdate(Constants.DownloadPath, true);
        mockDeploymentOperation.SimulateCompleted(AsyncStatus.Completed);

        // Assert
        await mockApplicationRestartService.Received(1).TryRestartAsync(Arg.Any<string>());
    }

    [Fact]
    public async Task RestartNowAndUpdate_WhenDeploymentFailsAutoScan_ShouldNotShowAlertButClearProgress()
    {
        // Arrange
        var testException = new Exception("Test deployment error");
        var mockDeploymentOperation = new DeploymentUtils.MockDeploymentOperation();
        var mockPackageDeploymentService = Substitute.For<IPackageDeploymentService>();

        mockPackageDeploymentService
            .AddPackageAsync(Arg.Any<Uri>(), Arg.Any<PackageDeploymentOptions>())
            .Returns(mockDeploymentOperation);

        var mockApplicationRestartService = Substitute.For<IApplicationRestartService>();

        var mockAppTitleService = Substitute.For<IAppTitleService>();
        var mockAlertDialogService = Substitute.For<IAlertDialogService>();
        var mockMainThreadService = Substitute.For<IMainThreadService>();

        mockMainThreadService.InvokeOnMainThread(Arg.Any<Action>()).Returns(callInfo =>
        {
            callInfo.ArgAt<Action>(0).Invoke();
            return Task.CompletedTask;
        });

        mockMainThreadService.InvokeOnMainThreadAsync(Arg.Any<Func<Task>>()).Returns(callInfo => callInfo.ArgAt<Func<Task>>(0)());

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
        await mockAlertDialogService.DidNotReceive().ShowAlert(
            Arg.Any<LocalizableText>(),
            Arg.Any<LocalizableText>(),
            Arg.Any<LocalizableText>());

        mockAppTitleService.Received(1).SetProgress(null);
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

        var mockAppTitleService = Substitute.For<IAppTitleService>();
        var mockAlertDialogService = Substitute.For<IAlertDialogService>();
        var mockMainThreadService = Substitute.For<IMainThreadService>();

        mockMainThreadService.InvokeOnMainThread(Arg.Any<Action>()).Returns(callInfo =>
        {
            callInfo.ArgAt<Action>(0).Invoke();
            return Task.CompletedTask;
        });

        mockMainThreadService.InvokeOnMainThreadAsync(Arg.Any<Func<Task>>()).Returns(callInfo => callInfo.ArgAt<Func<Task>>(0)());

        var deploymentService = CreateDeploymentService(
            appTitleService: mockAppTitleService,
            mainThreadService: mockMainThreadService,
            alertDialogService: mockAlertDialogService,
            applicationRestartService: mockApplicationRestartService,
            packageDeploymentService: mockPackageDeploymentService);

        // Act
        deploymentService.RestartNowAndUpdate(Constants.DownloadPath, true);
        mockDeploymentOperation.SimulateCompleted(AsyncStatus.Error, testException);

        // Assert
        await mockMainThreadService.Received(1).InvokeOnMainThreadAsync(Arg.Any<Func<Task>>());
        await mockAlertDialogService.Received(1).ShowAlert(
            KeyIs("Update_Alert_Failure_Title"),
            Arg.Is<LocalizableText>(text =>
                text.Key == "Update_Alert_InstallFailed_Message" &&
                text.Args.Count == 1 &&
                text.Args[0].Contains(testException.Message)),
            KeyIs("Modal_Accept"));

        mockAppTitleService.Received(1).SetProgress(null);
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

        var mockAppTitleService = Substitute.For<IAppTitleService>();
        var mockMainThreadService = Substitute.For<IMainThreadService>();

        mockMainThreadService.InvokeOnMainThread(Arg.Any<Action>()).Returns(callInfo =>
        {
            callInfo.ArgAt<Action>(0).Invoke();
            return Task.CompletedTask;
        });

        mockMainThreadService.InvokeOnMainThreadAsync(Arg.Any<Func<Task>>()).Returns(callInfo => callInfo.ArgAt<Func<Task>>(0)());

        var deploymentService = CreateDeploymentService(
            appTitleService: mockAppTitleService,
            mainThreadService: mockMainThreadService,
            applicationRestartService: mockApplicationRestartService,
            packageDeploymentService: mockPackageDeploymentService);

        // Act
        deploymentService.RestartNowAndUpdate(Constants.DownloadPath, true);
        mockDeploymentOperation.SimulateProgress(50);

        // Assert
        await mockMainThreadService.Received(1).InvokeOnMainThread(Arg.Any<Action>());
        mockAppTitleService.Received(1).SetProgress(new AppTitleProgress.Installing(50));
    }

    [Fact]
    public async Task RestartNowAndUpdate_WhenRestartDenied_ShouldSurfaceRelaunchPrompt()
    {
        // Arrange
        var mockDeploymentOperation = new DeploymentUtils.MockDeploymentOperation();
        var mockPackageDeploymentService = Substitute.For<IPackageDeploymentService>();

        mockPackageDeploymentService
            .AddPackageAsync(Arg.Any<Uri>(), Arg.Any<PackageDeploymentOptions>())
            .Returns(mockDeploymentOperation);

        var mockApplicationRestartService = Substitute.For<IApplicationRestartService>();
        mockApplicationRestartService.TryRestartAsync(Arg.Any<string>()).Returns(false);

        var mockAppTitleService = Substitute.For<IAppTitleService>();
        var mockMainThreadService = Substitute.For<IMainThreadService>();

        mockMainThreadService.InvokeOnMainThread(Arg.Any<Action>()).Returns(callInfo =>
        {
            callInfo.ArgAt<Action>(0).Invoke();
            return Task.CompletedTask;
        });

        mockMainThreadService.InvokeOnMainThreadAsync(Arg.Any<Func<Task>>()).Returns(callInfo => callInfo.ArgAt<Func<Task>>(0)());

        var deploymentService = CreateDeploymentService(
            appTitleService: mockAppTitleService,
            mainThreadService: mockMainThreadService,
            applicationRestartService: mockApplicationRestartService,
            packageDeploymentService: mockPackageDeploymentService);

        // Act
        deploymentService.RestartNowAndUpdate(Constants.DownloadPath, true);
        mockDeploymentOperation.SimulateCompleted(AsyncStatus.Completed);

        // Assert
        await mockApplicationRestartService.Received(1).TryRestartAsync(Arg.Any<string>());
        mockAppTitleService.Received(1).SetProgress(new AppTitleProgress.RelaunchToApply());
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
        deploymentService.UpdateOnNextRestart(Constants.DownloadPath, true);

        // Assert
        // NSubstitute's Received() doesn't reliably return configured values for IAsyncOperationWithProgress
        _ = mockPackageDeploymentService.Received(1)
            .AddPackageAsync(
                Arg.Is<Uri>(uri => uri != null && uri.LocalPath == Constants.DownloadPath),
                Arg.Is<PackageDeploymentOptions>(opt => opt != null &&
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
        deploymentService.UpdateOnNextRestart(Constants.DownloadPath, true);

        // Assert
        _ = mockApplicationRestartService.DidNotReceive().TryRestartAsync(Arg.Any<string>());
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
        deploymentService.UpdateOnNextRestart(Constants.DownloadPath, true);

        // Assert
        mockTraceLogger.Received(1).Debug(Arg.Any<DebugLogHandler>());
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
            callInfo.ArgAt<Action>(0).Invoke();
            return Task.CompletedTask;
        });

        mockMainThreadService.InvokeOnMainThreadAsync(Arg.Any<Func<Task>>()).Returns(callInfo => callInfo.ArgAt<Func<Task>>(0)());

        var deploymentService = CreateDeploymentService(
            appTitleService: mockAppTitleService,
            mainThreadService: mockMainThreadService,
            packageDeploymentService: mockPackageDeploymentService);

        // Act
        deploymentService.UpdateOnNextRestart(Constants.DownloadPath, true);
        mockDeploymentOperation.SimulateCompleted(AsyncStatus.Completed);

        // Assert
        await mockMainThreadService.Received(1).InvokeOnMainThreadAsync(Arg.Any<Func<Task>>());
        mockAppTitleService.Received(1).SetProgress(new AppTitleProgress.RelaunchToApply());
    }

    [Fact]
    public async Task UpdateOnNextRestart_WhenDeploymentFailsAutoScan_ShouldNotShowAlertButClearProgress()
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
            callInfo.ArgAt<Action>(0).Invoke();
            return Task.CompletedTask;
        });

        mockMainThreadService.InvokeOnMainThreadAsync(Arg.Any<Func<Task>>()).Returns(callInfo => callInfo.ArgAt<Func<Task>>(0)());

        var deploymentService = CreateDeploymentService(
            appTitleService: mockAppTitleService,
            mainThreadService: mockMainThreadService,
            alertDialogService: mockAlertDialogService,
            packageDeploymentService: mockPackageDeploymentService);

        // Act
        deploymentService.UpdateOnNextRestart(Constants.DownloadPath);
        mockDeploymentOperation.SimulateCompleted(AsyncStatus.Error, testException);

        // Assert
        await mockAlertDialogService.DidNotReceive().ShowAlert(
            Arg.Any<LocalizableText>(),
            Arg.Any<LocalizableText>(),
            Arg.Any<LocalizableText>());

        mockAppTitleService.Received(1).SetProgress(null);
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
            callInfo.ArgAt<Action>(0).Invoke();
            return Task.CompletedTask;
        });

        mockMainThreadService.InvokeOnMainThreadAsync(Arg.Any<Func<Task>>()).Returns(callInfo => callInfo.ArgAt<Func<Task>>(0)());

        var deploymentService = CreateDeploymentService(
            appTitleService: mockAppTitleService,
            mainThreadService: mockMainThreadService,
            alertDialogService: mockAlertDialogService,
            packageDeploymentService: mockPackageDeploymentService);

        // Act
        deploymentService.UpdateOnNextRestart(Constants.DownloadPath, true);
        mockDeploymentOperation.SimulateCompleted(AsyncStatus.Error, testException);

        // Assert
        await mockMainThreadService.Received(1).InvokeOnMainThreadAsync(Arg.Any<Func<Task>>());
        await mockAlertDialogService.Received(1).ShowAlert(
            KeyIs("Update_Alert_Failure_Title"),
            Arg.Is<LocalizableText>(text =>
                text.Key == "Update_Alert_InstallFailed_Message" &&
                text.Args.Count == 1 &&
                text.Args[0].Contains(testException.Message)),
            KeyIs("Modal_Accept"));

        mockAppTitleService.Received(1).SetProgress(null);
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
            callInfo.ArgAt<Action>(0).Invoke();
            return Task.CompletedTask;
        });

        mockMainThreadService.InvokeOnMainThreadAsync(Arg.Any<Func<Task>>()).Returns(callInfo => callInfo.ArgAt<Func<Task>>(0)());

        var deploymentService = CreateDeploymentService(
            appTitleService: mockAppTitleService,
            mainThreadService: mockMainThreadService,
            packageDeploymentService: mockPackageDeploymentService);

        // Act
        deploymentService.UpdateOnNextRestart(Constants.DownloadPath, true);
        mockDeploymentOperation.SimulateProgress(25);
        mockDeploymentOperation.SimulateProgress(50);
        mockDeploymentOperation.SimulateProgress(100);

        // Assert
        await mockMainThreadService.Received(3).InvokeOnMainThread(Arg.Any<Action>());
        mockAppTitleService.Received(1).SetProgress(new AppTitleProgress.Installing(25));
        mockAppTitleService.Received(1).SetProgress(new AppTitleProgress.Installing(50));
        mockAppTitleService.Received(1).SetProgress(new AppTitleProgress.Installing(100));
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
            callInfo.ArgAt<Action>(0).Invoke();
            return Task.CompletedTask;
        });

        mockMainThreadService.InvokeOnMainThreadAsync(Arg.Any<Func<Task>>()).Returns(callInfo => callInfo.ArgAt<Func<Task>>(0)());

        var deploymentService = CreateDeploymentService(
            appTitleService: mockAppTitleService,
            mainThreadService: mockMainThreadService,
            packageDeploymentService: mockPackageDeploymentService);

        // Act
        deploymentService.UpdateOnNextRestart(Constants.DownloadPath, true);
        mockDeploymentOperation.SimulateProgress(25);

        // Assert
        await mockMainThreadService.Received(1).InvokeOnMainThread(Arg.Any<Action>());
        mockAppTitleService.Received(1).SetProgress(new AppTitleProgress.Installing(25));
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

    private static LocalizableText KeyIs(string key) => Arg.Is<LocalizableText>(text => text.Key == key);
}
