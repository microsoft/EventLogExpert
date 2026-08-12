// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.UI.Alerts;
using EventLogExpert.UI.Modal;
using Microsoft.AspNetCore.Components;
using NSubstitute;

namespace EventLogExpert.UI.Tests.Modal;

public sealed class ModalCoordinatorCloseTests
{
    [Fact]
    public void GetActiveModalScope_CriticalModal_ReturnsCritical()
    {
        var service = new ModalService();
        using var sut = new ModalCoordinator(service);
        _ = sut.PushAsync<DummyModal, bool>();
        ModalId id = service.ActiveModalId;
        sut.RegisterModal(TestRegistration(id, scope: ModalScope.Critical));

        Assert.Equal(ModalScope.Critical, sut.GetActiveModalScope());
    }

    [Fact]
    public void GetActiveModalScope_NoModal_ReturnsNull()
    {
        var service = new ModalService();
        using var sut = new ModalCoordinator(service);

        Assert.Null(sut.GetActiveModalScope());
    }

    [Fact]
    public void GetActiveModalScope_StaleRegistrationAfterCancel_ReturnsNull()
    {
        var service = new ModalService();
        using var sut = new ModalCoordinator(service);
        _ = sut.PushAsync<DummyModal, bool>();
        ModalId id = service.ActiveModalId;
        sut.RegisterModal(TestRegistration(id, scope: ModalScope.Critical));
        service.CancelActive();

        ModalScope? scope = sut.GetActiveModalScope();

        Assert.Null(scope);
    }

    [Fact]
    public void GetActiveModalScope_StandardModal_ReturnsStandard()
    {
        var service = new ModalService();
        using var sut = new ModalCoordinator(service);
        _ = sut.PushAsync<DummyModal, bool>();
        ModalId id = service.ActiveModalId;
        sut.RegisterModal(TestRegistration(id, scope: ModalScope.Standard));

        Assert.Equal(ModalScope.Standard, sut.GetActiveModalScope());
    }

    [Fact]
    public void ModalRegistration_NullRequestClose_ThrowsArgumentNullException()
    {
        Assert.Throws<ArgumentNullException>(() => new ModalRegistration(new ModalId(1L), null!, ModalScope.Standard, inlineAlertHost: null));
    }

    [Fact]
    public async Task PushAsync_ActiveModalAcceptsPreemption_ReturnsOpened()
    {
        var service = new ModalService();
        using var sut = new ModalCoordinator(service);
        _ = sut.PushAsync<DummyModal, bool>();
        ModalId firstId = service.ActiveModalId;
        sut.RegisterModal(TestRegistration(firstId, _ => Task.FromResult(true)));

        Task<ModalOpenResult<bool>> secondShow = sut.PushAsync<OtherModal, bool>();
        ModalId secondId = service.ActiveModalId;
        sut.ForceCloseActive();

        ModalOpenResult<bool> result = await secondShow;
        Assert.True(result.WasOpened);
        Assert.NotEqual(firstId, secondId);
    }

    [Fact]
    public async Task PushAsync_ActiveModalVetoesPreemption_ReturnsNotOpened()
    {
        var service = new ModalService();
        using var sut = new ModalCoordinator(service);
        _ = sut.PushAsync<DummyModal, bool>();
        ModalId firstId = service.ActiveModalId;
        sut.RegisterModal(TestRegistration(firstId, _ => Task.FromResult(false)));

        ModalOpenResult<bool> result = await sut.PushAsync<OtherModal, bool>();

        Assert.False(result.WasOpened);
        Assert.Equal(default, result.Result);
        Assert.Equal(firstId, service.ActiveModalId);
    }

    [Fact]
    public void RegisterModal_ModalIdNone_RejectsRegistrationToAvoidGhostState()
    {
        var service = new ModalService();
        using var sut = new ModalCoordinator(service);

        sut.RegisterModal(TestRegistration(ModalId.None));

        Assert.Null(sut.GetActiveModalScope());
        Assert.False(sut.TryGetInlineAlertHost(out _));
    }

    [Fact]
    public async Task RequestCloseActiveAsync_ConcurrentCalls_ShareResult()
    {
        var service = new ModalService();
        using var sut = new ModalCoordinator(service);
        _ = sut.PushAsync<DummyModal, bool>();
        ModalId id = service.ActiveModalId;
        var handlerReached = new TaskCompletionSource();
        var releaseHandler = new TaskCompletionSource<bool>();
        sut.RegisterModal(TestRegistration(id, async _ =>
        {
            handlerReached.SetResult();
            return await releaseHandler.Task;
        }));

        Task<bool> firstCall = sut.RequestCloseActiveAsync(ModalCloseReason.UserDismiss);
        await handlerReached.Task;
        Task<bool> secondCall = sut.RequestCloseActiveAsync(ModalCloseReason.EscKey);
        releaseHandler.SetResult(true);

        bool firstResult = await firstCall;
        bool secondResult = await secondCall;
        Assert.True(firstResult);
        Assert.True(secondResult);
    }

    [Fact]
    public async Task RequestCloseActiveAsync_CriticalScopeAndOtherModalActivation_RejectsWithoutCallingHandler()
    {
        var service = new ModalService();
        using var sut = new ModalCoordinator(service);
        _ = sut.PushAsync<DummyModal, bool>();
        ModalId id = service.ActiveModalId;
        int handlerCallCount = 0;
        sut.RegisterModal(TestRegistration(id, _ =>
        {
            handlerCallCount++;
            return Task.FromResult(true);
        }, ModalScope.Critical));

        bool result = await sut.RequestCloseActiveAsync(ModalCloseReason.OtherModalActivation);

        Assert.False(result);
        Assert.Equal(0, handlerCallCount);
    }

    [Fact]
    public async Task RequestCloseActiveAsync_CriticalScopeAndUserDismiss_DelegatesToHandler()
    {
        var service = new ModalService();
        using var sut = new ModalCoordinator(service);
        _ = sut.PushAsync<DummyModal, bool>();
        ModalId id = service.ActiveModalId;
        int handlerCallCount = 0;
        sut.RegisterModal(TestRegistration(id, _ =>
        {
            handlerCallCount++;
            return Task.FromResult(true);
        }, ModalScope.Critical));

        bool result = await sut.RequestCloseActiveAsync(ModalCloseReason.UserDismiss);

        Assert.True(result);
        Assert.Equal(1, handlerCallCount);
    }

    [Fact]
    public async Task RequestCloseActiveAsync_CriticalScope_RejectsOtherModalActivationEvenWithEscInFlight()
    {
        var service = new ModalService();
        using var sut = new ModalCoordinator(service);
        _ = sut.PushAsync<DummyModal, bool>();
        ModalId id = service.ActiveModalId;
        var handlerReached = new TaskCompletionSource();
        var releaseHandler = new TaskCompletionSource<bool>();
        sut.RegisterModal(TestRegistration(id, async _ =>
        {
            handlerReached.SetResult();
            return await releaseHandler.Task;
        }, ModalScope.Critical));

        Task<bool> escCall = sut.RequestCloseActiveAsync(ModalCloseReason.EscKey);
        await handlerReached.Task;

        bool omaResult = await sut.RequestCloseActiveAsync(ModalCloseReason.OtherModalActivation);

        Assert.False(omaResult);

        releaseHandler.SetResult(true);
        await escCall;
    }

    [Fact]
    public async Task RequestCloseActiveAsync_HandlerAccepts_ReturnsTrue()
    {
        var service = new ModalService();
        using var sut = new ModalCoordinator(service);
        _ = sut.PushAsync<DummyModal, bool>();
        ModalId id = service.ActiveModalId;
        sut.RegisterModal(TestRegistration(id, _ => Task.FromResult(true)));

        bool result = await sut.RequestCloseActiveAsync(ModalCloseReason.UserDismiss);

        Assert.True(result);
    }

    [Fact]
    public async Task RequestCloseActiveAsync_HandlerCanceled_AwaitersReceiveAccepted()
    {
        var service = new ModalService();
        using var sut = new ModalCoordinator(service);
        _ = sut.PushAsync<DummyModal, bool>();
        ModalId id = service.ActiveModalId;
        sut.RegisterModal(TestRegistration(id, _ => throw new OperationCanceledException()));

        bool result = await sut.RequestCloseActiveAsync(ModalCloseReason.UserDismiss);

        Assert.True(result);
    }

    [Fact]
    public async Task RequestCloseActiveAsync_HandlerThrows_CoalescedAwaitersSeeException()
    {
        var service = new ModalService();
        using var sut = new ModalCoordinator(service);
        _ = sut.PushAsync<DummyModal, bool>();
        ModalId id = service.ActiveModalId;
        var handlerReached = new TaskCompletionSource();
        var releaseHandler = new TaskCompletionSource<bool>();
        sut.RegisterModal(TestRegistration(id, async _ =>
        {
            handlerReached.SetResult();
            await releaseHandler.Task;
            throw new InvalidOperationException("handler failure");
        }));

        Task<bool> firstCall = sut.RequestCloseActiveAsync(ModalCloseReason.UserDismiss);
        await handlerReached.Task;
        Task<bool> secondCall = sut.RequestCloseActiveAsync(ModalCloseReason.EscKey);

        releaseHandler.SetResult(true);

        await Assert.ThrowsAsync<InvalidOperationException>(() => firstCall);
        await Assert.ThrowsAsync<InvalidOperationException>(() => secondCall);
    }

    [Fact]
    public async Task RequestCloseActiveAsync_HandlerThrows_PropagatesException()
    {
        var service = new ModalService();
        using var sut = new ModalCoordinator(service);
        _ = sut.PushAsync<DummyModal, bool>();
        ModalId id = service.ActiveModalId;
        sut.RegisterModal(TestRegistration(id, _ => throw new InvalidOperationException("handler failure")));

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => sut.RequestCloseActiveAsync(ModalCloseReason.UserDismiss));

        Assert.Equal("handler failure", ex.Message);

        sut.UnregisterModal(id);
        bool followup = await sut.RequestCloseActiveAsync(ModalCloseReason.UserDismiss);
        Assert.True(followup);
    }

    [Fact]
    public async Task RequestCloseActiveAsync_HandlerVetoes_ReturnsFalse()
    {
        var service = new ModalService();
        using var sut = new ModalCoordinator(service);
        _ = sut.PushAsync<DummyModal, bool>();
        ModalId id = service.ActiveModalId;
        sut.RegisterModal(TestRegistration(id, _ => Task.FromResult(false)));

        bool result = await sut.RequestCloseActiveAsync(ModalCloseReason.UserDismiss);

        Assert.False(result);
        Assert.NotNull(sut.ActiveSession);
    }

    [Fact]
    public async Task RequestCloseActiveAsync_HandlerVetoes_SecondCallCanSucceedAfterHandlerSwitches()
    {
        var service = new ModalService();
        using var sut = new ModalCoordinator(service);
        _ = sut.PushAsync<DummyModal, bool>();
        ModalId id = service.ActiveModalId;
        bool acceptNext = false;
        sut.RegisterModal(TestRegistration(id, _ => Task.FromResult(acceptNext)));

        bool first = await sut.RequestCloseActiveAsync(ModalCloseReason.UserDismiss);
        acceptNext = true;
        bool second = await sut.RequestCloseActiveAsync(ModalCloseReason.UserDismiss);

        Assert.False(first);
        Assert.True(second);
    }

    [Fact]
    public async Task RequestCloseActiveAsync_InitWindowOtherModalActivation_RejectsToProtectScopePolicy()
    {
        var service = new ModalService();
        using var sut = new ModalCoordinator(service);
        _ = sut.PushAsync<DummyModal, bool>();
        Assert.NotEqual(ModalId.None, service.ActiveModalId);
        Assert.Null(sut.GetActiveModalScope());

        bool result = await sut.RequestCloseActiveAsync(ModalCloseReason.OtherModalActivation);

        Assert.False(result);
    }

    [Fact]
    public async Task RequestCloseActiveAsync_InitWindowUserDismiss_ReturnsTrue()
    {
        var service = new ModalService();
        using var sut = new ModalCoordinator(service);
        _ = sut.PushAsync<DummyModal, bool>();
        Assert.NotEqual(ModalId.None, service.ActiveModalId);
        Assert.Null(sut.GetActiveModalScope());

        bool result = await sut.RequestCloseActiveAsync(ModalCloseReason.UserDismiss);

        Assert.True(result);
    }

    [Fact]
    public async Task RequestCloseActiveAsync_NoActiveModal_ReturnsTrue()
    {
        var service = new ModalService();
        using var sut = new ModalCoordinator(service);

        bool result = await sut.RequestCloseActiveAsync(ModalCloseReason.UserDismiss);

        Assert.True(result);
    }

    [Fact]
    public async Task RequestCloseActiveAsync_StaleCriticalRegistrationAfterCancel_DoesNotVetoOtherModalActivation()
    {
        var service = new ModalService();
        using var sut = new ModalCoordinator(service);
        _ = sut.PushAsync<DummyModal, bool>();
        ModalId id = service.ActiveModalId;
        int handlerCallCount = 0;
        sut.RegisterModal(TestRegistration(id, _ =>
        {
            handlerCallCount++;
            return Task.FromResult(true);
        }, ModalScope.Critical));
        service.CancelActive();

        bool result = await sut.RequestCloseActiveAsync(ModalCloseReason.OtherModalActivation);

        Assert.True(result);
        Assert.Equal(0, handlerCallCount);
    }

    [Fact]
    public async Task RequestCloseActiveAsync_StaleInFlightCloseFromForceClosedModal_RunsNewModalsOwnVeto()
    {
        var service = new ModalService();
        using var sut = new ModalCoordinator(service);

        // Modal A opens and its close handler blocks in-flight.
        _ = sut.PushAsync<DummyModal, bool>();
        ModalId idA = service.ActiveModalId;
        var aHandlerReached = new TaskCompletionSource();
        var releaseA = new TaskCompletionSource<bool>();
        sut.RegisterModal(TestRegistration(idA, async _ =>
        {
            aHandlerReached.SetResult();
            return await releaseA.Task;
        }));

        Task<bool> closeA = sut.RequestCloseActiveAsync(ModalCloseReason.UserDismiss);
        await aHandlerReached.Task;

        // A is force-closed while its close is still in-flight; B opens in its place.
        sut.ForceCloseActive();
        _ = sut.PushAsync<OtherModal, bool>();
        ModalId idB = service.ActiveModalId;
        Assert.NotEqual(idA, idB);

        int bHandlerCalls = 0;
        sut.RegisterModal(TestRegistration(idB, _ =>
        {
            bHandlerCalls++;
            return Task.FromResult(false);
        }));

        // B's close must invoke B's own veto, not coalesce onto A's stale in-flight close.
        bool bClose = await sut.RequestCloseActiveAsync(ModalCloseReason.UserDismiss);

        Assert.Equal(1, bHandlerCalls);
        Assert.False(bClose);

        releaseA.SetResult(true);
        await closeA;
    }

    [Fact]
    public void TryGetInlineAlertHost_StaleRegistrationAfterCancel_ReturnsFalse()
    {
        var service = new ModalService();
        using var sut = new ModalCoordinator(service);
        _ = sut.PushAsync<DummyModal, bool>();
        ModalId id = service.ActiveModalId;
        var host = Substitute.For<IInlineAlertHost>();
        sut.RegisterModal(new ModalRegistration(id, _ => Task.FromResult(true), ModalScope.Standard, host));
        service.CancelActive();

        bool found = sut.TryGetInlineAlertHost(out IInlineAlertHost? resolved);

        Assert.False(found);
        Assert.Null(resolved);
    }

    private static ModalRegistration TestRegistration(
        ModalId id,
        Func<ModalCloseRequest, Task<bool>>? requestClose = null,
        ModalScope scope = ModalScope.Standard,
        IInlineAlertHost? host = null) =>
        new(id, requestClose ?? (_ => Task.FromResult(true)), scope, host);

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
