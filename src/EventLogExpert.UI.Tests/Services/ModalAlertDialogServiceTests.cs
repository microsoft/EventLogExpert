// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.UI.Interfaces;
using EventLogExpert.UI.Services;
using NSubstitute;

namespace EventLogExpert.UI.Tests.Services;

public sealed class ModalAlertDialogServiceTests
{
    [Fact]
    public async Task DisplayPrompt_WhenActiveHost_ShouldRouteInlineAndReturnTypedValue()
    {
        // Arrange
        var host = Substitute.For<IInlineAlertHost>();
        host.ShowInlineAlertAsync(Arg.Any<InlineAlertRequest>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new InlineAlertResult(true, "typed-value")));

        var modalService = Substitute.For<IModalService>();
        modalService.TryGetActiveAlertHost(out Arg.Any<IInlineAlertHost?>()).Returns(call =>
        {
            call[0] = host;
            return true;
        });

        var sut = new ModalAlertDialogService(
            modalService,
            PassthroughMainThread(),
            _ => Task.FromResult(false),
            _ => Task.FromResult(string.Empty));

        // Act
        var result = await sut.DisplayPrompt("Rename", "Enter new name");

        // Assert
        Assert.Equal("typed-value", result);
        await host.Received(1).ShowInlineAlertAsync(
            Arg.Is<InlineAlertRequest>(r => r.IsPrompt && r.Title == "Rename" && r.Message == "Enter new name"),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task DisplayPrompt_WhenInlineCancelled_ShouldReturnEmptyString()
    {
        // Arrange — preserve the existing IsNullOrEmpty-friendly contract on cancel.
        var host = Substitute.For<IInlineAlertHost>();
        host.ShowInlineAlertAsync(Arg.Any<InlineAlertRequest>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new InlineAlertResult(false, null)));

        var modalService = Substitute.For<IModalService>();
        modalService.TryGetActiveAlertHost(out Arg.Any<IInlineAlertHost?>()).Returns(call =>
        {
            call[0] = host;
            return true;
        });

        var sut = new ModalAlertDialogService(
            modalService,
            PassthroughMainThread(),
            _ => Task.FromResult(false),
            _ => Task.FromResult(string.Empty));

        // Act
        var result = await sut.DisplayPrompt("Rename", "Enter new name");

        // Assert
        Assert.Equal(string.Empty, result);
    }

    [Fact]
    public async Task DisplayPrompt_WhenNoActiveHost_ShouldCallStandalonePromptOpener()
    {
        // Arrange
        var modalService = Substitute.For<IModalService>();
        modalService.TryGetActiveAlertHost(out Arg.Any<IInlineAlertHost?>()).Returns(false);

        IReadOnlyDictionary<string, object?>? capturedPrompt = null;
        var sut = new ModalAlertDialogService(
            modalService,
            PassthroughMainThread(),
            _ => Task.FromResult(false),
            parameters => { capturedPrompt = parameters; return Task.FromResult("user-typed"); });

        // Act
        var result = await sut.DisplayPrompt("Rename", "Enter new name", "old-value");

        // Assert
        Assert.Equal("user-typed", result);
        Assert.NotNull(capturedPrompt);
        Assert.Equal("Rename", capturedPrompt!["Title"]);
        Assert.Equal("Enter new name", capturedPrompt["Message"]);
        Assert.Equal("old-value", capturedPrompt["InitialValue"]);
    }

    [Fact]
    public async Task ShowAlert_ShouldMarshalThroughMainThreadService()
    {
        // Arrange — capture that MainThread invocation happens before the routing decision runs.
        var modalService = Substitute.For<IModalService>();
        modalService.TryGetActiveAlertHost(out Arg.Any<IInlineAlertHost?>()).Returns(false);

        var mainThread = Substitute.For<IMainThreadService>();
        mainThread.InvokeOnMainThreadAsync(Arg.Any<Func<Task>>())
            .Returns(call => ((Func<Task>)call[0])());

        var sut = new ModalAlertDialogService(
            modalService,
            mainThread,
            _ => Task.FromResult(true),
            _ => Task.FromResult(string.Empty));

        // Act
        await sut.ShowAlert("t", "m", "c");

        // Assert
        await mainThread.Received(1).InvokeOnMainThreadAsync(Arg.Any<Func<Task>>());
    }

    [Fact]
    public async Task ShowAlertOneButton_WhenNoActiveHost_ShouldCallStandaloneOpener()
    {
        // Arrange
        var modalService = Substitute.For<IModalService>();
        modalService.TryGetActiveAlertHost(out Arg.Any<IInlineAlertHost?>()).Returns(false);

        IReadOnlyDictionary<string, object?>? capturedAlert = null;
        var sut = new ModalAlertDialogService(
            modalService,
            PassthroughMainThread(),
            parameters => { capturedAlert = parameters; return Task.FromResult(true); },
            _ => Task.FromResult(string.Empty));

        // Act
        await sut.ShowAlert("My Title", "My Message", "Close");

        // Assert
        Assert.NotNull(capturedAlert);
        Assert.Equal("My Title", capturedAlert!["Title"]);
        Assert.Equal("My Message", capturedAlert["Message"]);
        Assert.Null(capturedAlert["AcceptLabel"]);
        Assert.Equal("Close", capturedAlert["CancelLabel"]);
    }

    [Fact]
    public async Task ShowAlertTwoButton_WhenActiveHost_ShouldRouteToHostInline()
    {
        // Arrange
        var host = Substitute.For<IInlineAlertHost>();
        host.ShowInlineAlertAsync(Arg.Any<InlineAlertRequest>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new InlineAlertResult(true, null)));

        var modalService = Substitute.For<IModalService>();
        modalService.TryGetActiveAlertHost(out Arg.Any<IInlineAlertHost?>()).Returns(call =>
        {
            call[0] = host;
            return true;
        });

        var standaloneCalled = false;
        var sut = new ModalAlertDialogService(
            modalService,
            PassthroughMainThread(),
            _ => { standaloneCalled = true; return Task.FromResult(false); },
            _ => Task.FromResult(string.Empty));

        // Act
        var result = await sut.ShowAlert("Confirm", "Are you sure?", "Yes", "No");

        // Assert
        Assert.True(result);
        Assert.False(standaloneCalled);
        await host.Received(1).ShowInlineAlertAsync(
            Arg.Is<InlineAlertRequest>(r =>
                r.Title == "Confirm" &&
                r.Message == "Are you sure?" &&
                r.AcceptLabel == "Yes" &&
                r.CancelLabel == "No" &&
                r.IsPrompt == false),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ShowAlertTwoButton_WhenInlineCancelled_ShouldReturnFalse()
    {
        // Arrange
        var host = Substitute.For<IInlineAlertHost>();
        host.ShowInlineAlertAsync(Arg.Any<InlineAlertRequest>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromException<InlineAlertResult>(new TaskCanceledException()));

        var modalService = Substitute.For<IModalService>();
        modalService.TryGetActiveAlertHost(out Arg.Any<IInlineAlertHost?>()).Returns(call =>
        {
            call[0] = host;
            return true;
        });

        var sut = new ModalAlertDialogService(
            modalService,
            PassthroughMainThread(),
            _ => Task.FromResult(false),
            _ => Task.FromResult(string.Empty));

        // Act
        var result = await sut.ShowAlert("Confirm", "Sure?", "Yes", "No");

        // Assert
        Assert.False(result);
    }

    private static IMainThreadService PassthroughMainThread() =>
        new MainThreadService(action => { action(); return Task.CompletedTask; });
}
