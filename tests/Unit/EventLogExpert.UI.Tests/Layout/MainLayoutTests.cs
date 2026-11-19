// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using Bunit;
using EventLogExpert.Logging.Abstractions;
using EventLogExpert.Runtime.Banner;
using EventLogExpert.Runtime.Common.AppTitle;
using EventLogExpert.Runtime.Menu;
using EventLogExpert.Runtime.Settings;
using EventLogExpert.Runtime.Update;
using EventLogExpert.UI.Announcement;
using EventLogExpert.UI.ErrorHandling;
using EventLogExpert.UI.Keyboard;
using EventLogExpert.UI.Layout;
using EventLogExpert.UI.LogTable.Find;
using EventLogExpert.UI.LogTable.Stats;
using EventLogExpert.UI.Menu;
using EventLogExpert.UI.Modal;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.JSInterop;
using Microsoft.JSInterop.Infrastructure;
using NSubstitute;
using NSubstitute.Core;
using NSubstitute.Extensions;
using System.Collections.Concurrent;
using StatusBarComponent = EventLogExpert.UI.StatusBar.StatusBar;
using TestContext = Xunit.TestContext;

namespace EventLogExpert.UI.Tests.Layout;

public sealed class MainLayoutTests : BunitContext
{
    private const string DisposeCall = "dispose";
    private const string FocusRingModulePath = "./_content/EventLogExpert.UI/Common/keyboardFocusRing.js";
    private const string FocusTooltipModulePath = "./_content/EventLogExpert.UI/Common/focusTooltip.js";
    private const string KeyboardModulePath = "./_content/EventLogExpert.UI/Keyboard/Keyboard.js";
    private const string RegisterFocusRingCall = "registerKeyboardFocusRing";
    private const string RegisterFocusTooltipCall = "registerFocusTooltip";
    private const string RegisterKeyboardCall = "registerKeyboardShortcuts";
    private const string SetThemeCall = "setTheme";
    private const string ThemeModulePath = "./_content/EventLogExpert.UI/Layout/MainLayout.razor.js";
    private const string UnregisterKeyboardCall = "unregisterKeyboardShortcuts";

    private static readonly string[] s_disposedModulePaths =
    [
        KeyboardModulePath,
        ThemeModulePath,
        FocusRingModulePath,
        FocusTooltipModulePath
    ];

    private static readonly TimeSpan s_handshakeTimeout = TimeSpan.FromSeconds(10);

    private static readonly string[] s_startupModulePaths =
    [
        ThemeModulePath,
        FocusRingModulePath,
        FocusTooltipModulePath
    ];
    private static readonly (string ModulePath, string Call)[] s_startupCalls =
    [
        (ThemeModulePath, SetThemeCall),
        (FocusRingModulePath, RegisterFocusRingCall),
        (FocusTooltipModulePath, RegisterFocusTooltipCall)
    ];

    private readonly IAppTitleService _appTitle = Substitute.For<IAppTitleService>();
    private readonly IErrorBoundaryLogger _boundaryLogger = Substitute.For<IErrorBoundaryLogger>();
    private readonly ICriticalErrorService _criticalErrors = Substitute.For<ICriticalErrorService>();
    private readonly ConcurrentBag<TaskCompletionSource> _gatesReleasedOnDispose = [];
    private readonly ConcurrentDictionary<string, Func<ValueTask<IJSObjectReference>>> _importBehaviors = new();
    private readonly ConcurrentDictionary<string, TaskCompletionSource> _importRequests = new();
    private readonly IJSObjectReference _javascriptModule = Substitute.For<IJSObjectReference>();
    private readonly IJSRuntime _javascriptRuntime = Substitute.For<IJSRuntime>();
    private readonly TaskCompletionSource _layoutDisposalStarted = CreateGate();
    private readonly ConcurrentDictionary<string, ScriptedModule> _modules = new();
    private readonly ConcurrentBag<TaskCompletionSource<IJSObjectReference>> _pendingImports = [];
    private readonly ISettingsService _settings = Substitute.For<ISettingsService>();
    private readonly IUpdateService _updateService = Substitute.For<IUpdateService>();

    public MainLayoutTests()
    {
        Services.AddSingleton(_criticalErrors);
        Services.AddSingleton(_javascriptRuntime);
        Services.AddSingleton(_settings);
        Services.AddSingleton(_appTitle);
        Services.AddSingleton(_updateService);
        Services.AddSingleton(Substitute.For<ITraceLogger>());
        Services.AddSingleton(_boundaryLogger);
        Services.AddSingleton(Substitute.For<IMenuActionService>());
        Services.AddSingleton(Substitute.For<IModalCoordinator>());
        Services.AddSingleton(Substitute.For<IFindCoordinator>());
        Services.AddSingleton<KeyboardShortcutService>();

        ComponentFactories.AddStub<ModalHost>();
        ComponentFactories.AddStub<MenuHost>();
        ComponentFactories.AddStub<MenuBar>();
        ComponentFactories.AddStub<MainContent>();
        ComponentFactories.AddStub<StatsDrawer>();
        ComponentFactories.AddStub<StatusBarComponent>();
        ComponentFactories.AddStub<AnnouncerHost>();

        _settings.ThemeChanged = null;
        _settings.When(settings => settings.ThemeChanged = Arg.Is<Action?>(handler => handler == null))
            .Do(_ => _layoutDisposalStarted.TrySetResult());
    }

    public static TheoryData<string> DisposedModulePaths() => [.. s_disposedModulePaths];

    public static TheoryData<string, string, Exception> ExpectedCallFailures()
    {
        TheoryData<string, string, Exception> cases = [];

        foreach ((string modulePath, string call) in s_startupCalls)
        {
            cases.Add(modulePath, call, new JSException("Call failed."));

            foreach (Exception failure in CreateExpectedTeardownFailures())
            {
                cases.Add(modulePath, call, failure);
            }
        }

        return cases;
    }

    public static TheoryData<string, Exception> ExpectedLateDisposalFailures()
    {
        TheoryData<string, Exception> cases = [];

        foreach (string modulePath in s_startupModulePaths)
        {
            foreach (Exception failure in CreateExpectedTeardownFailures())
            {
                cases.Add(modulePath, failure);
            }
        }

        return cases;
    }

    public static TheoryData<string, Exception> ExpectedModuleDisposalFailures()
    {
        TheoryData<string, Exception> cases = [];

        foreach (string modulePath in s_disposedModulePaths)
        {
            foreach (Exception failure in CreateExpectedTeardownFailures())
            {
                cases.Add(modulePath, failure);
            }
        }

        return cases;
    }

    public static TheoryData<string> StartupModulePaths() => [.. s_startupModulePaths];

    public static TheoryData<string> ThemeModulePaths() =>
    [
        ThemeModulePath
    ];

    [Fact]
    public async Task Dispose_FocusRingImportCompletesMidTeardown_DisposesModuleOnceWithoutRegistering()
    {
        RouteImports();
        var import = PendImport(FocusRingModulePath);
        TaskCompletionSource keyboardRelease = CreateGateReleasedOnDispose();
        var keyboard = ModuleAt(KeyboardModulePath);
        keyboard.SetBehavior(UnregisterKeyboardCall, BlockedUntil(keyboardRelease));
        var focusRing = ModuleAt(FocusRingModulePath);
        var component = RenderLayout();
        var layout = component.FindComponent<MainLayout>();
        await AwaitBoundedAsync(ImportRequested(FocusRingModulePath));

        var teardown = layout.Instance.DisposeAsync().AsTask();
        await AwaitBoundedAsync(keyboard.Started(UnregisterKeyboardCall));
        import.SetResult(focusRing.Reference);
        await AwaitBoundedAsync(focusRing.DisposeStarted);
        keyboardRelease.SetResult();
        await AwaitBoundedAsync(teardown);
        await FlushAsync(component);

        Assert.Equal([DisposeCall], focusRing.Calls());
        Assert.Equal(0, CountImports(FocusTooltipModulePath));
    }

    [Theory]
    [MemberData(nameof(StartupModulePaths))]
    public async Task Dispose_ImportFaultsUnexpectedlyAfterDisposal_ReportsCriticalOnceWithoutBoundary(string modulePath)
    {
        RouteImports();
        var import = PendImport(modulePath);
        var reports = ExpectReports(1);
        var component = RenderLayout();
        var layout = component.FindComponent<MainLayout>();
        InvalidOperationException failure = new("Import failed after disposal.");
        await AwaitBoundedAsync(ImportRequested(modulePath));

        await AwaitBoundedAsync(layout.Instance.DisposeAsync());
        import.SetException(failure);
        var reported = await AwaitBoundedAsync(reports);
        await FlushAsync(component);

        Assert.Same(failure, Assert.Single(reported));
        await AssertReportedDirectlyAsync(failure);
    }

    [Theory]
    [MemberData(nameof(StartupModulePaths))]
    public async Task Dispose_ImportPending_ReturnsWithoutAwaitingAndDisposesLateModuleOnce(string modulePath)
    {
        RouteImports();
        var import = PendImport(modulePath);
        var lateModule = ModuleAt(modulePath);
        var component = RenderLayout();
        var layout = component.FindComponent<MainLayout>();
        await AwaitBoundedAsync(ImportRequested(modulePath));

        await AwaitBoundedAsync(layout.Instance.DisposeAsync());
        import.SetResult(lateModule.Reference);
        await AwaitBoundedAsync(lateModule.DisposeStarted);
        await FlushAsync(component);

        Assert.Equal([DisposeCall], lateModule.Calls());
        Assert.All(StartupPathsAfter(modulePath), laterPath => Assert.Equal(0, CountImports(laterPath)));
    }

    [Fact]
    public async Task Dispose_KeyboardRegistrationFailsUnexpectedlyAfterDisposal_ReportsCriticalOnceWithoutBoundary()
    {
        RouteImports();
        TaskCompletionSource registerGate = CreateGateReleasedOnDispose();
        var keyboard = ModuleAt(KeyboardModulePath);
        keyboard.SetBehavior(RegisterKeyboardCall, BlockedUntil(registerGate));
        var reports = ExpectReports(1);
        var component = RenderLayout();
        var layout = component.FindComponent<MainLayout>();
        InvalidOperationException failure = new("Keyboard registration failed after disposal.");
        await AwaitBoundedAsync(keyboard.Started(RegisterKeyboardCall));

        var teardown = layout.Instance.DisposeAsync().AsTask();
        registerGate.SetException(failure);
        var reported = await AwaitBoundedAsync(reports);
        await AwaitBoundedAsync(teardown);
        await FlushAsync(component);

        Assert.Same(failure, Assert.Single(reported));
        await AssertReportedDirectlyAsync(failure);
    }

    [Theory]
    [MemberData(nameof(ExpectedLateDisposalFailures))]
    public async Task Dispose_LateModuleDisposalFailsExpectedly_CompletesWithoutReport(string modulePath, Exception failure)
    {
        RouteImports();
        var import = PendImport(modulePath);
        var lateModule = ModuleAt(modulePath);
        lateModule.SetBehavior(DisposeCall, Throwing(failure));
        var component = RenderLayout();
        var layout = component.FindComponent<MainLayout>();
        await AwaitBoundedAsync(ImportRequested(modulePath));

        var teardown = layout.Instance.DisposeAsync().AsTask();
        import.SetResult(lateModule.Reference);
        await AwaitBoundedAsync(lateModule.DisposeStarted);
        await AwaitBoundedAsync(teardown);
        await FlushAsync(component);

        await AssertNotReportedAsync();
    }

    [Theory]
    [MemberData(nameof(StartupModulePaths))]
    public async Task Dispose_LateModuleDisposalFailsUnexpectedly_ReportsCriticalOnce(string modulePath)
    {
        RouteImports();
        var import = PendImport(modulePath);
        var lateModule = ModuleAt(modulePath);
        JSException failure = new("Late module disposal failed.");
        lateModule.SetBehavior(DisposeCall, Throwing(failure));
        var reports = ExpectReports(1);
        var component = RenderLayout();
        var layout = component.FindComponent<MainLayout>();
        await AwaitBoundedAsync(ImportRequested(modulePath));

        var teardown = layout.Instance.DisposeAsync().AsTask();
        import.SetResult(lateModule.Reference);
        await AwaitBoundedAsync(lateModule.DisposeStarted);
        var reported = await AwaitBoundedAsync(reports);
        await AwaitBoundedAsync(teardown);
        await FlushAsync(component);

        Assert.Same(failure, Assert.Single(reported));
        await AssertReportedDirectlyAsync(failure);
    }

    [Theory]
    [MemberData(nameof(ExpectedModuleDisposalFailures))]
    public async Task Dispose_ModuleDisposalFailsExpectedly_IsSwallowed(string failingPath, Exception failure)
    {
        RouteImports();
        ModuleAt(failingPath).SetBehavior(DisposeCall, Throwing(failure));
        var component = RenderLayout();
        var layout = component.FindComponent<MainLayout>();
        await AwaitStartupAsync();

        await AwaitBoundedAsync(layout.Instance.DisposeAsync());

        AssertEveryModuleDisposedOnce();
    }

    [Theory]
    [MemberData(nameof(DisposedModulePaths))]
    public async Task Dispose_ModuleDisposalFailsWithJavaScriptException_SurfacesAfterRemainingModulesAreDisposed(
        string failingPath)
    {
        RouteImports();
        JSException failure = new("Module disposal failed.");
        ModuleAt(failingPath).SetBehavior(DisposeCall, Throwing(failure));
        var component = RenderLayout();
        var layout = component.FindComponent<MainLayout>();
        await AwaitStartupAsync();

        var thrown = await Assert.ThrowsAsync<JSException>(() => layout.Instance.DisposeAsync().AsTask());

        Assert.Same(failure, thrown);
        AssertEveryModuleDisposedOnce();
    }

    [Fact]
    public async Task Dispose_MultipleModuleDisposalsFail_ThrowsAggregateAfterAllModulesAreDisposed()
    {
        RouteImports();
        InvalidOperationException keyboardFailure = new("Keyboard module disposal failed.");
        InvalidOperationException focusRingFailure = new("Focus ring module disposal failed.");
        ModuleAt(KeyboardModulePath).SetBehavior(DisposeCall, Throwing(keyboardFailure));
        ModuleAt(FocusRingModulePath).SetBehavior(DisposeCall, Throwing(focusRingFailure));
        var component = RenderLayout();
        var layout = component.FindComponent<MainLayout>();
        await AwaitStartupAsync();

        var thrown = await Assert.ThrowsAsync<AggregateException>(() => layout.Instance.DisposeAsync().AsTask());

        Exception[] expected = [keyboardFailure, focusRingFailure];
        Assert.Equal(expected, thrown.InnerExceptions);
        AssertEveryModuleDisposedOnce();
    }

    [Fact]
    public async Task Dispose_UpdateCheckPending_SkipsLogNameResetWhenCheckCompletes()
    {
        RouteImports();
        TaskCompletionSource updateCheck = new();
        _updateService.CheckForUpdates(Arg.Any<bool>(), Arg.Any<bool>()).Returns(updateCheck.Task);
        var component = RenderLayout();
        var layout = component.FindComponent<MainLayout>();

        await AwaitBoundedAsync(layout.Instance.DisposeAsync());
        updateCheck.SetResult();
        await FlushAsync(component);

        _appTitle.DidNotReceive().SetLogName(Arg.Any<string?>());
    }

    [Fact]
    public async Task Dispose_WhenModuleDisposalFails_DisposesRemainingModulesBeforeRethrowing()
    {
        _javascriptRuntime.ReturnsForAll(ValueTask.FromResult(_javascriptModule));

        var originalException = new InvalidOperationException("Module disposal failed.");
        var disposalAttempts = 0;
        _javascriptModule.DisposeAsync().Returns(_ =>
            ++disposalAttempts == 1
                ? ValueTask.FromException(originalException)
                : ValueTask.CompletedTask);

        var component = Render<UnhandledExceptionHandler>(parameters => parameters.AddChildContent<MainLayout>());
        var layout = component.FindComponent<MainLayout>();

        var disposalException = await Assert.ThrowsAsync<InvalidOperationException>(
            async () => await layout.Instance.DisposeAsync());

        Assert.Same(originalException, disposalException);
        await _javascriptModule.Received(4).DisposeAsync();
    }

    [Theory]
    [MemberData(nameof(ExpectedCallFailures))]
    public async Task FirstRender_ExpectedCallFailure_IsSwallowedAndStartupContinues(
        string modulePath,
        string call,
        Exception failure)
    {
        RouteImports();
        ModuleAt(modulePath).SetBehavior(call, Throwing(failure));

        var component = RenderLayout();
        await AwaitStartupAsync();
        await FlushAsync(component);

        AssertKeyboardFocusRingAndTooltipRegistered();
        await AssertNotReportedAsync();
    }

    [Fact]
    public async Task FirstRender_ExpectedThemeImportFailure_SkipsThemeAndContinuesStartup()
    {
        RouteImports();
        _importBehaviors[ThemeModulePath] = () =>
            ValueTask.FromException<IJSObjectReference>(new JSException("Theme import failed."));

        var component = RenderLayout();
        await AwaitStartupAsync();
        await FlushAsync(component);

        Assert.Equal(0, ModuleAt(ThemeModulePath).CountCalls(SetThemeCall));
        AssertKeyboardFocusRingAndTooltipRegistered();
        await AssertNotReportedAsync();
    }

    [Theory]
    [MemberData(nameof(StartupModulePaths))]
    public void FirstRender_ImmediateImportFailure_IsHandled(string modulePath)
    {
        var originalException = new JSException("Startup import failed immediately.");
        _javascriptRuntime.ReturnsForAll<ValueTask<IJSObjectReference>>(invocation =>
            IsImportFor(invocation, modulePath) ? throw originalException : ValueTask.FromResult(_javascriptModule));

        var component = Render<UnhandledExceptionHandler>(parameters => parameters.AddChildContent<MainLayout>());

        component.WaitForAssertion(() =>
        {
            _criticalErrors.DidNotReceive().ReportCritical(Arg.Any<Exception>());
        });
        Assert.False(Renderer.UnhandledException.IsCompleted);
    }

    [Fact]
    public async Task FirstRender_KeyboardRegistrationFailsUnexpectedlyWhileLive_ReachesLifecycleOnce()
    {
        RouteImports();
        InvalidOperationException failure = new("Keyboard registration failed.");
        ModuleAt(KeyboardModulePath).SetBehavior(RegisterKeyboardCall, ThrowingAfterYield(failure));
        var boundaryLogs = ExpectBoundaryLogs(1);

        var component = RenderLayout();
        var logged = await AwaitBoundedAsync(boundaryLogs);
        await AwaitLayoutDisposalStartAsync();
        await FlushAsync(component);

        Assert.Same(failure, Assert.Single(logged));
        await AssertSurfacedThroughBoundaryOnceAsync(failure);
    }

    [Theory]
    [MemberData(nameof(StartupModulePaths))]
    public async Task FirstRender_UnexpectedAsyncImportFailure_ReachesBoundaryOnceAndTeardownDoesNotRethrow(
        string modulePath)
    {
        RouteImports();
        var import = PendImport(modulePath);
        var boundaryLogs = ExpectBoundaryLogs(1);
        InvalidOperationException failure = new("Unexpected startup failure.");
        var component = RenderLayout();
        await AwaitBoundedAsync(ImportRequested(modulePath));

        import.SetException(failure);
        var logged = await AwaitBoundedAsync(boundaryLogs);
        await AwaitLayoutDisposalStartAsync();
        await FlushAsync(component);

        Assert.Same(failure, Assert.Single(logged));
        await AssertSurfacedThroughBoundaryOnceAsync(failure);
        Assert.Equal(1, CountImports(modulePath));
    }

    [Fact]
    public async Task OnInitializedAsync_UpdateCheckCompletesWhileLive_ResetsLogName()
    {
        RouteImports();
        TaskCompletionSource updateCheck = new();
        _updateService.CheckForUpdates(Arg.Any<bool>(), Arg.Any<bool>()).Returns(updateCheck.Task);
        TaskCompletionSource logNameReset = CreateGateReleasedOnDispose();
        _appTitle.When(title => title.SetLogName(Arg.Any<string?>())).Do(_ => logNameReset.TrySetResult());
        var component = RenderLayout();

        updateCheck.SetResult();
        await AwaitBoundedAsync(logNameReset.Task);
        await FlushAsync(component);

        _appTitle.Received(1).SetLogName(null);
    }

    [Theory]
    [MemberData(nameof(ThemeModulePaths))]
    public async Task ThemeChanged_FaultedImport_RetriesThenReusesSuccessfulLoad(string modulePath)
    {
        var attempts = 0;
        _javascriptRuntime.ReturnsForAll<ValueTask<IJSObjectReference>>(invocation =>
            IsImportFor(invocation, modulePath) && ++attempts == 1
                ? ValueTask.FromException<IJSObjectReference>(new JSException("Theme import failed asynchronously."))
                : ValueTask.FromResult(_javascriptModule));
        var component = Render<UnhandledExceptionHandler>(parameters => parameters.AddChildContent<MainLayout>());
        Assert.Equal(1, CountImports(modulePath));

        var onThemeChanged = _settings.ThemeChanged;
        Assert.NotNull(onThemeChanged);
        await component.InvokeAsync(onThemeChanged);
        await component.InvokeAsync(onThemeChanged);

        component.WaitForAssertion(() =>
        {
            Assert.Equal(2, CountImports(modulePath));
            Assert.Equal(2, _javascriptModule.ReceivedCalls().Count(invocation =>
                Equals(invocation.GetArguments()[0], "setTheme")));
        });
        _criticalErrors.DidNotReceive().ReportCritical(Arg.Any<Exception>());
    }

    [Theory]
    [MemberData(nameof(ThemeModulePaths))]
    public async Task ThemeChanged_FaultedInvocation_ReusesSuccessfullyImportedModule(string modulePath)
    {
        var themeInvocations = 0;
        _javascriptRuntime.ReturnsForAll(ValueTask.FromResult(_javascriptModule));
        _javascriptModule.ReturnsForAll<ValueTask<IJSVoidResult>>(invocation =>
            invocation.ArgAt<string>(0) == "setTheme" && ++themeInvocations == 1
                ? ValueTask.FromException<IJSVoidResult>(new JSException("Theme application failed."))
                : default);
        var component = Render<UnhandledExceptionHandler>(parameters => parameters.AddChildContent<MainLayout>());

        var onThemeChanged = _settings.ThemeChanged;
        Assert.NotNull(onThemeChanged);
        await component.InvokeAsync(onThemeChanged);

        component.WaitForAssertion(() =>
        {
            Assert.Equal(1, CountImports(modulePath));
            Assert.Equal(2, themeInvocations);
        });
        _criticalErrors.DidNotReceive().ReportCritical(Arg.Any<Exception>());
    }

    [Fact]
    public async Task ThemeChanged_RetryAfterExpectedImportFailure_ReportsNewAttemptFailureOnce()
    {
        RouteImports();
        TaskCompletionSource<IJSObjectReference> firstAttempt = new(TaskCreationOptions.RunContinuationsAsynchronously);
        TaskCompletionSource<IJSObjectReference> secondAttempt = new(TaskCreationOptions.RunContinuationsAsynchronously);
        var attempts = 0;
        TaskCompletionSource secondImportRequested = CreateGate();
        _importBehaviors[ThemeModulePath] = () =>
        {
            if (Interlocked.Increment(ref attempts) == 1)
            {
                return new ValueTask<IJSObjectReference>(firstAttempt.Task);
            }

            secondImportRequested.TrySetResult();

            return new ValueTask<IJSObjectReference>(secondAttempt.Task);
        };
        var boundaryLogs = ExpectBoundaryLogs(1);
        InvalidOperationException failure = new("Retried theme import failed.");
        var component = RenderLayout();
        await AwaitBoundedAsync(ImportRequested(ThemeModulePath));

        firstAttempt.SetException(new JSException("Theme import failed."));
        await AwaitStartupAsync();
        await RaiseThemeChangedAsync(component);
        await AwaitBoundedAsync(secondImportRequested.Task);
        secondAttempt.SetException(failure);
        var logged = await AwaitBoundedAsync(boundaryLogs);
        await AwaitLayoutDisposalStartAsync();
        await FlushAsync(component);

        Assert.Same(failure, Assert.Single(logged));
        await AssertSurfacedThroughBoundaryOnceAsync(failure);
    }

    [Fact]
    public async Task ThemeChanged_SetThemeFailsAfterDisposalOnTwoCalls_ReportsEachCallOnce()
    {
        RouteImports();
        TaskCompletionSource firstCall = CreateGateReleasedOnDispose();
        TaskCompletionSource secondCall = CreateGateReleasedOnDispose();
        var theme = ModuleAt(ThemeModulePath);
        theme.SetBehavior(SetThemeCall, BlockedInOrder(firstCall, secondCall));
        var reports = ExpectReports(2);
        InvalidOperationException failure = new("Theme application failed after disposal.");
        var component = RenderLayout();
        var layout = component.FindComponent<MainLayout>();
        await AwaitBoundedAsync(theme.Started(SetThemeCall));
        await RaiseThemeChangedAsync(component);
        await AwaitBoundedAsync(theme.Started(SetThemeCall, 2));

        await AwaitBoundedAsync(layout.Instance.DisposeAsync());
        firstCall.SetException(failure);
        secondCall.SetException(failure);
        var reported = await AwaitBoundedAsync(reports);
        await FlushAsync(component);

        Assert.All(reported, reportedFailure => Assert.Same(failure, reportedFailure));
        await AssertReportedDirectlyAsync(failure, 2);
    }

    [Fact]
    public async Task ThemeChanged_SetThemeFailsUnexpectedly_DispatchesToBoundaryOnce()
    {
        RouteImports();
        var component = RenderLayout();
        await AwaitStartupAsync();
        InvalidOperationException failure = new("Theme application failed.");
        ModuleAt(ThemeModulePath).SetBehavior(SetThemeCall, Throwing(failure));
        var boundaryLogs = ExpectBoundaryLogs(1);

        await RaiseThemeChangedAsync(component);
        var logged = await AwaitBoundedAsync(boundaryLogs);
        await AwaitLayoutDisposalStartAsync();
        await FlushAsync(component);

        Assert.Same(failure, Assert.Single(logged));
        await AssertSurfacedThroughBoundaryOnceAsync(failure);
    }

    [Fact]
    public async Task ThemeChanged_SharedImportFailsUnexpectedly_ReachesBoundaryExactlyOnce()
    {
        RouteImports();
        var import = PendImport(ThemeModulePath);
        var boundaryLogs = ExpectBoundaryLogs(1);
        InvalidOperationException failure = new("Shared theme import failed.");
        var component = RenderLayout();
        await AwaitBoundedAsync(ImportRequested(ThemeModulePath));
        await RaiseThemeChangedAsync(component);
        await FlushAsync(component);

        import.SetException(failure);
        var logged = await AwaitBoundedAsync(boundaryLogs);
        await AwaitLayoutDisposalStartAsync();
        await FlushAsync(component);

        Assert.Same(failure, Assert.Single(logged));
        await AssertSurfacedThroughBoundaryOnceAsync(failure);
        Assert.Equal(1, CountImports(ThemeModulePath));
    }

    protected override async ValueTask DisposeAsyncCore()
    {
        foreach (var gate in _gatesReleasedOnDispose)
        {
            gate.TrySetResult();
        }

        foreach (var import in _pendingImports)
        {
            import.TrySetCanceled();
        }

        await base.DisposeAsyncCore();
    }

    private static Task AwaitBoundedAsync(Task task) =>
        task.WaitAsync(s_handshakeTimeout, TestContext.Current.CancellationToken);

    private static Task AwaitBoundedAsync(ValueTask task) => AwaitBoundedAsync(task.AsTask());

    private static Task<T> AwaitBoundedAsync<T>(Task<T> task) =>
        task.WaitAsync(s_handshakeTimeout, TestContext.Current.CancellationToken);

    private static Func<ValueTask> BlockedInOrder(params TaskCompletionSource[] gates)
    {
        var callCount = -1;

        return () =>
        {
            int index = Interlocked.Increment(ref callCount);

            return index < gates.Length ? new ValueTask(gates[index].Task) : ValueTask.CompletedTask;
        };
    }

    private static Func<ValueTask> BlockedUntil(TaskCompletionSource gate) => BlockedInOrder(gate);

    private static Exception[] CreateExpectedTeardownFailures() =>
    [
        new JSDisconnectedException("Circuit gone."),
        new ObjectDisposedException("module"),
        new TaskCanceledException(),
        new OperationCanceledException()
    ];

    private static TaskCompletionSource CreateGate() => new(TaskCreationOptions.RunContinuationsAsynchronously);

    private static bool IsImportFor(CallInfo invocation, string modulePath) =>
        invocation.ArgAt<string>(0) == "import" &&
        invocation.ArgAt<object?[]>(1) is [string requestedPath] && requestedPath == modulePath;

    private static string[] StartupPathsAfter(string modulePath) => modulePath switch
    {
        ThemeModulePath => [KeyboardModulePath, FocusRingModulePath, FocusTooltipModulePath],
        FocusRingModulePath => [FocusTooltipModulePath],
        _ => []
    };

    private static Func<ValueTask> Throwing(Exception failure) => () => ValueTask.FromException(failure);

    private static Func<ValueTask> ThrowingAfterYield(Exception failure) => async () =>
    {
        await Task.Yield();

        throw failure;
    };

    private async Task AssertBoundaryNotEngagedAsync()
    {
        await _boundaryLogger.DidNotReceive().LogErrorAsync(Arg.Any<Exception>());
        Assert.False(Renderer.UnhandledException.IsCompleted);
    }

    private void AssertEveryModuleDisposedOnce()
    {
        foreach (string modulePath in s_disposedModulePaths)
        {
            Assert.Equal(1, ModuleAt(modulePath).CountCalls(DisposeCall));
        }
    }

    private void AssertKeyboardFocusRingAndTooltipRegistered()
    {
        Assert.Equal(1, ModuleAt(KeyboardModulePath).CountCalls(RegisterKeyboardCall));
        Assert.Equal(1, ModuleAt(FocusRingModulePath).CountCalls(RegisterFocusRingCall));
        Assert.Equal(1, ModuleAt(FocusTooltipModulePath).CountCalls(RegisterFocusTooltipCall));
    }

    private async Task AssertNotReportedAsync()
    {
        _criticalErrors.DidNotReceive().ReportCritical(Arg.Any<Exception>());
        await AssertBoundaryNotEngagedAsync();
    }

    private async Task AssertReportedDirectlyAsync(Exception failure, int count = 1)
    {
        _criticalErrors.Received(count).ReportCritical(Arg.Any<Exception>());
        _criticalErrors.Received(count).ReportCritical(failure);
        await AssertBoundaryNotEngagedAsync();
    }

    private async Task AssertSurfacedThroughBoundaryOnceAsync(Exception failure)
    {
        await _boundaryLogger.Received(1).LogErrorAsync(Arg.Any<Exception>());
        await _boundaryLogger.Received(1).LogErrorAsync(failure);
        _criticalErrors.Received(1).ReportCritical(Arg.Any<Exception>());
        _criticalErrors.Received(1).ReportCritical(failure);
        Assert.False(Renderer.UnhandledException.IsCompleted);
    }

    private Task AwaitLayoutDisposalStartAsync() => AwaitBoundedAsync(_layoutDisposalStarted.Task);

    private Task AwaitStartupAsync() =>
        AwaitBoundedAsync(ModuleAt(FocusTooltipModulePath).Started(RegisterFocusTooltipCall));

    private int CountImports(string modulePath) =>
        _javascriptRuntime.ReceivedCalls().Count(invocation =>
            invocation.GetMethodInfo().Name == nameof(IJSRuntime.InvokeAsync) &&
            invocation.GetArguments() is ["import", object?[] arguments] &&
            arguments is [string requestedPath] && requestedPath == modulePath);

    private TaskCompletionSource CreateGateReleasedOnDispose()
    {
        TaskCompletionSource gate = CreateGate();
        _gatesReleasedOnDispose.Add(gate);

        return gate;
    }

    private Task<Exception[]> ExpectBoundaryLogs(int count)
    {
        ExceptionCollector collector = new(count);
        _boundaryLogger.LogErrorAsync(Arg.Any<Exception>()).Returns(invocation =>
        {
            collector.Record(invocation.Arg<Exception>());

            return ValueTask.CompletedTask;
        });

        return collector.Completion;
    }

    private Task<Exception[]> ExpectReports(int count)
    {
        ExceptionCollector collector = new(count);
        _criticalErrors.When(service => service.ReportCritical(Arg.Any<Exception>()))
            .Do(invocation => collector.Record(invocation.Arg<Exception>()));

        return collector.Completion;
    }

    private Task FlushAsync(IRenderedComponent<UnhandledExceptionHandler> component) =>
        component.InvokeAsync(() => { });

    private TaskCompletionSource ImportRequestFor(string modulePath) =>
        _importRequests.GetOrAdd(modulePath, _ => CreateGate());

    private Task ImportRequested(string modulePath) => ImportRequestFor(modulePath).Task;

    private ScriptedModule ModuleAt(string modulePath) => _modules.GetOrAdd(modulePath, _ => new ScriptedModule());

    private TaskCompletionSource<IJSObjectReference> PendImport(string modulePath)
    {
        TaskCompletionSource<IJSObjectReference> import = new(TaskCreationOptions.RunContinuationsAsynchronously);
        _pendingImports.Add(import);
        _importBehaviors[modulePath] = () => new ValueTask<IJSObjectReference>(import.Task);

        return import;
    }

    private Task RaiseThemeChangedAsync(IRenderedComponent<UnhandledExceptionHandler> component)
    {
        Action? onThemeChanged = _settings.ThemeChanged;
        Assert.NotNull(onThemeChanged);

        return component.InvokeAsync(onThemeChanged);
    }

    private IRenderedComponent<UnhandledExceptionHandler> RenderLayout() =>
        Render<UnhandledExceptionHandler>(parameters => parameters.AddChildContent<MainLayout>());

    private void RouteImports() =>
        _javascriptRuntime.ReturnsForAll<ValueTask<IJSObjectReference>>(invocation =>
        {
            if (invocation.ArgAt<object?[]>(1) is not [string requestedPath])
            {
                throw new InvalidOperationException("Unexpected module import request.");
            }

            ImportRequestFor(requestedPath).TrySetResult();

            return _importBehaviors.TryGetValue(requestedPath, out var behavior)
                ? behavior()
                : ValueTask.FromResult(ModuleAt(requestedPath).Reference);
        });

    private sealed class ExceptionCollector(int expectedCount)
    {
        private readonly List<Exception> _collected = [];
        private readonly TaskCompletionSource<Exception[]> _completion =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly Lock _lock = new();

        public Task<Exception[]> Completion => _completion.Task;

        public void Record(Exception exception)
        {
            lock (_lock)
            {
                _collected.Add(exception);

                if (_collected.Count == expectedCount)
                {
                    _completion.TrySetResult([.. _collected]);
                }
            }
        }
    }

    private sealed class ScriptedModule
    {
        private readonly Dictionary<string, Func<ValueTask>> _behaviors = [];
        private readonly List<string> _calls = [];
        private readonly TaskCompletionSource _disposeStarted = CreateGate();
        private readonly Lock _lock = new();
        private readonly Dictionary<(string Identifier, int Occurrence), TaskCompletionSource> _started = [];

        public ScriptedModule()
        {
            Reference = Substitute.For<IJSObjectReference>();
            Reference.ReturnsForAll<ValueTask<IJSVoidResult>>(invocation => InvokeAsync(invocation.ArgAt<string>(0)));
            Reference.DisposeAsync().Returns(_ => RunAsync(DisposeCall));
        }

        public Task DisposeStarted => _disposeStarted.Task;

        public IJSObjectReference Reference { get; }

        public string[] Calls()
        {
            lock (_lock)
            {
                return [.. _calls];
            }
        }

        public int CountCalls(string identifier)
        {
            lock (_lock)
            {
                return _calls.Count(call => call == identifier);
            }
        }

        public void SetBehavior(string identifier, Func<ValueTask> behavior)
        {
            lock (_lock)
            {
                _behaviors[identifier] = behavior;
            }
        }

        public Task Started(string identifier, int occurrence = 1)
        {
            lock (_lock)
            {
                return StartedSource(identifier, occurrence).Task;
            }
        }

        private async ValueTask<IJSVoidResult> InvokeAsync(string identifier)
        {
            await RunAsync(identifier);

            return default!;
        }

        private Func<ValueTask>? Record(string identifier)
        {
            TaskCompletionSource started;
            Func<ValueTask>? behavior;

            lock (_lock)
            {
                _calls.Add(identifier);
                started = StartedSource(identifier, _calls.Count(call => call == identifier));
                _behaviors.TryGetValue(identifier, out behavior);
            }

            started.TrySetResult();

            if (identifier == DisposeCall)
            {
                _disposeStarted.TrySetResult();
            }

            return behavior;
        }

        private async ValueTask RunAsync(string identifier)
        {
            Func<ValueTask>? behavior = Record(identifier);

            if (behavior is not null)
            {
                await behavior();
            }
        }

        private TaskCompletionSource StartedSource(string identifier, int occurrence)
        {
            if (!_started.TryGetValue((identifier, occurrence), out TaskCompletionSource? source))
            {
                source = CreateGate();
                _started[(identifier, occurrence)] = source;
            }

            return source;
        }
    }
}
