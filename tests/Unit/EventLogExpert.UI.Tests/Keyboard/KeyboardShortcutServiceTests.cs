// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Runtime.Menu;
using EventLogExpert.Runtime.Settings;
using EventLogExpert.UI.Keyboard;
using EventLogExpert.UI.LogTable.Find;
using EventLogExpert.UI.Modal;
using Microsoft.JSInterop;
using Microsoft.JSInterop.Infrastructure;
using NSubstitute;
using NSubstitute.Extensions;

namespace EventLogExpert.UI.Tests.Keyboard;

public sealed class KeyboardShortcutServiceTests
{
    private const string DisposeCall = "dispose";
    private const string RegisterCall = "registerKeyboardShortcuts";
    private const string UnregisterCall = "unregisterKeyboardShortcuts";

    private static readonly TimeSpan s_handshakeTimeout = TimeSpan.FromSeconds(10);

    public static TheoryData<Exception> ExpectedDisposalFailures() =>
    [
        new JSDisconnectedException("circuit gone"),
        new OperationCanceledException(),
        new TaskCanceledException(),
        new ObjectDisposedException("module")
    ];

    public static TheoryData<Exception> ExpectedImportFailures() =>
    [
        new JSException("import failed"),
        new JSDisconnectedException("circuit gone"),
        new TaskCanceledException()
    ];

    public static TheoryData<Exception> ExpectedRegisterFailures() =>
    [
        new JSException("register failed"),
        new JSDisconnectedException("circuit gone"),
        new OperationCanceledException(),
        new TaskCanceledException()
    ];

    public static TheoryData<Exception> ExpectedUnregisterFailures() =>
    [
        new JSDisconnectedException("circuit gone"),
        new TaskCanceledException(),
        new ObjectDisposedException("module")
    ];

    [Fact]
    public async Task DisposeAsync_Overlapped_DetachesOnce()
    {
        CallLog log = new();
        ScriptedModule module = new("module", log);
        KeyboardShortcutService service = CreateService();
        await service.EnsureRegisteredAsync(service.CreateLease(), RuntimeFor(module));
        TaskCompletionSource unregisterGate = CreateGate();
        module.OnUnregister = BlockedUntil(unregisterGate);

        Task first = service.DisposeAsync().AsTask();
        await AwaitBoundedAsync(module.UnregisterStarted);
        Task second = service.DisposeAsync().AsTask();
        unregisterGate.SetResult();
        await AwaitBoundedAsync(first);
        await AwaitBoundedAsync(second);

        Assert.Equal([RegisterCall, UnregisterCall, DisposeCall], log.For("module"));
        Assert.True(module.IsReferenceDisposed());
    }

    [Fact]
    public async Task EnsureRegisteredAsync_AfterServiceDisposed_AttachesNothingAndDisposesOwnModule()
    {
        CallLog log = new();
        ScriptedModule lateModule = new("late", log);
        KeyboardShortcutService service = CreateService();
        KeyboardShortcutLease lease = service.CreateLease();
        await service.DisposeAsync();

        await service.EnsureRegisteredAsync(lease, RuntimeFor(lateModule));

        Assert.Equal([DisposeCall], log.For("late"));
    }

    [Fact]
    public async Task EnsureRegisteredAsync_DeclinePathDisposalPending_DoesNotHoldGate()
    {
        CallLog log = new();
        ScriptedModule staleModule = new("stale", log);
        ScriptedModule currentModule = new("current", log);
        TaskCompletionSource disposeGate = CreateGate();
        staleModule.OnDispose = BlockedUntil(disposeGate);
        KeyboardShortcutService service = CreateService();
        KeyboardShortcutLease stale = service.CreateLease();
        TaskCompletionSource<IJSObjectReference> staleImport = CreateImportGate();
        Task staleEnsure = service.EnsureRegisteredAsync(stale, RuntimeFor(new ValueTask<IJSObjectReference>(staleImport.Task)));
        KeyboardShortcutLease current = service.CreateLease();

        staleImport.SetResult(staleModule.Reference);
        await AwaitBoundedAsync(staleModule.DisposeStarted);
        await AwaitBoundedAsync(service.EnsureRegisteredAsync(current, RuntimeFor(currentModule)));

        Assert.False(staleEnsure.IsCompleted);
        Assert.Equal([RegisterCall], log.For("current"));

        disposeGate.SetResult();
        await AwaitBoundedAsync(staleEnsure);

        Assert.Equal([DisposeCall], log.For("stale"));
    }

    [Theory]
    [MemberData(nameof(ExpectedDisposalFailures))]
    public async Task EnsureRegisteredAsync_DeclinedModuleDisposalFailsExpectedly_CompletesQuietly(Exception failure)
    {
        CallLog log = new();
        ScriptedModule module = new("module", log);
        module.OnDispose = Throwing(failure);
        KeyboardShortcutService service = CreateService();
        KeyboardShortcutLease lease = service.CreateLease();
        TaskCompletionSource<IJSObjectReference> import = CreateImportGate();
        Task ensure = service.EnsureRegisteredAsync(lease, RuntimeFor(new ValueTask<IJSObjectReference>(import.Task)));
        await service.UnregisterAsync(lease);

        import.SetResult(module.Reference);
        await AwaitBoundedAsync(ensure);

        Assert.Equal([DisposeCall], log.For("module"));
    }

    [Fact]
    public async Task EnsureRegisteredAsync_DeclinedModuleDisposalFailsUnexpectedly_SurfacesSameInstanceAndReleasesGate()
    {
        CallLog log = new();
        ScriptedModule declinedModule = new("declined", log);
        ScriptedModule followingModule = new("following", log);
        JSException failure = new("module disposal failed");
        declinedModule.OnDispose = Throwing(failure);
        KeyboardShortcutService service = CreateService();
        KeyboardShortcutLease declined = service.CreateLease();
        TaskCompletionSource<IJSObjectReference> import = CreateImportGate();
        Task ensure = service.EnsureRegisteredAsync(declined, RuntimeFor(new ValueTask<IJSObjectReference>(import.Task)));
        await service.UnregisterAsync(declined);

        import.SetResult(declinedModule.Reference);
        var thrown = await Assert.ThrowsAsync<JSException>(() => AwaitBoundedAsync(ensure));
        await AwaitBoundedAsync(service.EnsureRegisteredAsync(service.CreateLease(), RuntimeFor(followingModule)));

        Assert.Same(failure, thrown);
        Assert.Equal([DisposeCall], log.For("declined"));
        Assert.Equal([RegisterCall], log.For("following"));
    }

    [Theory]
    [MemberData(nameof(ExpectedImportFailures))]
    public async Task EnsureRegisteredAsync_ExpectedImportFailure_DoesNotWaitForGate(Exception failure)
    {
        CallLog log = new();
        ScriptedModule holderModule = new("holder", log);
        TaskCompletionSource registerGate = CreateGate();
        holderModule.OnRegister = BlockedUntil(registerGate);
        KeyboardShortcutService service = CreateService();
        Task holderEnsure = service.EnsureRegisteredAsync(service.CreateLease(), RuntimeFor(holderModule));
        await AwaitBoundedAsync(holderModule.RegisterStarted);
        KeyboardShortcutLease failing = service.CreateLease();

        await AwaitBoundedAsync(service.EnsureRegisteredAsync(failing, RuntimeFor(ValueTask.FromException<IJSObjectReference>(failure))));

        Assert.False(holderEnsure.IsCompleted);

        registerGate.SetResult();
        await AwaitBoundedAsync(holderEnsure);
    }

    [Theory]
    [MemberData(nameof(ExpectedImportFailures))]
    public async Task EnsureRegisteredAsync_ExpectedImportFailure_LeavesPublishedSlotAttached(Exception failure)
    {
        CallLog log = new();
        ScriptedModule publishedModule = new("published", log);
        KeyboardShortcutService service = CreateService();
        KeyboardShortcutLease published = service.CreateLease();
        await service.EnsureRegisteredAsync(published, RuntimeFor(publishedModule));

        await service.EnsureRegisteredAsync(service.CreateLease(), RuntimeFor(ValueTask.FromException<IJSObjectReference>(failure)));

        Assert.Equal([RegisterCall], log.For("published"));
        Assert.False(publishedModule.IsReferenceDisposed());

        await service.UnregisterAsync(published);

        Assert.Contains(UnregisterCall, log.For("published"));
    }

    [Fact]
    public async Task EnsureRegisteredAsync_ImportCancelledWithBareOperationCanceledException_ReturnsQuietly()
    {
        KeyboardShortcutService service = CreateService();

        await service.EnsureRegisteredAsync(
            service.CreateLease(),
            RuntimeFor(ValueTask.FromException<IJSObjectReference>(new OperationCanceledException())));
    }

    [Fact]
    public async Task EnsureRegisteredAsync_LeaseReleasedWhileImportPending_DisposesModuleWithoutRegistering()
    {
        CallLog log = new();
        ScriptedModule module = new("module", log);
        KeyboardShortcutService service = CreateService();
        KeyboardShortcutLease lease = service.CreateLease();
        TaskCompletionSource<IJSObjectReference> import = CreateImportGate();
        Task ensure = service.EnsureRegisteredAsync(lease, RuntimeFor(new ValueTask<IJSObjectReference>(import.Task)));
        await service.UnregisterAsync(lease);

        import.SetResult(module.Reference);
        await AwaitBoundedAsync(ensure);

        Assert.Equal([DisposeCall], log.For("module"));
    }

    [Fact]
    public async Task EnsureRegisteredAsync_LeaseReleasedWhileRegisterPending_DetachesAndDisposesNewSlot()
    {
        CallLog log = new();
        ScriptedModule module = new("module", log);
        TaskCompletionSource registerGate = CreateGate();
        module.OnRegister = BlockedUntil(registerGate);
        KeyboardShortcutService service = CreateService();
        KeyboardShortcutLease lease = service.CreateLease();
        Task ensure = service.EnsureRegisteredAsync(lease, RuntimeFor(module));
        await AwaitBoundedAsync(module.RegisterStarted);
        Task unregister = service.UnregisterAsync(lease).AsTask();

        registerGate.SetResult();
        await AwaitBoundedAsync(ensure);
        await AwaitBoundedAsync(unregister);

        Assert.Equal([RegisterCall, UnregisterCall, DisposeCall], log.For("module"));
        Assert.True(module.IsReferenceDisposed());

        await service.UnregisterAsync(lease);
        await service.DisposeAsync();

        Assert.Equal([RegisterCall, UnregisterCall, DisposeCall], log.For("module"));
    }

    [Fact]
    public async Task EnsureRegisteredAsync_LeaseSupersededDuringOwnRegister_PublishesThenIsReplaced()
    {
        CallLog log = new();
        ScriptedModule olderModule = new("older", log);
        ScriptedModule newerModule = new("newer", log);
        TaskCompletionSource registerGate = CreateGate();
        olderModule.OnRegister = BlockedUntil(registerGate);
        KeyboardShortcutService service = CreateService();
        Task olderEnsure = service.EnsureRegisteredAsync(service.CreateLease(), RuntimeFor(olderModule));
        await AwaitBoundedAsync(olderModule.RegisterStarted);
        KeyboardShortcutLease newer = service.CreateLease();
        Task newerEnsure = service.EnsureRegisteredAsync(newer, RuntimeFor(newerModule));

        Assert.Empty(log.For("newer"));

        registerGate.SetResult();
        await AwaitBoundedAsync(olderEnsure);
        await AwaitBoundedAsync(newerEnsure);

        Assert.Equal([$"older:{RegisterCall}", $"newer:{RegisterCall}", $"older:{DisposeCall}"], log.All());
        Assert.True(olderModule.IsReferenceDisposed());
        Assert.False(newerModule.IsReferenceDisposed());

        await service.UnregisterAsync(newer);

        Assert.Equal([RegisterCall, UnregisterCall, DisposeCall], log.For("newer"));
    }

    [Fact]
    public async Task EnsureRegisteredAsync_NewerLeaseReleasedDuringRegister_RetiresBothSlots()
    {
        CallLog log = new();
        ScriptedModule olderModule = new("older", log);
        ScriptedModule newerModule = new("newer", log);
        KeyboardShortcutService service = CreateService();
        KeyboardShortcutLease older = service.CreateLease();
        await service.EnsureRegisteredAsync(older, RuntimeFor(olderModule));
        KeyboardShortcutLease newer = service.CreateLease();
        TaskCompletionSource registerGate = CreateGate();
        newerModule.OnRegister = BlockedUntil(registerGate);
        Task newerEnsure = service.EnsureRegisteredAsync(newer, RuntimeFor(newerModule));
        await AwaitBoundedAsync(newerModule.RegisterStarted);
        Task newerUnregister = service.UnregisterAsync(newer).AsTask();

        registerGate.SetResult();
        await AwaitBoundedAsync(newerEnsure);
        await AwaitBoundedAsync(newerUnregister);
        await service.UnregisterAsync(older);

        Assert.Equal([RegisterCall, UnregisterCall, DisposeCall], log.For("newer"));
        Assert.Equal([RegisterCall, DisposeCall], log.For("older"));
        Assert.True(newerModule.IsReferenceDisposed());
        Assert.True(olderModule.IsReferenceDisposed());
    }

    [Fact]
    public async Task EnsureRegisteredAsync_OlderImportCompletesAfterNewerRegistration_DeclinesAndKeepsNewerAttached()
    {
        CallLog log = new();
        ScriptedModule olderModule = new("older", log);
        ScriptedModule newerModule = new("newer", log);
        KeyboardShortcutService service = CreateService();
        TaskCompletionSource<IJSObjectReference> olderImport = CreateImportGate();
        Task olderEnsure = service.EnsureRegisteredAsync(
            service.CreateLease(),
            RuntimeFor(new ValueTask<IJSObjectReference>(olderImport.Task)));
        KeyboardShortcutLease newer = service.CreateLease();
        await service.EnsureRegisteredAsync(newer, RuntimeFor(newerModule));

        olderImport.SetResult(olderModule.Reference);
        await AwaitBoundedAsync(olderEnsure);

        Assert.Equal([DisposeCall], log.For("older"));
        Assert.Equal([RegisterCall], log.For("newer"));
        Assert.False(newerModule.IsReferenceDisposed());

        await service.UnregisterAsync(newer);

        Assert.Equal([RegisterCall, UnregisterCall, DisposeCall], log.For("newer"));
    }

    [Fact]
    public async Task EnsureRegisteredAsync_PreviousSlotDisposalFails_PublishesNewSlotThenSurfacesFailure()
    {
        CallLog log = new();
        ScriptedModule previousModule = new("previous", log);
        ScriptedModule replacementModule = new("replacement", log);
        JSException failure = new("previous module disposal failed");
        previousModule.OnDispose = Throwing(failure);
        KeyboardShortcutService service = CreateService();
        await service.EnsureRegisteredAsync(service.CreateLease(), RuntimeFor(previousModule));
        KeyboardShortcutLease replacement = service.CreateLease();

        var thrown = await Assert.ThrowsAsync<JSException>(
            () => service.EnsureRegisteredAsync(replacement, RuntimeFor(replacementModule)));

        Assert.Same(failure, thrown);
        Assert.True(previousModule.IsReferenceDisposed());
        Assert.False(replacementModule.IsReferenceDisposed());

        await service.UnregisterAsync(replacement);

        Assert.Equal([RegisterCall, UnregisterCall, DisposeCall], log.For("replacement"));
    }

    [Fact]
    public async Task EnsureRegisteredAsync_RegisterAndUnregisterFailUnexpectedly_SurfacesAggregateWithRegisterFailureFirst()
    {
        CallLog log = new();
        ScriptedModule failingModule = new("failing", log);
        InvalidOperationException registerFailure = new("register failed");
        InvalidOperationException cleanupFailure = new("unregister failed");
        failingModule.OnRegister = Throwing(registerFailure);
        failingModule.OnUnregister = Throwing(cleanupFailure);
        KeyboardShortcutService service = CreateService();

        var thrown = await Assert.ThrowsAsync<AggregateException>(
            () => service.EnsureRegisteredAsync(service.CreateLease(), RuntimeFor(failingModule)));

        Assert.Equal([registerFailure, cleanupFailure], thrown.InnerExceptions);
        Assert.Equal([RegisterCall, UnregisterCall, DisposeCall], log.For("failing"));
        Assert.True(failingModule.IsReferenceDisposed());
    }

    [Fact]
    public async Task EnsureRegisteredAsync_RegisterFailsExpectedlyAndUnregisterFailsUnexpectedly_SurfacesOnlyCleanupFailure()
    {
        CallLog log = new();
        ScriptedModule failingModule = new("failing", log);
        InvalidOperationException cleanupFailure = new("unregister failed");
        failingModule.OnRegister = Throwing(new JSException("register failed"));
        failingModule.OnUnregister = Throwing(cleanupFailure);
        KeyboardShortcutService service = CreateService();

        var thrown = await Assert.ThrowsAsync<InvalidOperationException>(
            () => service.EnsureRegisteredAsync(service.CreateLease(), RuntimeFor(failingModule)));

        Assert.Same(cleanupFailure, thrown);
        Assert.Equal([RegisterCall, UnregisterCall, DisposeCall], log.For("failing"));
        Assert.True(failingModule.IsReferenceDisposed());
    }

    [Theory]
    [MemberData(nameof(ExpectedRegisterFailures))]
    public async Task EnsureRegisteredAsync_RegisterFailsExpectedly_CleansUpAndEndsUnregistered(Exception failure)
    {
        CallLog log = new();
        ScriptedModule previousModule = new("previous", log);
        ScriptedModule failingModule = new("failing", log);
        failingModule.OnRegister = Throwing(failure);
        KeyboardShortcutService service = CreateService();
        await service.EnsureRegisteredAsync(service.CreateLease(), RuntimeFor(previousModule));

        await service.EnsureRegisteredAsync(service.CreateLease(), RuntimeFor(failingModule));
        await service.DisposeAsync();

        Assert.Equal([RegisterCall, UnregisterCall, DisposeCall], log.For("failing"));
        Assert.Equal([RegisterCall, DisposeCall], log.For("previous"));
        Assert.True(failingModule.IsReferenceDisposed());
        Assert.True(previousModule.IsReferenceDisposed());
    }

    [Fact]
    public async Task EnsureRegisteredAsync_RegisterFailsUnexpectedly_CleansUpThenSurfacesSameInstance()
    {
        CallLog log = new();
        ScriptedModule previousModule = new("previous", log);
        ScriptedModule failingModule = new("failing", log);
        ScriptedModule followingModule = new("following", log);
        InvalidOperationException failure = new("register failed");
        failingModule.OnRegister = Throwing(failure);
        KeyboardShortcutService service = CreateService();
        await service.EnsureRegisteredAsync(service.CreateLease(), RuntimeFor(previousModule));

        var thrown = await Assert.ThrowsAsync<InvalidOperationException>(
            () => service.EnsureRegisteredAsync(service.CreateLease(), RuntimeFor(failingModule)));
        await AwaitBoundedAsync(service.EnsureRegisteredAsync(service.CreateLease(), RuntimeFor(followingModule)));

        Assert.Same(failure, thrown);
        Assert.Equal([RegisterCall, UnregisterCall, DisposeCall], log.For("failing"));
        Assert.Equal([RegisterCall, DisposeCall], log.For("previous"));
        Assert.Equal([RegisterCall], log.For("following"));
        Assert.True(failingModule.IsReferenceDisposed());
        Assert.True(previousModule.IsReferenceDisposed());
    }

    [Fact]
    public async Task EnsureRegisteredAsync_UnexpectedImportFailure_PropagatesSameInstance()
    {
        InvalidOperationException failure = new("import failed");
        KeyboardShortcutService service = CreateService();

        var thrown = await Assert.ThrowsAsync<InvalidOperationException>(
            () => service.EnsureRegisteredAsync(
                service.CreateLease(),
                RuntimeFor(ValueTask.FromException<IJSObjectReference>(failure))));

        Assert.Same(failure, thrown);
    }

    [Fact]
    public async Task UnregisterAsync_BareOperationCanceledFromUnregister_IsSwallowed()
    {
        CallLog log = new();
        ScriptedModule module = new("module", log);
        module.OnUnregister = Throwing(new OperationCanceledException());
        KeyboardShortcutService service = CreateService();
        KeyboardShortcutLease lease = service.CreateLease();
        await service.EnsureRegisteredAsync(lease, RuntimeFor(module));

        await service.UnregisterAsync(lease);

        Assert.Contains(UnregisterCall, log.For("module"));
    }

    [Fact]
    public async Task UnregisterAsync_BothUnregisterAndModuleDisposalFail_SurfacesAggregateAfterAllSteps()
    {
        CallLog log = new();
        ScriptedModule module = new("module", log);
        ScriptedModule followingModule = new("following", log);
        InvalidOperationException unregisterFailure = new("unregister failed");
        JSException disposalFailure = new("module disposal failed");
        module.OnUnregister = Throwing(unregisterFailure);
        module.OnDispose = Throwing(disposalFailure);
        KeyboardShortcutService service = CreateService();
        KeyboardShortcutLease lease = service.CreateLease();
        await service.EnsureRegisteredAsync(lease, RuntimeFor(module));

        var thrown = await Assert.ThrowsAsync<AggregateException>(() => service.UnregisterAsync(lease).AsTask());
        await AwaitBoundedAsync(service.EnsureRegisteredAsync(service.CreateLease(), RuntimeFor(followingModule)));

        Assert.Equal(2, thrown.InnerExceptions.Count);
        Assert.Contains(unregisterFailure, thrown.InnerExceptions);
        Assert.Contains(disposalFailure, thrown.InnerExceptions);
        Assert.True(module.IsReferenceDisposed());
        Assert.Equal([RegisterCall, UnregisterCall, DisposeCall], log.For("module"));
        Assert.Equal([RegisterCall], log.For("following"));
    }

    [Theory]
    [MemberData(nameof(ExpectedUnregisterFailures))]
    public async Task UnregisterAsync_ExpectedUnregisterFailure_CompletesQuietly(Exception failure)
    {
        CallLog log = new();
        ScriptedModule module = new("module", log);
        module.OnUnregister = Throwing(failure);
        KeyboardShortcutService service = CreateService();
        KeyboardShortcutLease lease = service.CreateLease();
        await service.EnsureRegisteredAsync(lease, RuntimeFor(module));

        await service.UnregisterAsync(lease);

        Assert.Contains(UnregisterCall, log.For("module"));
    }

    [Fact]
    public async Task UnregisterAsync_OwningLease_UnregistersAndDisposesSlot()
    {
        CallLog log = new();
        ScriptedModule module = new("module", log);
        KeyboardShortcutService service = CreateService();
        KeyboardShortcutLease lease = service.CreateLease();
        await service.EnsureRegisteredAsync(lease, RuntimeFor(module));

        await service.UnregisterAsync(lease);

        Assert.Equal([RegisterCall, UnregisterCall, DisposeCall], log.For("module"));
        Assert.True(module.IsReferenceDisposed());
    }

    [Fact]
    public async Task UnregisterAsync_StaleLeaseAfterReplacement_LeavesNewerRegistrationAttached()
    {
        CallLog log = new();
        ScriptedModule olderModule = new("older", log);
        ScriptedModule newerModule = new("newer", log);
        KeyboardShortcutService service = CreateService();
        KeyboardShortcutLease older = service.CreateLease();
        await service.EnsureRegisteredAsync(older, RuntimeFor(olderModule));
        KeyboardShortcutLease newer = service.CreateLease();
        await service.EnsureRegisteredAsync(newer, RuntimeFor(newerModule));

        await service.UnregisterAsync(older);

        Assert.Equal([RegisterCall, DisposeCall], log.For("older"));
        Assert.Equal([RegisterCall], log.For("newer"));
        Assert.False(newerModule.IsReferenceDisposed());

        await service.UnregisterAsync(newer);

        Assert.Equal([RegisterCall, UnregisterCall, DisposeCall], log.For("newer"));
    }

    [Fact]
    public async Task UnregisterAsync_UnexpectedUnregisterFailure_DisposesRefAndModuleThenSurfacesSameInstance()
    {
        CallLog log = new();
        ScriptedModule module = new("module", log);
        JSException failure = new("unregister failed");
        module.OnUnregister = Throwing(failure);
        KeyboardShortcutService service = CreateService();
        KeyboardShortcutLease lease = service.CreateLease();
        await service.EnsureRegisteredAsync(lease, RuntimeFor(module));

        var thrown = await Assert.ThrowsAsync<JSException>(() => service.UnregisterAsync(lease).AsTask());

        Assert.Same(failure, thrown);
        Assert.Equal([RegisterCall, UnregisterCall, DisposeCall], log.For("module"));
        Assert.True(module.IsReferenceDisposed());
    }

    private static Task AwaitBoundedAsync(Task task) =>
        task.WaitAsync(s_handshakeTimeout, TestContext.Current.CancellationToken);

    private static Task AwaitBoundedAsync(ValueTask task) => AwaitBoundedAsync(task.AsTask());

    private static Func<ValueTask> BlockedUntil(TaskCompletionSource gate) => () => new ValueTask(gate.Task);

    private static TaskCompletionSource CreateGate() => new(TaskCreationOptions.RunContinuationsAsynchronously);

    private static TaskCompletionSource<IJSObjectReference> CreateImportGate() =>
        new(TaskCreationOptions.RunContinuationsAsynchronously);

    private static KeyboardShortcutService CreateService() => new(
        Substitute.For<IMenuActionService>(),
        Substitute.For<IModalCoordinator>(),
        Substitute.For<IFindCoordinator>(),
        Substitute.For<ISettingsService>());

    private static IJSRuntime RuntimeFor(ValueTask<IJSObjectReference> import)
    {
        IJSRuntime runtime = Substitute.For<IJSRuntime>();
        runtime.ReturnsForAll<ValueTask<IJSObjectReference>>(_ => import);

        return runtime;
    }

    private static IJSRuntime RuntimeFor(ScriptedModule module) => RuntimeFor(ValueTask.FromResult(module.Reference));

    private static Func<ValueTask> Throwing(Exception exception) => () => ValueTask.FromException(exception);

    private sealed class CallLog
    {
        private readonly List<(string Module, string Call)> _entries = [];
        private readonly Lock _lock = new();

        public string[] All()
        {
            lock (_lock)
            {
                return [.. _entries.Select(entry => $"{entry.Module}:{entry.Call}")];
            }
        }

        public string[] For(string module)
        {
            lock (_lock)
            {
                return [.. _entries.Where(entry => entry.Module == module).Select(entry => entry.Call)];
            }
        }

        public void Record(string module, string call)
        {
            lock (_lock)
            {
                _entries.Add((module, call));
            }
        }
    }

    private sealed class ScriptedModule
    {
        private readonly TaskCompletionSource _disposeStarted = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly CallLog _log;
        private readonly string _name;
        private readonly TaskCompletionSource _registerStarted = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource _unregisterStarted = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public ScriptedModule(string name, CallLog log)
        {
            _name = name;
            _log = log;
            Reference = Substitute.For<IJSObjectReference>();
            Reference.ReturnsForAll<ValueTask<IJSVoidResult>>(
                call => InvokeAsync(call.ArgAt<string>(0), call.ArgAt<object?[]?>(1)));
            Reference.DisposeAsync().Returns(_ => DisposeAsync());
        }

        public Task DisposeStarted => _disposeStarted.Task;

        public Func<ValueTask>? OnDispose { get; set; }

        public Func<ValueTask>? OnRegister { get; set; }

        public Func<ValueTask>? OnUnregister { get; set; }

        public IJSObjectReference Reference { get; }

        public Task RegisterStarted => _registerStarted.Task;

        public Task UnregisterStarted => _unregisterStarted.Task;

        private DotNetObjectReference<KeyboardShortcutService>? RegisteredReference { get; set; }

        public bool IsReferenceDisposed()
        {
            Assert.NotNull(RegisteredReference);

            try
            {
                _ = RegisteredReference.Value;

                return false;
            }
            catch (ObjectDisposedException)
            {
                return true;
            }
        }

        private static async ValueTask RunAsync(Func<ValueTask>? behavior)
        {
            if (behavior is not null)
            {
                await behavior();
            }
        }

        private async ValueTask DisposeAsync()
        {
            _log.Record(_name, DisposeCall);
            _disposeStarted.TrySetResult();

            await RunAsync(OnDispose);
        }

        private async ValueTask<IJSVoidResult> InvokeAsync(string identifier, object?[]? arguments)
        {
            _log.Record(_name, identifier);

            switch (identifier)
            {
                case RegisterCall:
                    if (arguments is [DotNetObjectReference<KeyboardShortcutService> reference])
                    {
                        RegisteredReference = reference;
                    }

                    _registerStarted.TrySetResult();
                    await RunAsync(OnRegister);
                    break;

                case UnregisterCall:
                    _unregisterStarted.TrySetResult();
                    await RunAsync(OnUnregister);
                    break;
            }

            return default!;
        }
    }
}
