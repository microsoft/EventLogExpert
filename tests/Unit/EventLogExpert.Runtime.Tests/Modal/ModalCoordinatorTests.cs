// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Runtime.Alerts;
using EventLogExpert.Runtime.Modal;
using Microsoft.AspNetCore.Components;
using NSubstitute;

namespace EventLogExpert.Runtime.Tests.Modal;

public sealed class ModalCoordinatorTests
{
    [Fact]
    public async Task ActiveSession_AfterComplete_IsNullBeforeAwaiterResumes()
    {
        // Arrange
        var service = new ModalService();
        using var sut = new ModalCoordinator(service);
        Task<ModalOpenResult<bool>> showTask = sut.PushAsync<DummyModal, bool>();
        ModalId activeId = service.ActiveModalId;
        Assert.NotNull(sut.ActiveSession);

        // Capture ActiveSession at the moment the awaiter resumes — proves the publication-before-resume
        // invariant: ModalService must fire StateChanged (clearing the coordinator's mirror) BEFORE
        // TrySetResult resumes the awaiter. Without the invariant, sessionAtResume would be non-null.
        var resumeSnapshot = new TaskCompletionSource<ModalSession?>();
        Task observer = Task.Run(async () =>
        {
            await showTask;
            resumeSnapshot.SetResult(sut.ActiveSession);
        }, TestContext.Current.CancellationToken);

        // Act
        service.Complete(activeId, true);

        // Assert
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
        // Arrange
        var service = new ModalService();
        using var sut = new ModalCoordinator(service);

        // Act
        _ = sut.PushAsync<DummyModal, bool>();

        // Assert
        Assert.NotNull(sut.ActiveSession);
        Assert.Equal(service.ActiveModalId, sut.ActiveSession.Id);
        Assert.Equal(typeof(DummyModal), sut.ActiveSession.ComponentType);

        await Task.CompletedTask;
    }

    [Fact]
    public void ActiveSession_NoActiveModal_ReturnsNull()
    {
        // Arrange
        var service = new ModalService();
        using var sut = new ModalCoordinator(service);

        // Assert
        Assert.Null(sut.ActiveSession);
    }

    [Fact]
    public void Complete_StaleId_NoOps_StackUnchanged()
    {
        // Arrange
        var service = new ModalService();
        using var sut = new ModalCoordinator(service);
        _ = sut.PushAsync<DummyModal, bool>();
        long activeId = service.ActiveModalId.Value;

        // Act
        sut.Complete(new ModalId(activeId + 99), true);

        // Assert
        Assert.NotNull(sut.ActiveSession);
        Assert.Equal(new ModalId(activeId), sut.ActiveSession.Id);
    }

    [Fact]
    public void Dispose_UnhooksServiceStateChanged_NoLeak()
    {
        // Arrange
        var service = new ModalService();
        var sut = new ModalCoordinator(service);
        int fireCount = 0;
        sut.StateChanged += () => fireCount++;

        // Act
        sut.Dispose();
        _ = service.Show<DummyModal, bool>();

        // Assert — disposed coordinator must not propagate service state changes.
        Assert.Equal(0, fireCount);
        service.CancelActive();
    }

    [Fact]
    public async Task PushAsync_PreemptsPriorModal_CoordinatorMirrorsLatest()
    {
        // Arrange
        var service = new ModalService();
        using var sut = new ModalCoordinator(service);

        // Act
        Task<ModalOpenResult<bool>> firstShow = sut.PushAsync<DummyModal, bool>();
        ModalId firstId = service.ActiveModalId;
        sut.RegisterModal(TestRegistration(firstId, _ => Task.FromResult(true)));
        Task<ModalOpenResult<bool>> secondShow = sut.PushAsync<OtherModal, bool>();
        ModalId secondId = service.ActiveModalId;

        // Assert
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
    public void RegisterModal_ActiveId_RegistersAndInlineAlertHostIsReadable()
    {
        // Arrange
        var service = new ModalService();
        using var sut = new ModalCoordinator(service);
        _ = sut.PushAsync<DummyModal, bool>();
        ModalId id = service.ActiveModalId;
        var host = Substitute.For<IInlineAlertHost>();

        // Act
        sut.RegisterModal(TestRegistration(id, host));

        // Assert
        Assert.True(sut.TryGetInlineAlertHost(out var resolved));
        Assert.Same(host, resolved);
    }

    [Fact]
    public void RegisterModal_StaleId_IsNoOp()
    {
        // Arrange
        var service = new ModalService();
        using var sut = new ModalCoordinator(service);
        var host = Substitute.For<IInlineAlertHost>();

        // Act
        sut.RegisterModal(TestRegistration(new ModalId(999L), host));

        // Assert
        Assert.False(sut.TryGetInlineAlertHost(out _));
    }

    [Fact]
    public void StateChanged_OnPush_FiresAfterMirrorUpdate()
    {
        // Arrange
        var service = new ModalService();
        using var sut = new ModalCoordinator(service);
        int fireCount = 0;
        ModalSession? observedAtFire = null;
        sut.StateChanged += () =>
        {
            fireCount++;
            observedAtFire = sut.ActiveSession;
        };

        // Act
        _ = sut.PushAsync<DummyModal, bool>();

        // Assert — subscriber must observe the new active session, not null and not stale.
        Assert.Equal(1, fireCount);
        Assert.NotNull(observedAtFire);
        Assert.Equal(service.ActiveModalId, observedAtFire.Id);
    }

    [Fact]
    public void TryGetInlineAlertHost_AfterPushPreemptsPrior_ReturnsFalseAndClearsStaleHost()
    {
        // Arrange
        var service = new ModalService();
        using var sut = new ModalCoordinator(service);
        _ = sut.PushAsync<DummyModal, bool>();
        ModalId firstId = service.ActiveModalId;
        var host = Substitute.For<IInlineAlertHost>();
        sut.RegisterModal(TestRegistration(firstId, host));
        Assert.True(sut.TryGetInlineAlertHost(out _));

        // Act — successor preempts prior; the host is now stale.
        _ = sut.PushAsync<OtherModal, bool>();

        // Assert
        Assert.False(sut.TryGetInlineAlertHost(out _));

        sut.ForceCloseActive();
    }

    [Fact]
    public void UnregisterModal_MatchingId_ClearsRegistration()
    {
        // Arrange
        var service = new ModalService();
        using var sut = new ModalCoordinator(service);
        _ = sut.PushAsync<DummyModal, bool>();
        ModalId id = service.ActiveModalId;
        sut.RegisterModal(TestRegistration(id, Substitute.For<IInlineAlertHost>()));

        // Act
        sut.UnregisterModal(id);

        // Assert
        Assert.False(sut.TryGetInlineAlertHost(out _));
        Assert.Null(sut.GetActiveModalScope());
    }

    [Fact]
    public void UnregisterModal_StaleId_IsNoOp()
    {
        // Arrange
        var service = new ModalService();
        using var sut = new ModalCoordinator(service);
        _ = sut.PushAsync<DummyModal, bool>();
        ModalId id = service.ActiveModalId;
        var host = Substitute.For<IInlineAlertHost>();
        sut.RegisterModal(TestRegistration(id, host));

        // Act
        sut.UnregisterModal(new ModalId(id.Value + 99));

        // Assert
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
