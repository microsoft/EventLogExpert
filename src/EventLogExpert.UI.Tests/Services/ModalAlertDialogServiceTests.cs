// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.UI.Interfaces;
using EventLogExpert.UI.Models;
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
            Substitute.For<IBannerService>(),
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
            Substitute.For<IBannerService>(),
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
            Substitute.For<IBannerService>(),
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
            Substitute.For<IBannerService>(),
            _ => Task.FromResult(true),
            _ => Task.FromResult(string.Empty));

        // Act
        await sut.ShowAlert("t", "m", "c");

        // Assert
        await mainThread.Received(1).InvokeOnMainThreadAsync(Arg.Any<Func<Task>>());
    }

    [Fact]
    public async Task ShowAlertOneButton_BannerPresentation_DoesNotMarshalThroughMainThreadService()
    {
        // Arrange — banner-routed alerts skip the UI-thread marshal because the banner service is thread-safe.
        var bannerService = Substitute.For<IBannerService>();
        var mainThread = Substitute.For<IMainThreadService>();

        var sut = new ModalAlertDialogService(
            Substitute.For<IModalService>(),
            mainThread,
            bannerService,
            _ => Task.FromResult(false),
            _ => Task.FromResult(string.Empty));

        // Act
        await sut.ShowAlert("t", "m", "OK", AlertPresentation.Banner);

        // Assert
        await mainThread.DidNotReceive().InvokeOnMainThreadAsync(Arg.Any<Func<Task>>());
        bannerService.Received(1).ReportInfoBanner("t", "m", BannerSeverity.Warning);
    }

    [Fact]
    public async Task ShowAlertOneButton_BannerPresentation_RoutesToReportInfoBanner_WithWarningSeverity()
    {
        // Arrange
        var bannerService = Substitute.For<IBannerService>();
        var modalService = Substitute.For<IModalService>();
        var standaloneCalled = false;

        var sut = new ModalAlertDialogService(
            modalService,
            PassthroughMainThread(),
            bannerService,
            _ => { standaloneCalled = true; return Task.FromResult(false); },
            _ => Task.FromResult(string.Empty));

        // Act
        await sut.ShowAlert("Banner Title", "Banner Message", "OK", AlertPresentation.Banner);

        // Assert
        bannerService.Received(1).ReportInfoBanner("Banner Title", "Banner Message", BannerSeverity.Warning);
        Assert.False(standaloneCalled);
        modalService.DidNotReceive().TryGetActiveAlertHost(out Arg.Any<IInlineAlertHost?>());
    }

    [Fact]
    public async Task ShowAlertOneButton_InlineOnlyNoHost_ThrowsInvalidOperationException()
    {
        // Arrange
        var modalService = Substitute.For<IModalService>();
        modalService.TryGetActiveAlertHost(out Arg.Any<IInlineAlertHost?>()).Returns(false);

        var sut = new ModalAlertDialogService(
            modalService,
            PassthroughMainThread(),
            Substitute.For<IBannerService>(),
            _ => Task.FromResult(false),
            _ => Task.FromResult(string.Empty));

        // Act + Assert
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            sut.ShowAlert("t", "m", "OK", AlertPresentation.InlineOnly));
    }

    [Fact]
    public async Task ShowAlertOneButton_InlineOnlyWithHost_RoutesInline()
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
            Substitute.For<IBannerService>(),
            _ => { standaloneCalled = true; return Task.FromResult(false); },
            _ => Task.FromResult(string.Empty));

        // Act
        await sut.ShowAlert("t", "m", "OK", AlertPresentation.InlineOnly);

        // Assert
        Assert.False(standaloneCalled);
        await host.Received(1).ShowInlineAlertAsync(Arg.Any<InlineAlertRequest>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ShowAlertOneButton_PopupOnly_AlwaysOpensStandalone_EvenWithHost()
    {
        // Arrange
        var host = Substitute.For<IInlineAlertHost>();
        var modalService = Substitute.For<IModalService>();
        modalService.TryGetActiveAlertHost(out Arg.Any<IInlineAlertHost?>()).Returns(call =>
        {
            call[0] = host;
            return true;
        });

        IReadOnlyDictionary<string, object?>? capturedAlert = null;
        var sut = new ModalAlertDialogService(
            modalService,
            PassthroughMainThread(),
            Substitute.For<IBannerService>(),
            parameters => { capturedAlert = parameters; return Task.FromResult(true); },
            _ => Task.FromResult(string.Empty));

        // Act
        await sut.ShowAlert("t", "m", "Close", AlertPresentation.PopupOnly);

        // Assert
        Assert.NotNull(capturedAlert);
        Assert.Equal("Close", capturedAlert!["CancelLabel"]);
        await host.DidNotReceive().ShowInlineAlertAsync(Arg.Any<InlineAlertRequest>(), Arg.Any<CancellationToken>());
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
            Substitute.For<IBannerService>(),
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
    public async Task ShowAlertTwoButton_BannerPresentation_ThrowsArgumentException()
    {
        // Arrange
        var sut = new ModalAlertDialogService(
            Substitute.For<IModalService>(),
            PassthroughMainThread(),
            Substitute.For<IBannerService>(),
            _ => Task.FromResult(false),
            _ => Task.FromResult(string.Empty));

        // Act + Assert — Banner is not valid for accept/cancel pairs.
        var ex = await Assert.ThrowsAsync<ArgumentException>(() =>
            sut.ShowAlert("t", "m", "Yes", "No", AlertPresentation.Banner));
        Assert.Equal("presentation", ex.ParamName);
    }

    [Fact]
    public async Task ShowAlertTwoButton_InlineOnlyNoHost_ThrowsInvalidOperationException()
    {
        // Arrange
        var modalService = Substitute.For<IModalService>();
        modalService.TryGetActiveAlertHost(out Arg.Any<IInlineAlertHost?>()).Returns(false);

        var sut = new ModalAlertDialogService(
            modalService,
            PassthroughMainThread(),
            Substitute.For<IBannerService>(),
            _ => Task.FromResult(false),
            _ => Task.FromResult(string.Empty));

        // Act + Assert
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            sut.ShowAlert("t", "m", "Yes", "No", AlertPresentation.InlineOnly));
    }

    [Fact]
    public async Task ShowAlertTwoButton_PopupOnly_AlwaysOpensStandalone()
    {
        // Arrange
        var host = Substitute.For<IInlineAlertHost>();
        var modalService = Substitute.For<IModalService>();
        modalService.TryGetActiveAlertHost(out Arg.Any<IInlineAlertHost?>()).Returns(call =>
        {
            call[0] = host;
            return true;
        });

        IReadOnlyDictionary<string, object?>? capturedAlert = null;
        var sut = new ModalAlertDialogService(
            modalService,
            PassthroughMainThread(),
            Substitute.For<IBannerService>(),
            parameters => { capturedAlert = parameters; return Task.FromResult(true); },
            _ => Task.FromResult(string.Empty));

        // Act
        var result = await sut.ShowAlert("t", "m", "Yes", "No", AlertPresentation.PopupOnly);

        // Assert
        Assert.True(result);
        Assert.NotNull(capturedAlert);
        Assert.Equal("Yes", capturedAlert!["AcceptLabel"]);
        Assert.Equal("No", capturedAlert["CancelLabel"]);
        await host.DidNotReceive().ShowInlineAlertAsync(Arg.Any<InlineAlertRequest>(), Arg.Any<CancellationToken>());
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
            Substitute.For<IBannerService>(),
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
            Substitute.For<IBannerService>(),
            _ => Task.FromResult(false),
            _ => Task.FromResult(string.Empty));

        // Act
        var result = await sut.ShowAlert("Confirm", "Sure?", "Yes", "No");

        // Assert
        Assert.False(result);
    }

    [Fact]
    public async Task ShowCriticalAlert_DoesNotMarshalThroughMainThreadService()
    {
        // Arrange — critical alerts go straight to the thread-safe banner service; no UI marshal needed.
        var bannerService = Substitute.For<IBannerService>();
        var mainThread = Substitute.For<IMainThreadService>();

        var sut = new ModalAlertDialogService(
            Substitute.For<IModalService>(),
            mainThread,
            bannerService,
            _ => Task.FromResult(false),
            _ => Task.FromResult(string.Empty));

        // Act
        await sut.ShowCriticalAlert("t", "m");

        // Assert
        await mainThread.DidNotReceive().InvokeOnMainThreadAsync(Arg.Any<Func<Task>>());
        bannerService.Received(1).ReportCritical("t", "m");
    }

    [Fact]
    public async Task ShowCriticalAlert_RoutesToBannerServiceReportCritical_WithTitleAndMessage()
    {
        // Arrange
        var bannerService = Substitute.For<IBannerService>();
        var modalService = Substitute.For<IModalService>();
        var standaloneCalled = false;

        var sut = new ModalAlertDialogService(
            modalService,
            PassthroughMainThread(),
            bannerService,
            _ => { standaloneCalled = true; return Task.FromResult(false); },
            _ => Task.FromResult(string.Empty));

        // Act
        await sut.ShowCriticalAlert("Critical Title", "Critical Message");

        // Assert
        bannerService.Received(1).ReportCritical("Critical Title", "Critical Message");
        Assert.False(standaloneCalled);
        modalService.DidNotReceive().TryGetActiveAlertHost(out Arg.Any<IInlineAlertHost?>());
    }

    private static IMainThreadService PassthroughMainThread() =>
        new MainThreadService(action => { action(); return Task.CompletedTask; });
}
