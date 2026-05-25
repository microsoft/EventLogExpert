// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Runtime.Modal;
using Microsoft.AspNetCore.Components;

namespace EventLogExpert.Runtime.Tests.Modal;

/// <summary>Verifies StateChanged fires before awaiter resumption on Complete/CancelActive/Show preempt.</summary>
public sealed class ModalServiceCompletionOrderingTests
{
    [Fact]
    public async Task CancelActive_FiresStateChangedBeforeAwaiterResumes()
    {
        // Arrange
        var sut = new ModalService();
        bool stateChangedFiredBeforeAwaiter = false;
        bool awaiterResumed = false;

        Task<bool> showTask = sut.Show<DummyModal, bool>();

        // Subscribe AFTER Show so the initial Show event doesn't satisfy the assertion before
        // CancelActive runs; we only want to observe the CancelActive-fired StateChanged.
        sut.StateChanged += () =>
        {
            if (!awaiterResumed) { stateChangedFiredBeforeAwaiter = true; }
        };

        var awaiterReached = new TaskCompletionSource();

        Task observer = Task.Run(async () =>
        {
            await showTask;
            awaiterResumed = true;
            awaiterReached.SetResult();
        }, TestContext.Current.CancellationToken);

        // Act
        sut.CancelActive();
        await awaiterReached.Task;
        await observer;

        // Assert
        Assert.True(stateChangedFiredBeforeAwaiter);
    }

    [Fact]
    public async Task CancelActive_StateChangedSubscriberThrows_AwaiterStillResumes()
    {
        // Arrange
        var sut = new ModalService();
        Task<bool> showTask = sut.Show<DummyModal, bool>();
        sut.StateChanged += () => throw new InvalidOperationException("subscriber misbehavior");

        // Act
        Exception? thrown = Record.Exception(sut.CancelActive);

        // Assert
        Assert.NotNull(thrown);
        bool result = await showTask;
        Assert.False(result);
    }

    [Fact]
    public async Task Complete_FiresStateChangedBeforeAwaiterResumes()
    {
        // Arrange
        var sut = new ModalService();
        bool stateChangedFiredBeforeAwaiter = false;
        bool awaiterResumed = false;

        Task<bool> showTask = sut.Show<DummyModal, bool>();
        long id = sut.ActiveModalId.Value;

        // Subscribe AFTER Show so the initial Show event doesn't satisfy the assertion before
        // Complete runs; we only want to observe the Complete-fired StateChanged.
        sut.StateChanged += () =>
        {
            if (!awaiterResumed) { stateChangedFiredBeforeAwaiter = true; }
        };

        var awaiterReached = new TaskCompletionSource();

        Task observer = Task.Run(async () =>
        {
            await showTask;
            awaiterResumed = true;
            awaiterReached.SetResult();
        }, TestContext.Current.CancellationToken);

        // Act
        sut.Complete(new ModalId(id), true);
        await awaiterReached.Task;
        await observer;

        // Assert
        Assert.True(stateChangedFiredBeforeAwaiter);
    }

    [Fact]
    public async Task Complete_StateChangedSubscriberThrows_AwaiterStillResumes()
    {
        // Arrange
        var sut = new ModalService();
        Task<bool> showTask = sut.Show<DummyModal, bool>();
        long id = sut.ActiveModalId.Value;

        // Subscribe AFTER Show so the throw only fires on the Complete-triggered StateChanged.
        sut.StateChanged += () => throw new InvalidOperationException("subscriber misbehavior");

        // Act
        Exception? thrown = Record.Exception(() => sut.Complete(new ModalId(id), true));

        // Assert
        Assert.NotNull(thrown);
        bool result = await showTask;
        Assert.True(result);
    }

    [Fact]
    public async Task Show_PreemptingPrior_FiresStateChangedBeforePriorAwaiterResumes()
    {
        // Arrange
        var sut = new ModalService();
        bool stateChangedFiredBeforePriorAwaiter = false;
        bool priorAwaiterResumed = false;

        Task<bool> firstShow = sut.Show<DummyModal, bool>();
        var priorAwaiterReached = new TaskCompletionSource();

        Task priorObserver = Task.Run(async () =>
        {
            await firstShow;
            priorAwaiterResumed = true;
            priorAwaiterReached.SetResult();
        }, TestContext.Current.CancellationToken);

        sut.StateChanged += () =>
        {
            if (!priorAwaiterResumed) { stateChangedFiredBeforePriorAwaiter = true; }
        };

        // Act — second Show pre-empts; StateChanged announcing the NEW modal must fire before
        // the prior modal's awaiter resumes.
        Task<bool> secondShow = sut.Show<OtherModal, bool>();
        await priorAwaiterReached.Task;
        await priorObserver;

        // Assert
        Assert.True(stateChangedFiredBeforePriorAwaiter);

        sut.CancelActive();
        await secondShow;
    }

    [Fact]
    public async Task Show_PreemptingWhenStateChangedThrows_PriorAwaiterStillResumes()
    {
        // Arrange
        var sut = new ModalService();
        Task<bool> firstShow = sut.Show<DummyModal, bool>();
        sut.StateChanged += () => throw new InvalidOperationException("subscriber misbehavior");

        // Act
        Exception? thrown = Record.Exception(() => { _ = sut.Show<OtherModal, bool>(); });

        // Assert
        Assert.NotNull(thrown);
        bool firstResult = await firstShow;
        Assert.False(firstResult);
    }

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
