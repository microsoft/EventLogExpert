// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.UI.Alerts;
using EventLogExpert.UI.Modal;
using Microsoft.AspNetCore.Components;
using NSubstitute;

namespace EventLogExpert.UI.Tests.Modal;

public sealed class ModalCoordinatorTests
{
    [Fact]
    public async Task ActiveSession_AfterComplete_IsNullBeforeAwaiterResumes()
    {
        var service = new ModalService();
        using var sut = new ModalCoordinator(service);
        Task<ModalOpenResult<bool>> showTask = sut.PushAsync<DummyModal, bool>();
        ModalId activeId = service.ActiveModalId;
        Assert.NotNull(sut.ActiveSession);

        var resumeSnapshot = new TaskCompletionSource<ModalSession?>();
        Task observer = Task.Run(async () =>
        {
            await showTask;
            resumeSnapshot.SetResult(sut.ActiveSession);
        }, TestContext.Current.CancellationToken);

        service.Complete(activeId, true);

        ModalSession? sessionAtResume = await resumeSnapshot.Task;
        Assert.Null(sessionAtResume);
        await observer;
        ModalOpenResult<bool> result = await showTask;
        Assert.True(result.WasOpened);
        Assert.True(result.Result);
        Assert.Null(sut.ActiveSession);
    }

    [Fact]
    public async Task ActiveSession_AfterPush_ReflectsService()
    {
        var service = new ModalService();
        using var sut = new ModalCoordinator(service);

        _ = sut.PushAsync<DummyModal, bool>();

        Assert.NotNull(sut.ActiveSession);
        Assert.Equal(service.ActiveModalId, sut.ActiveSession.Id);
        Assert.Equal(typeof(DummyModal), sut.ActiveSession.ComponentType);

        await Task.CompletedTask;
    }

    [Fact]
    public void ActiveSession_NoActiveModal_ReturnsNull()
    {
        var service = new ModalService();
        using var sut = new ModalCoordinator(service);

        Assert.Null(sut.ActiveSession);
    }

    [Fact]
    public void Complete_StaleId_NoOps_StackUnchanged()
    {
        var service = new ModalService();
        using var sut = new ModalCoordinator(service);
        _ = sut.PushAsync<DummyModal, bool>();
        long activeId = service.ActiveModalId.Value;

        sut.Complete(new ModalId(activeId + 99), true);

        Assert.NotNull(sut.ActiveSession);
        Assert.Equal(new ModalId(activeId), sut.ActiveSession.Id);
    }

    [Fact]
    public void Dispose_UnhooksServiceStateChanged_NoLeak()
    {
        var service = new ModalService();
        var sut = new ModalCoordinator(service);
        int fireCount = 0;
        sut.StateChanged += () => fireCount++;

        sut.Dispose();
        _ = service.Show<DummyModal, bool>();

        Assert.Equal(0, fireCount);
        service.CancelActive();
    }

    [Fact]
    public void Dispose_WhenCalledTwice_IsIdempotent()
    {
        var sut = new ModalCoordinator(new ModalService());

        sut.Dispose();
        sut.Dispose();

    }

    [Fact]
    public async Task PushAsync_PreemptsPriorModal_CoordinatorMirrorsLatest()
    {
        var service = new ModalService();
        using var sut = new ModalCoordinator(service);

        Task<ModalOpenResult<bool>> firstShow = sut.PushAsync<DummyModal, bool>();
        ModalId firstId = service.ActiveModalId;
        sut.RegisterModal(TestRegistration(firstId, _ => Task.FromResult(true)));
        Task<ModalOpenResult<bool>> secondShow = sut.PushAsync<OtherModal, bool>();
        ModalId secondId = service.ActiveModalId;

        Assert.NotEqual(firstId, secondId);
        Assert.NotNull(sut.ActiveSession);
        Assert.Equal(secondId, sut.ActiveSession.Id);
        Assert.Equal(typeof(OtherModal), sut.ActiveSession.ComponentType);
        ModalOpenResult<bool> firstResult = await firstShow;
        Assert.True(firstResult.WasOpened);
        Assert.False(firstResult.Result);

        sut.ForceCloseActive();
        ModalOpenResult<bool> secondResult = await secondShow;
        Assert.True(secondResult.WasOpened);
        Assert.False(secondResult.Result);
    }

    [Fact]
    public async Task PushAsync_WhenCriticalModalActive_RejectsReplacement()
    {
        var service = new ModalService();
        using var sut = new ModalCoordinator(service);

        Task<ModalOpenResult<bool>> firstShow = sut.PushAsync<DummyModal, bool>();
        ModalId firstId = service.ActiveModalId;

        sut.RegisterModal(TestRegistration(firstId, _ => Task.FromResult(true), ModalScope.Critical));

        Task<ModalOpenResult<bool>> secondShow = sut.PushAsync<OtherModal, bool>();
        ModalOpenResult<bool> secondResult = await secondShow;

        Assert.False(secondResult.WasOpened);

        sut.ForceCloseActive();
        ModalOpenResult<bool> firstResult = await firstShow;
        Assert.True(firstResult.WasOpened);
        Assert.False(firstResult.Result);
    }

    [Fact]
    public void RegisterModal_ActiveId_RegistersAndInlineAlertHostIsReadable()
    {
        var service = new ModalService();
        using var sut = new ModalCoordinator(service);
        _ = sut.PushAsync<DummyModal, bool>();
        ModalId id = service.ActiveModalId;
        var host = Substitute.For<IInlineAlertHost>();

        sut.RegisterModal(TestRegistration(id, host));

        Assert.True(sut.TryGetInlineAlertHost(out var resolved));
        Assert.Same(host, resolved);
    }

    [Fact]
    public void RegisterModal_StaleId_IsNoOp()
    {
        var service = new ModalService();
        using var sut = new ModalCoordinator(service);
        var host = Substitute.For<IInlineAlertHost>();

        sut.RegisterModal(TestRegistration(new ModalId(999L), host));

        Assert.False(sut.TryGetInlineAlertHost(out _));
    }

    [Fact]
    public void StateChanged_OnPush_FiresAfterMirrorUpdate()
    {
        var service = new ModalService();
        using var sut = new ModalCoordinator(service);
        int fireCount = 0;
        ModalSession? observedAtFire = null;
        sut.StateChanged += () =>
        {
            fireCount++;
            observedAtFire = sut.ActiveSession;
        };

        _ = sut.PushAsync<DummyModal, bool>();

        Assert.Equal(1, fireCount);
        Assert.NotNull(observedAtFire);
        Assert.Equal(service.ActiveModalId, observedAtFire.Id);
    }

    [Fact]
    public void TryGetInlineAlertHost_AfterPushPreemptsPrior_ReturnsFalseAndClearsStaleHost()
    {
        var service = new ModalService();
        using var sut = new ModalCoordinator(service);
        _ = sut.PushAsync<DummyModal, bool>();
        ModalId firstId = service.ActiveModalId;
        var host = Substitute.For<IInlineAlertHost>();
        sut.RegisterModal(TestRegistration(firstId, host));
        Assert.True(sut.TryGetInlineAlertHost(out _));

        _ = sut.PushAsync<OtherModal, bool>();

        Assert.False(sut.TryGetInlineAlertHost(out _));

        sut.ForceCloseActive();
    }

    [Fact]
    public void UnregisterModal_MatchingId_ClearsRegistration()
    {
        var service = new ModalService();
        using var sut = new ModalCoordinator(service);
        _ = sut.PushAsync<DummyModal, bool>();
        ModalId id = service.ActiveModalId;
        sut.RegisterModal(TestRegistration(id, Substitute.For<IInlineAlertHost>()));

        sut.UnregisterModal(id);

        Assert.False(sut.TryGetInlineAlertHost(out _));
        Assert.Null(sut.GetActiveModalScope());
    }

    [Fact]
    public void UnregisterModal_StaleId_IsNoOp()
    {
        var service = new ModalService();
        using var sut = new ModalCoordinator(service);
        _ = sut.PushAsync<DummyModal, bool>();
        ModalId id = service.ActiveModalId;
        var host = Substitute.For<IInlineAlertHost>();
        sut.RegisterModal(TestRegistration(id, host));

        sut.UnregisterModal(new ModalId(id.Value + 99));

        Assert.True(sut.TryGetInlineAlertHost(out var resolved));
        Assert.Same(host, resolved);
    }

    private static ModalRegistration TestRegistration(ModalId id, IInlineAlertHost? host = null, ModalScope scope = ModalScope.Standard) =>
        new(id, _ => Task.FromResult(true), scope, host);

    private static ModalRegistration TestRegistration(ModalId id, Func<ModalCloseRequest, Task<bool>> requestClose, ModalScope scope = ModalScope.Standard) =>
        new(id, requestClose, scope, inlineAlertHost: null);

    private sealed class DummyModal : IComponent
    {
        public void Attach(RenderHandle renderHandle) { }

        public Task SetParametersAsync(ParameterView parameters) => Task.CompletedTask;
    }

    private sealed class OtherModal : IComponent
    {
        public void Attach(RenderHandle renderHandle) { }

        public Task SetParametersAsync(ParameterView parameters) => Task.CompletedTask;
    }
}
