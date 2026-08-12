// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.UI.Modal;
using Microsoft.AspNetCore.Components;

namespace EventLogExpert.UI.Tests.Modal;

public sealed class ModalServiceTests
{
    [Fact]
    public async Task CancelActive_WhenModalIsOpen_ShouldCompleteWithDefaultAndClearState()
    {
        var service = new ModalService();
        var stateChangedCount = 0;
        service.StateChanged += () => stateChangedCount++;

        var task = service.Show<FakeModalA, bool>();

        service.CancelActive();

        var result = await task;
        Assert.False(result);
        Assert.Null(service.ActiveModalType);
        Assert.Equal(2, stateChangedCount);
    }

    [Fact]
    public void CancelActive_WhenNoModalIsOpen_ShouldBeNoOp()
    {
        var service = new ModalService();
        var stateChangedCount = 0;
        service.StateChanged += () => stateChangedCount++;

        var exception = Record.Exception(service.CancelActive);

        Assert.Null(exception);
        Assert.Equal(0, stateChangedCount);
    }

    [Fact]
    public void Complete_CalledTwiceWithSameId_ShouldBeIdempotent()
    {
        var service = new ModalService();
        _ = service.Show<FakeModalA, bool>();
        var modalId = service.ActiveModalId;

        service.Complete(modalId, true);

        var exception = Record.Exception(() => service.Complete(modalId, false));

        Assert.Null(exception);
    }

    [Fact]
    public async Task Complete_WithCurrentId_ShouldCompleteTaskAndClearState()
    {
        var service = new ModalService();
        var stateChangedCount = 0;
        service.StateChanged += () => stateChangedCount++;

        var task = service.Show<FakeModalA, bool>();
        var modalId = service.ActiveModalId;

        service.Complete(modalId, true);

        var result = await task;
        Assert.True(result);
        Assert.Null(service.ActiveModalType);
        Assert.Null(service.ActiveModalParameters);
        Assert.Equal(2, stateChangedCount);
    }

    [Fact]
    public async Task Complete_WithMismatchedResultType_ShouldNotStrandAwaiter()
    {
        var service = new ModalService();
        var task = service.Show<FakeModalA, bool>();
        var modalId = service.ActiveModalId;

        service.Complete(modalId, "wrong-type");

        Assert.False(task.IsCompleted);
        Assert.Equal(typeof(FakeModalA), service.ActiveModalType);

        service.Complete(modalId, true);
        var result = await task;
        Assert.True(result);
        Assert.Null(service.ActiveModalType);
    }

    [Fact]
    public async Task Complete_WithStaleId_ShouldNotCompleteCurrentModalsTask()
    {
        var service = new ModalService();
        var firstTask = service.Show<FakeModalA, bool>();
        var staleId = service.ActiveModalId;

        var secondTask = service.Show<FakeModalB, bool>();

        await firstTask;

        service.Complete(staleId, true);

        Assert.False(secondTask.IsCompleted);
        Assert.Equal(typeof(FakeModalB), service.ActiveModalType);
    }

    [Fact]
    public async Task Show_WhenAnotherModalIsActive_ShouldCompleteFirstWithDefault()
    {
        var service = new ModalService();
        var firstTask = service.Show<FakeModalA, bool>();

        var secondTask = service.Show<FakeModalB, bool>();

        var firstResult = await firstTask;
        Assert.False(firstResult);
        Assert.False(secondTask.IsCompleted);
        Assert.Equal(typeof(FakeModalB), service.ActiveModalType);
    }

    [Fact]
    public void Show_WhenCalledTwice_ShouldAssignDifferentActiveModalIds()
    {
        var service = new ModalService();

        _ = service.Show<FakeModalA, bool>();
        var firstId = service.ActiveModalId;
        _ = service.Show<FakeModalA, bool>();
        var secondId = service.ActiveModalId;

        Assert.NotEqual(firstId, secondId);
    }

    [Fact]
    public void Show_WhenCalled_ShouldSetActiveStateAndRaiseStateChanged()
    {
        var service = new ModalService();
        var stateChangedCount = 0;
        service.StateChanged += () => stateChangedCount++;

        var parameters = new Dictionary<string, object?> { ["Foo"] = 42 };

        var task = service.Show<FakeModalA, bool>(parameters);

        Assert.Equal(typeof(FakeModalA), service.ActiveModalType);
        Assert.Same(parameters, service.ActiveModalParameters);
        Assert.NotEqual(ModalId.None, service.ActiveModalId);
        Assert.False(task.IsCompleted);
        Assert.Equal(1, stateChangedCount);
    }

    [Fact]
    public async Task Show_WhenSameTypeReopened_ShouldCreateFreshTaskWithNewId()
    {
        var service = new ModalService();

        var firstTask = service.Show<FakeModalA, bool>();
        var firstId = service.ActiveModalId;
        service.Complete(firstId, true);
        var firstResult = await firstTask;

        var secondTask = service.Show<FakeModalA, bool>();
        var secondId = service.ActiveModalId;

        Assert.True(firstResult);
        Assert.NotEqual(firstId, secondId);
        Assert.False(secondTask.IsCompleted);
    }

    private sealed class FakeModalA : IComponent
    {
        public void Attach(RenderHandle renderHandle) { }

        public Task SetParametersAsync(ParameterView parameters) => Task.CompletedTask;
    }

    private sealed class FakeModalB : IComponent
    {
        public void Attach(RenderHandle renderHandle) { }

        public Task SetParametersAsync(ParameterView parameters) => Task.CompletedTask;
    }
}
