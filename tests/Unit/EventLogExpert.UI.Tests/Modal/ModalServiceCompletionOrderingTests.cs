// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.UI.Modal;
using Microsoft.AspNetCore.Components;

namespace EventLogExpert.UI.Tests.Modal;

public sealed class ModalServiceCompletionOrderingTests
{
    [Fact]
    public async Task CancelActive_FiresStateChangedBeforeAwaiterResumes()
    {
        var sut = new ModalService();
        bool stateChangedFiredBeforeAwaiter = false;
        bool awaiterResumed = false;

        Task<bool> showTask = sut.Show<DummyModal, bool>();

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

        sut.CancelActive();
        await awaiterReached.Task;
        await observer;

        Assert.True(stateChangedFiredBeforeAwaiter);
    }

    [Fact]
    public async Task CancelActive_StateChangedSubscriberThrows_AwaiterStillResumes()
    {
        var sut = new ModalService();
        Task<bool> showTask = sut.Show<DummyModal, bool>();
        sut.StateChanged += () => throw new InvalidOperationException("subscriber misbehavior");

        Exception? thrown = Record.Exception(sut.CancelActive);

        Assert.NotNull(thrown);
        bool result = await showTask;
        Assert.False(result);
    }

    [Fact]
    public async Task Complete_FiresStateChangedBeforeAwaiterResumes()
    {
        var sut = new ModalService();
        bool stateChangedFiredBeforeAwaiter = false;
        bool awaiterResumed = false;

        Task<bool> showTask = sut.Show<DummyModal, bool>();
        long id = sut.ActiveModalId.Value;

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

        sut.Complete(new ModalId(id), true);
        await awaiterReached.Task;
        await observer;

        Assert.True(stateChangedFiredBeforeAwaiter);
    }

    [Fact]
    public async Task Complete_StateChangedSubscriberThrows_AwaiterStillResumes()
    {
        var sut = new ModalService();
        Task<bool> showTask = sut.Show<DummyModal, bool>();
        long id = sut.ActiveModalId.Value;

        sut.StateChanged += () => throw new InvalidOperationException("subscriber misbehavior");

        Exception? thrown = Record.Exception(() => sut.Complete(new ModalId(id), true));

        Assert.NotNull(thrown);
        bool result = await showTask;
        Assert.True(result);
    }

    [Fact]
    public async Task Show_PreemptingPrior_FiresStateChangedBeforePriorAwaiterResumes()
    {
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

        Task<bool> secondShow = sut.Show<OtherModal, bool>();
        await priorAwaiterReached.Task;
        await priorObserver;

        Assert.True(stateChangedFiredBeforePriorAwaiter);

        sut.CancelActive();
        await secondShow;
    }

    [Fact]
    public async Task Show_PreemptingWhenStateChangedThrows_PriorAwaiterStillResumes()
    {
        var sut = new ModalService();
        Task<bool> firstShow = sut.Show<DummyModal, bool>();
        sut.StateChanged += () => throw new InvalidOperationException("subscriber misbehavior");

        Exception? thrown = Record.Exception(() => { _ = sut.Show<OtherModal, bool>(); });

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
