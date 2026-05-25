// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Runtime.Alerts;
using EventLogExpert.Runtime.Modal;
using Microsoft.AspNetCore.Components;
using NSubstitute;

namespace EventLogExpert.Runtime.Tests.Modal;

public sealed class ModalCoordinatorCloseTests
{
    [Fact]
    public void GetActiveModalScope_CriticalModal_ReturnsCritical()
    {
        // Arrange
        var service = new ModalService();
        using var sut = new ModalCoordinator(service);
        _ = sut.PushAsync<DummyModal, bool>();
        ModalId id = service.ActiveModalId;
        sut.RegisterModal(TestRegistration(id, scope: ModalScope.Critical));

        // Act + Assert
        Assert.Equal(ModalScope.Critical, sut.GetActiveModalScope());
    }

    [Fact]
    public void GetActiveModalScope_NoModal_ReturnsNull()
    {
        // Arrange
        var service = new ModalService();
        using var sut = new ModalCoordinator(service);

        // Act + Assert
        Assert.Null(sut.GetActiveModalScope());
    }

    [Fact]
    public void GetActiveModalScope_StaleRegistrationAfterCancel_ReturnsNull()
    {
        // Arrange — register a modal, then cancel via the service (ModalService publishes ActiveModalId changes before
        // firing StateChanged, so a read can race the backstop in OnModalServiceStateChanged).
        var service = new ModalService();
        using var sut = new ModalCoordinator(service);
        _ = sut.PushAsync<DummyModal, bool>();
        ModalId id = service.ActiveModalId;
        sut.RegisterModal(TestRegistration(id, scope: ModalScope.Critical));
        service.CancelActive();

        // Act
        ModalScope? scope = sut.GetActiveModalScope();

        // Assert
        Assert.Null(scope);
    }

    [Fact]
    public void GetActiveModalScope_StandardModal_ReturnsStandard()
    {
        // Arrange
        var service = new ModalService();
        using var sut = new ModalCoordinator(service);
        _ = sut.PushAsync<DummyModal, bool>();
        ModalId id = service.ActiveModalId;
        sut.RegisterModal(TestRegistration(id, scope: ModalScope.Standard));

        // Act + Assert
        Assert.Equal(ModalScope.Standard, sut.GetActiveModalScope());
    }

    [Fact]
    public void ModalRegistration_NullRequestClose_ThrowsArgumentNullException()
    {
        // Arrange + Act + Assert
        Assert.Throws<ArgumentNullException>(() => new ModalRegistration(new ModalId(1L), null!, ModalScope.Standard, inlineAlertHost: null));
    }

    [Fact]
    public async Task PushAsync_ActiveModalAcceptsPreemption_ReturnsOpened()
    {
        // Arrange
        var service = new ModalService();
        using var sut = new ModalCoordinator(service);
        _ = sut.PushAsync<DummyModal, bool>();
        ModalId firstId = service.ActiveModalId;
        sut.RegisterModal(TestRegistration(firstId, _ => Task.FromResult(true)));

        // Act
        Task<ModalOpenResult<bool>> secondShow = sut.PushAsync<OtherModal, bool>();
        ModalId secondId = service.ActiveModalId;
        sut.ForceCloseActive();

        // Assert
        ModalOpenResult<bool> result = await secondShow;
        Assert.True(result.WasOpened);
        Assert.NotEqual(firstId, secondId);
    }

    [Fact]
    public async Task PushAsync_ActiveModalVetoesPreemption_ReturnsNotOpened()
    {
        // Arrange
        var service = new ModalService();
        using var sut = new ModalCoordinator(service);
        _ = sut.PushAsync<DummyModal, bool>();
        ModalId firstId = service.ActiveModalId;
        sut.RegisterModal(TestRegistration(firstId, _ => Task.FromResult(false)));

        // Act
        ModalOpenResult<bool> result = await sut.PushAsync<OtherModal, bool>();

        // Assert
        Assert.False(result.WasOpened);
        Assert.Equal(default, result.Result);
        Assert.Equal(firstId, service.ActiveModalId);
    }

    [Fact]
    public void RegisterModal_ModalIdNone_RejectsRegistrationToAvoidGhostState()
    {
        // Arrange — caller passes a ModalId.None registration (e.g., default-constructed); coordinator must not store it.
        var service = new ModalService();
        using var sut = new ModalCoordinator(service);

        // Act
        sut.RegisterModal(TestRegistration(ModalId.None));

        // Assert
        Assert.Null(sut.GetActiveModalScope());
        Assert.False(sut.TryGetInlineAlertHost(out _));
    }

    [Fact]
    public async Task RequestCloseActiveAsync_ConcurrentCalls_ShareResult()
    {
        // Arrange
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

        // Act — first call enters handler; second call coalesces.
        Task<bool> firstCall = sut.RequestCloseActiveAsync(ModalCloseReason.UserDismiss);
        await handlerReached.Task;
        Task<bool> secondCall = sut.RequestCloseActiveAsync(ModalCloseReason.EscKey);
        releaseHandler.SetResult(true);

        // Assert
        bool firstResult = await firstCall;
        bool secondResult = await secondCall;
        Assert.True(firstResult);
        Assert.True(secondResult);
    }

    [Fact]
    public async Task RequestCloseActiveAsync_CriticalScope_RejectsOtherModalActivationEvenWithEscInFlight()
    {
        // Arrange — scope policy fires per-call BEFORE coalescing, so Critical+OtherModalActivation is rejected
        // even when an Esc close is already in flight.
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

        // Act
        bool omaResult = await sut.RequestCloseActiveAsync(ModalCloseReason.OtherModalActivation);

        // Assert
        Assert.False(omaResult);

        releaseHandler.SetResult(true);
        await escCall;
    }

    [Fact]
    public async Task RequestCloseActiveAsync_CriticalScopeAndOtherModalActivation_RejectsWithoutCallingHandler()
    {
        // Arrange
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

        // Act
        bool result = await sut.RequestCloseActiveAsync(ModalCloseReason.OtherModalActivation);

        // Assert
        Assert.False(result);
        Assert.Equal(0, handlerCallCount);
    }

    [Fact]
    public async Task RequestCloseActiveAsync_CriticalScopeAndUserDismiss_DelegatesToHandler()
    {
        // Arrange — Critical scope only blocks OtherModalActivation; user gestures still delegate.
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

        // Act
        bool result = await sut.RequestCloseActiveAsync(ModalCloseReason.UserDismiss);

        // Assert
        Assert.True(result);
        Assert.Equal(1, handlerCallCount);
    }

    [Fact]
    public async Task RequestCloseActiveAsync_HandlerAccepts_ReturnsTrue()
    {
        // Arrange
        var service = new ModalService();
        using var sut = new ModalCoordinator(service);
        _ = sut.PushAsync<DummyModal, bool>();
        ModalId id = service.ActiveModalId;
        sut.RegisterModal(TestRegistration(id, _ => Task.FromResult(true)));

        // Act
        bool result = await sut.RequestCloseActiveAsync(ModalCloseReason.UserDismiss);

        // Assert
        Assert.True(result);
    }

    [Fact]
    public async Task RequestCloseActiveAsync_HandlerCanceled_AwaitersReceiveAccepted()
    {
        // Arrange — OperationCanceledException from a force-close race is treated as accepted=true so
        // coalesced PushAsync callers don't blow up with unhandled OCE.
        var service = new ModalService();
        using var sut = new ModalCoordinator(service);
        _ = sut.PushAsync<DummyModal, bool>();
        ModalId id = service.ActiveModalId;
        sut.RegisterModal(TestRegistration(id, _ => throw new OperationCanceledException()));

        // Act
        bool result = await sut.RequestCloseActiveAsync(ModalCloseReason.UserDismiss);

        // Assert
        Assert.True(result);
    }

    [Fact]
    public async Task RequestCloseActiveAsync_HandlerThrows_CoalescedAwaitersSeeException()
    {
        // Arrange
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

        // Act
        releaseHandler.SetResult(true);

        // Assert
        await Assert.ThrowsAsync<InvalidOperationException>(() => firstCall);
        await Assert.ThrowsAsync<InvalidOperationException>(() => secondCall);
    }

    [Fact]
    public async Task RequestCloseActiveAsync_HandlerThrows_PropagatesException()
    {
        // Arrange
        var service = new ModalService();
        using var sut = new ModalCoordinator(service);
        _ = sut.PushAsync<DummyModal, bool>();
        ModalId id = service.ActiveModalId;
        sut.RegisterModal(TestRegistration(id, _ => throw new InvalidOperationException("handler failure")));

        // Act
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => sut.RequestCloseActiveAsync(ModalCloseReason.UserDismiss));

        // Assert
        Assert.Equal("handler failure", ex.Message);

        // Subsequent call should not be stuck on the previous TCS — _inFlightCloseTcs cleared in finally.
        sut.UnregisterModal(id);
        bool followup = await sut.RequestCloseActiveAsync(ModalCloseReason.UserDismiss);
        Assert.True(followup);
    }

    [Fact]
    public async Task RequestCloseActiveAsync_HandlerVetoes_ReturnsFalse()
    {
        // Arrange
        var service = new ModalService();
        using var sut = new ModalCoordinator(service);
        _ = sut.PushAsync<DummyModal, bool>();
        ModalId id = service.ActiveModalId;
        sut.RegisterModal(TestRegistration(id, _ => Task.FromResult(false)));

        // Act
        bool result = await sut.RequestCloseActiveAsync(ModalCloseReason.UserDismiss);

        // Assert
        Assert.False(result);
        Assert.NotNull(sut.ActiveSession);
    }

    [Fact]
    public async Task RequestCloseActiveAsync_HandlerVetoes_SecondCallCanSucceedAfterHandlerSwitches()
    {
        // Arrange — first call vetoed; the captured flag toggles to accept and the next call should succeed
        // (verifies _inFlightCloseTcs is cleared in finally after a veto, not stuck holding the false result).
        var service = new ModalService();
        using var sut = new ModalCoordinator(service);
        _ = sut.PushAsync<DummyModal, bool>();
        ModalId id = service.ActiveModalId;
        bool acceptNext = false;
        sut.RegisterModal(TestRegistration(id, _ => Task.FromResult(acceptNext)));

        // Act
        bool first = await sut.RequestCloseActiveAsync(ModalCloseReason.UserDismiss);
        acceptNext = true;
        bool second = await sut.RequestCloseActiveAsync(ModalCloseReason.UserDismiss);

        // Assert
        Assert.False(first);
        Assert.True(second);
    }

    [Fact]
    public async Task RequestCloseActiveAsync_InitWindowOtherModalActivation_RejectsToProtectScopePolicy()
    {
        // Arrange — service has ActiveModalId published but RegisterModal hasn't fired yet (OnInitialized gap).
        // OtherModalActivation must be rejected so a not-yet-registered Critical modal can't be preempted.
        var service = new ModalService();
        using var sut = new ModalCoordinator(service);
        _ = sut.PushAsync<DummyModal, bool>();
        Assert.NotEqual(ModalId.None, service.ActiveModalId);
        Assert.Null(sut.GetActiveModalScope());

        // Act
        bool result = await sut.RequestCloseActiveAsync(ModalCloseReason.OtherModalActivation);

        // Assert
        Assert.False(result);
    }

    [Fact]
    public async Task RequestCloseActiveAsync_InitWindowUserDismiss_ReturnsTrue()
    {
        // Arrange — non-preemption reasons during init window still return true (idempotent no-op equivalent).
        var service = new ModalService();
        using var sut = new ModalCoordinator(service);
        _ = sut.PushAsync<DummyModal, bool>();
        Assert.NotEqual(ModalId.None, service.ActiveModalId);
        Assert.Null(sut.GetActiveModalScope());

        // Act
        bool result = await sut.RequestCloseActiveAsync(ModalCloseReason.UserDismiss);

        // Assert
        Assert.True(result);
    }

    [Fact]
    public async Task RequestCloseActiveAsync_NoActiveModal_ReturnsTrue()
    {
        // Arrange
        var service = new ModalService();
        using var sut = new ModalCoordinator(service);

        // Act
        bool result = await sut.RequestCloseActiveAsync(ModalCloseReason.UserDismiss);

        // Assert
        Assert.True(result);
    }

    [Fact]
    public async Task RequestCloseActiveAsync_StaleCriticalRegistrationAfterCancel_DoesNotVetoOtherModalActivation()
    {
        // Arrange — register a Critical modal, then cancel via the service. A subsequent OtherModalActivation must
        // NOT see the stale Critical scope (which would incorrectly veto a legitimate preemption).
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

        // Act — service has no active modal anymore; OMA should fall through, not be vetoed by stale Critical scope.
        bool result = await sut.RequestCloseActiveAsync(ModalCloseReason.OtherModalActivation);

        // Assert
        Assert.True(result);
        Assert.Equal(0, handlerCallCount);
    }

    [Fact]
    public void TryGetInlineAlertHost_StaleRegistrationAfterCancel_ReturnsFalse()
    {
        // Arrange — register a modal with an inline-alert host, then cancel; the stored host must not be returned.
        var service = new ModalService();
        using var sut = new ModalCoordinator(service);
        _ = sut.PushAsync<DummyModal, bool>();
        ModalId id = service.ActiveModalId;
        var host = Substitute.For<IInlineAlertHost>();
        sut.RegisterModal(new ModalRegistration(id, _ => Task.FromResult(true), ModalScope.Standard, host));
        service.CancelActive();

        // Act
        bool found = sut.TryGetInlineAlertHost(out IInlineAlertHost? resolved);

        // Assert
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
