// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.UI.Services;
using Microsoft.AspNetCore.Components;

namespace EventLogExpert.UI.Tests.Services;

public sealed class ModalServiceTests
{
    [Fact]
    public async Task CancelActive_WhenModalIsOpen_ShouldCompleteWithDefaultAndClearState()
    {
        // Arrange
        var service = new ModalService();
        var stateChangedCount = 0;
        service.StateChanged += () => stateChangedCount++;

        var task = service.Show<FakeModalA, bool>();

        // Act
        service.CancelActive();

        // Assert
        var result = await task;
        Assert.False(result);
        Assert.Null(service.ActiveModalType);
        Assert.Equal(2, stateChangedCount);
    }

    [Fact]
    public void CancelActive_WhenNoModalIsOpen_ShouldBeNoOp()
    {
        // Arrange
        var service = new ModalService();
        var stateChangedCount = 0;
        service.StateChanged += () => stateChangedCount++;

        // Act
        var exception = Record.Exception(service.CancelActive);

        // Assert
        Assert.Null(exception);
        Assert.Equal(0, stateChangedCount);
    }

    [Fact]
    public void Complete_CalledTwiceWithSameId_ShouldBeIdempotent()
    {
        // Arrange
        var service = new ModalService();
        _ = service.Show<FakeModalA, bool>();
        var modalId = service.ActiveModalId;

        service.Complete(modalId, true);

        // Act — second call uses an id that no longer matches ActiveModalId (cleared above).
        var exception = Record.Exception(() => service.Complete(modalId, false));

        // Assert
        Assert.Null(exception);
    }

    [Fact]
    public async Task Complete_WithCurrentId_ShouldCompleteTaskAndClearState()
    {
        // Arrange
        var service = new ModalService();
        var stateChangedCount = 0;
        service.StateChanged += () => stateChangedCount++;

        var task = service.Show<FakeModalA, bool>();
        var modalId = service.ActiveModalId;

        // Act
        service.Complete(modalId, true);

        // Assert
        var result = await task;
        Assert.True(result);
        Assert.Null(service.ActiveModalType);
        Assert.Null(service.ActiveModalParameters);
        Assert.Equal(2, stateChangedCount); // Once for Show, once for Complete.
    }

    [Fact]
    public async Task Complete_WithMismatchedResultType_ShouldNotStrandAwaiter()
    {
        // Arrange — guards the dynamic-cast inside Complete{TResult}. A wrong TResult MUST be a
        // no-op so the real awaiter (with the correct type) can still complete.
        var service = new ModalService();
        var task = service.Show<FakeModalA, bool>();
        var modalId = service.ActiveModalId;

        // Act — wrong TResult type for the in-flight task.
        service.Complete(modalId, "wrong-type");

        // Assert — task still pending, state still active.
        Assert.False(task.IsCompleted);
        Assert.Equal(typeof(FakeModalA), service.ActiveModalType);

        // Correct-typed call still completes the task and clears state.
        service.Complete(modalId, true);
        var result = await task;
        Assert.True(result);
        Assert.Null(service.ActiveModalType);
    }

    [Fact]
    public async Task Complete_WithStaleId_ShouldNotCompleteCurrentModalsTask()
    {
        // Arrange — modal A is replaced by modal B; A's late callback must not complete B.
        var service = new ModalService();
        var firstTask = service.Show<FakeModalA, bool>();
        var staleId = service.ActiveModalId;

        var secondTask = service.Show<FakeModalB, bool>();

        // First modal's auto-cancel completes its own task as default.
        await firstTask;

        // Act — simulate a delayed callback from modal A using its captured (now stale) id.
        service.Complete(staleId, true);

        // Assert — modal B's task is still pending.
        Assert.False(secondTask.IsCompleted);
        Assert.Equal(typeof(FakeModalB), service.ActiveModalType);
    }

    [Fact]
    public async Task Show_WhenAnotherModalIsActive_ShouldCompleteFirstWithDefault()
    {
        // Arrange
        var service = new ModalService();
        var firstTask = service.Show<FakeModalA, bool>();

        // Act
        var secondTask = service.Show<FakeModalB, bool>();

        // Assert
        var firstResult = await firstTask;
        Assert.False(firstResult);
        Assert.False(secondTask.IsCompleted);
        Assert.Equal(typeof(FakeModalB), service.ActiveModalType);
    }

    [Fact]
    public void Show_WhenCalled_ShouldSetActiveStateAndRaiseStateChanged()
    {
        // Arrange
        var service = new ModalService();
        var stateChangedCount = 0;
        service.StateChanged += () => stateChangedCount++;

        var parameters = new Dictionary<string, object?> { ["Foo"] = 42 };

        // Act
        var task = service.Show<FakeModalA, bool>(parameters);

        // Assert
        Assert.Equal(typeof(FakeModalA), service.ActiveModalType);
        Assert.Same(parameters, service.ActiveModalParameters);
        Assert.NotEqual(0, service.ActiveModalId);
        Assert.False(task.IsCompleted);
        Assert.Equal(1, stateChangedCount);
    }

    [Fact]
    public void Show_WhenCalledTwice_ShouldAssignDifferentActiveModalIds()
    {
        // Arrange
        var service = new ModalService();

        // Act
        _ = service.Show<FakeModalA, bool>();
        var firstId = service.ActiveModalId;
        _ = service.Show<FakeModalA, bool>();
        var secondId = service.ActiveModalId;

        // Assert
        Assert.NotEqual(firstId, secondId);
    }

    [Fact]
    public async Task Show_WhenSameTypeReopened_ShouldCreateFreshTaskWithNewId()
    {
        // Arrange
        var service = new ModalService();

        var firstTask = service.Show<FakeModalA, bool>();
        var firstId = service.ActiveModalId;
        service.Complete(firstId, true);
        var firstResult = await firstTask;

        // Act
        var secondTask = service.Show<FakeModalA, bool>();
        var secondId = service.ActiveModalId;

        // Assert
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
