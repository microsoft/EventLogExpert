// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Runtime.Alerts;
using EventLogExpert.Runtime.Banner;
using EventLogExpert.Runtime.Common.Threading;
using EventLogExpert.UI.Alerts;
using EventLogExpert.UI.Modal;
using NSubstitute;

namespace EventLogExpert.UI.Tests.Alerts;

public sealed class ModalAlertDialogServiceTests
{
    [Fact]
    public async Task DisplayPrompt_WhenActiveHost_ShouldRouteInlineAndReturnTypedValue()
    {
        var host = Substitute.For<IInlineAlertHost>();
        host.ShowInlineAlertAsync(Arg.Any<InlineAlertRequest>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new InlineAlertResult(true, "typed-value")));

        var coordinator = Substitute.For<IModalCoordinator>();
        coordinator.TryGetInlineAlertHost(out Arg.Any<IInlineAlertHost?>()).Returns(call =>
        {
            call[0] = host;
            return true;
        });

        var sut = new AlertDialogService(
            coordinator,
            PassthroughMainThread(),
            Substitute.For<IErrorBannerService>(),
            Substitute.For<IInfoBannerService>(),
            _ => Task.FromResult(false),
            _ => Task.FromResult(string.Empty));

        var result = await sut.DisplayPrompt("Rename", "Enter new name");

        Assert.Equal("typed-value", result);
        await host.Received(1).ShowInlineAlertAsync(
            Arg.Is<InlineAlertRequest>(r => r != null && r.IsPrompt && r.Title == "Rename" && r.Message == "Enter new name"),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task DisplayPrompt_WhenInlineCancelled_ShouldReturnEmptyString()
    {
        var host = Substitute.For<IInlineAlertHost>();
        host.ShowInlineAlertAsync(Arg.Any<InlineAlertRequest>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new InlineAlertResult(false, null)));

        var coordinator = Substitute.For<IModalCoordinator>();
        coordinator.TryGetInlineAlertHost(out Arg.Any<IInlineAlertHost?>()).Returns(call =>
        {
            call[0] = host;
            return true;
        });

        var sut = new AlertDialogService(
            coordinator,
            PassthroughMainThread(),
            Substitute.For<IErrorBannerService>(),
            Substitute.For<IInfoBannerService>(),
            _ => Task.FromResult(false),
            _ => Task.FromResult(string.Empty));

        var result = await sut.DisplayPrompt("Rename", "Enter new name");

        Assert.Equal(string.Empty, result);
    }

    [Fact]
    public async Task DisplayPrompt_WhenNoActiveHost_ShouldCallStandalonePromptOpener()
    {
        var coordinator = Substitute.For<IModalCoordinator>();
        coordinator.TryGetInlineAlertHost(out Arg.Any<IInlineAlertHost?>()).Returns(false);

        IReadOnlyDictionary<string, object?>? capturedPrompt = null;
        var sut = new AlertDialogService(
            coordinator,
            PassthroughMainThread(),
            Substitute.For<IErrorBannerService>(),
            Substitute.For<IInfoBannerService>(),
            _ => Task.FromResult(false),
            parameters => { capturedPrompt = parameters; return Task.FromResult("user-typed"); });

        var result = await sut.DisplayPrompt("Rename", "Enter new name", "old-value");

        Assert.Equal("user-typed", result);
        Assert.NotNull(capturedPrompt);
        Assert.Equal("Rename", capturedPrompt!["Title"]);
        Assert.Equal("Enter new name", capturedPrompt["Message"]);
        Assert.Equal("old-value", capturedPrompt["InitialValue"]);
    }

    [Fact]
    public async Task DisplayPrompt_WhenNoActiveHost_ShouldForwardValidatorToStandalonePrompt()
    {
        var coordinator = Substitute.For<IModalCoordinator>();
        coordinator.TryGetInlineAlertHost(out Arg.Any<IInlineAlertHost?>()).Returns(false);

        IReadOnlyDictionary<string, object?>? capturedPrompt = null;
        Func<string, string?> validate = value => string.IsNullOrWhiteSpace(value) ? "Required." : null;

        var sut = new AlertDialogService(
            coordinator,
            PassthroughMainThread(),
            Substitute.For<IErrorBannerService>(),
            Substitute.For<IInfoBannerService>(),
            _ => Task.FromResult(false),
            parameters => { capturedPrompt = parameters; return Task.FromResult("user-typed"); });

        await sut.DisplayPrompt("Rename", "Enter new name", "old-value", validate);

        Assert.NotNull(capturedPrompt);
        Assert.Same(validate, capturedPrompt!["Validate"]);
    }

    [Fact]
    public async Task ShowAlert_ShouldMarshalThroughMainThreadService()
    {
        var coordinator = Substitute.For<IModalCoordinator>();
        coordinator.TryGetInlineAlertHost(out Arg.Any<IInlineAlertHost?>()).Returns(false);

        var mainThread = Substitute.For<IMainThreadService>();
        mainThread.InvokeOnMainThreadAsync(Arg.Any<Func<Task>>())
            .Returns(call => call.ArgAt<Func<Task>>(0)());

        var sut = new AlertDialogService(
            coordinator,
            mainThread,
            Substitute.For<IErrorBannerService>(),
            Substitute.For<IInfoBannerService>(),
            _ => Task.FromResult(true),
            _ => Task.FromResult(string.Empty));

        await sut.ShowAlert("t", "m", "c");

        await mainThread.Received(1).InvokeOnMainThreadAsync(Arg.Any<Func<Task>>());
    }

    [Fact]
    public async Task ShowAlertOneButton_BannerPresentation_DoesNotMarshalThroughMainThreadService()
    {
        var infoBannerService = Substitute.For<IInfoBannerService>();
        var errorBannerService = Substitute.For<IErrorBannerService>();
        var mainThread = Substitute.For<IMainThreadService>();

        var sut = new AlertDialogService(
            Substitute.For<IModalCoordinator>(),
            mainThread,
            errorBannerService,
            infoBannerService,
            _ => Task.FromResult(false),
            _ => Task.FromResult(string.Empty));

        await sut.ShowAlert("t", "m", "OK", AlertPresentation.Banner);

        await mainThread.DidNotReceive().InvokeOnMainThreadAsync(Arg.Any<Func<Task>>());
        infoBannerService.Received(1).ReportInfoBanner("t", "m", BannerSeverity.Warning);
        errorBannerService.DidNotReceive().ReportError(
            Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string?>(), Arg.Any<Func<Task>?>());
    }

    [Fact]
    public async Task ShowAlertOneButton_BannerPresentation_RoutesToReportInfoBanner_WithWarningSeverity()
    {
        var infoBannerService = Substitute.For<IInfoBannerService>();
        var errorBannerService = Substitute.For<IErrorBannerService>();
        var coordinator = Substitute.For<IModalCoordinator>();
        var standaloneCalled = false;

        var sut = new AlertDialogService(
            coordinator,
            PassthroughMainThread(),
            errorBannerService,
            infoBannerService,
            _ => { standaloneCalled = true; return Task.FromResult(false); },
            _ => Task.FromResult(string.Empty));

        await sut.ShowAlert("Banner Title", "Banner Message", "OK", AlertPresentation.Banner);

        infoBannerService.Received(1).ReportInfoBanner("Banner Title", "Banner Message", BannerSeverity.Warning);
        errorBannerService.DidNotReceive().ReportError(
            Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string?>(), Arg.Any<Func<Task>?>());
        Assert.False(standaloneCalled);
        coordinator.DidNotReceive().TryGetInlineAlertHost(out Arg.Any<IInlineAlertHost?>());
    }

    [Fact]
    public async Task ShowAlertOneButton_InlineOnlyNoHost_ThrowsInvalidOperationException()
    {
        var coordinator = Substitute.For<IModalCoordinator>();
        coordinator.TryGetInlineAlertHost(out Arg.Any<IInlineAlertHost?>()).Returns(false);

        var sut = new AlertDialogService(
            coordinator,
            PassthroughMainThread(),
            Substitute.For<IErrorBannerService>(),
            Substitute.For<IInfoBannerService>(),
            _ => Task.FromResult(false),
            _ => Task.FromResult(string.Empty));

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            sut.ShowAlert("t", "m", "OK", AlertPresentation.InlineOnly));
    }

    [Fact]
    public async Task ShowAlertOneButton_InlineOnlyWithHost_RoutesInline()
    {
        var host = Substitute.For<IInlineAlertHost>();
        host.ShowInlineAlertAsync(Arg.Any<InlineAlertRequest>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new InlineAlertResult(true, null)));

        var coordinator = Substitute.For<IModalCoordinator>();
        coordinator.TryGetInlineAlertHost(out Arg.Any<IInlineAlertHost?>()).Returns(call =>
        {
            call[0] = host;
            return true;
        });

        var standaloneCalled = false;
        var sut = new AlertDialogService(
            coordinator,
            PassthroughMainThread(),
            Substitute.For<IErrorBannerService>(),
            Substitute.For<IInfoBannerService>(),
            _ => { standaloneCalled = true; return Task.FromResult(false); },
            _ => Task.FromResult(string.Empty));

        await sut.ShowAlert("t", "m", "OK", AlertPresentation.InlineOnly);

        Assert.False(standaloneCalled);
        await host.Received(1).ShowInlineAlertAsync(Arg.Any<InlineAlertRequest>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ShowAlertOneButton_PopupOnly_AlwaysOpensStandalone_EvenWithHost()
    {
        var host = Substitute.For<IInlineAlertHost>();
        var coordinator = Substitute.For<IModalCoordinator>();
        coordinator.TryGetInlineAlertHost(out Arg.Any<IInlineAlertHost?>()).Returns(call =>
        {
            call[0] = host;
            return true;
        });

        IReadOnlyDictionary<string, object?>? capturedAlert = null;
        var sut = new AlertDialogService(
            coordinator,
            PassthroughMainThread(),
            Substitute.For<IErrorBannerService>(),
            Substitute.For<IInfoBannerService>(),
            parameters => { capturedAlert = parameters; return Task.FromResult(true); },
            _ => Task.FromResult(string.Empty));

        await sut.ShowAlert("t", "m", "Close", AlertPresentation.PopupOnly);

        Assert.NotNull(capturedAlert);
        Assert.Equal("Close", capturedAlert!["CancelLabel"]);
        await host.DidNotReceive().ShowInlineAlertAsync(Arg.Any<InlineAlertRequest>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ShowAlertOneButton_WhenNoActiveHost_ShouldCallStandaloneOpener()
    {
        var coordinator = Substitute.For<IModalCoordinator>();
        coordinator.TryGetInlineAlertHost(out Arg.Any<IInlineAlertHost?>()).Returns(false);

        IReadOnlyDictionary<string, object?>? capturedAlert = null;
        var sut = new AlertDialogService(
            coordinator,
            PassthroughMainThread(),
            Substitute.For<IErrorBannerService>(),
            Substitute.For<IInfoBannerService>(),
            parameters => { capturedAlert = parameters; return Task.FromResult(true); },
            _ => Task.FromResult(string.Empty));

        await sut.ShowAlert("My Title", "My Message", "Close");

        Assert.NotNull(capturedAlert);
        Assert.Equal("My Title", capturedAlert!["Title"]);
        Assert.Equal("My Message", capturedAlert["Message"]);
        Assert.Null(capturedAlert["AcceptLabel"]);
        Assert.Equal("Close", capturedAlert["CancelLabel"]);
    }

    [Fact]
    public async Task ShowAlertTwoButton_BannerPresentation_ThrowsArgumentException()
    {
        var sut = new AlertDialogService(
            Substitute.For<IModalCoordinator>(),
            PassthroughMainThread(),
            Substitute.For<IErrorBannerService>(),
            Substitute.For<IInfoBannerService>(),
            _ => Task.FromResult(false),
            _ => Task.FromResult(string.Empty));

        var ex = await Assert.ThrowsAsync<ArgumentException>(() =>
            sut.ShowAlert("t", "m", "Yes", "No", AlertPresentation.Banner));
        Assert.Equal("presentation", ex.ParamName);
    }

    [Fact]
    public async Task ShowAlertTwoButton_InlineOnlyNoHost_ThrowsInvalidOperationException()
    {
        var coordinator = Substitute.For<IModalCoordinator>();
        coordinator.TryGetInlineAlertHost(out Arg.Any<IInlineAlertHost?>()).Returns(false);

        var sut = new AlertDialogService(
            coordinator,
            PassthroughMainThread(),
            Substitute.For<IErrorBannerService>(),
            Substitute.For<IInfoBannerService>(),
            _ => Task.FromResult(false),
            _ => Task.FromResult(string.Empty));

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            sut.ShowAlert("t", "m", "Yes", "No", AlertPresentation.InlineOnly));
    }

    [Fact]
    public async Task ShowAlertTwoButton_PopupOnly_AlwaysOpensStandalone()
    {
        var host = Substitute.For<IInlineAlertHost>();
        var coordinator = Substitute.For<IModalCoordinator>();
        coordinator.TryGetInlineAlertHost(out Arg.Any<IInlineAlertHost?>()).Returns(call =>
        {
            call[0] = host;
            return true;
        });

        IReadOnlyDictionary<string, object?>? capturedAlert = null;
        var sut = new AlertDialogService(
            coordinator,
            PassthroughMainThread(),
            Substitute.For<IErrorBannerService>(),
            Substitute.For<IInfoBannerService>(),
            parameters => { capturedAlert = parameters; return Task.FromResult(true); },
            _ => Task.FromResult(string.Empty));

        var result = await sut.ShowAlert("t", "m", "Yes", "No", AlertPresentation.PopupOnly);

        Assert.True(result);
        Assert.NotNull(capturedAlert);
        Assert.Equal("Yes", capturedAlert!["AcceptLabel"]);
        Assert.Equal("No", capturedAlert["CancelLabel"]);
        await host.DidNotReceive().ShowInlineAlertAsync(Arg.Any<InlineAlertRequest>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ShowAlertTwoButton_WhenActiveHost_ShouldRouteToHostInline()
    {
        var host = Substitute.For<IInlineAlertHost>();
        host.ShowInlineAlertAsync(Arg.Any<InlineAlertRequest>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new InlineAlertResult(true, null)));

        var coordinator = Substitute.For<IModalCoordinator>();
        coordinator.TryGetInlineAlertHost(out Arg.Any<IInlineAlertHost?>()).Returns(call =>
        {
            call[0] = host;
            return true;
        });

        var standaloneCalled = false;
        var sut = new AlertDialogService(
            coordinator,
            PassthroughMainThread(),
            Substitute.For<IErrorBannerService>(),
            Substitute.For<IInfoBannerService>(),
            _ => { standaloneCalled = true; return Task.FromResult(false); },
            _ => Task.FromResult(string.Empty));

        var result = await sut.ShowAlert("Confirm", "Are you sure?", "Yes", "No");

        Assert.True(result);
        Assert.False(standaloneCalled);
        await host.Received(1).ShowInlineAlertAsync(
            Arg.Is<InlineAlertRequest>(r =>
                r != null &&
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
        var host = Substitute.For<IInlineAlertHost>();
        host.ShowInlineAlertAsync(Arg.Any<InlineAlertRequest>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromException<InlineAlertResult>(new TaskCanceledException()));

        var coordinator = Substitute.For<IModalCoordinator>();
        coordinator.TryGetInlineAlertHost(out Arg.Any<IInlineAlertHost?>()).Returns(call =>
        {
            call[0] = host;
            return true;
        });

        var sut = new AlertDialogService(
            coordinator,
            PassthroughMainThread(),
            Substitute.For<IErrorBannerService>(),
            Substitute.For<IInfoBannerService>(),
            _ => Task.FromResult(false),
            _ => Task.FromResult(string.Empty));

        var result = await sut.ShowAlert("Confirm", "Sure?", "Yes", "No");

        Assert.False(result);
    }

    [Fact]
    public async Task ShowErrorAlert_DoesNotMarshalThroughMainThreadService()
    {
        var errorBannerService = Substitute.For<IErrorBannerService>();
        var infoBannerService = Substitute.For<IInfoBannerService>();
        var mainThread = Substitute.For<IMainThreadService>();

        var sut = new AlertDialogService(
            Substitute.For<IModalCoordinator>(),
            mainThread,
            errorBannerService,
            infoBannerService,
            _ => Task.FromResult(false),
            _ => Task.FromResult(string.Empty));

        await sut.ShowErrorAlert("t", "m");

        await mainThread.DidNotReceive().InvokeOnMainThreadAsync(Arg.Any<Func<Task>>());
        errorBannerService.Received(1).ReportError("t", "m");
        infoBannerService.DidNotReceive().ReportInfoBanner(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<BannerSeverity>());
    }

    [Fact]
    public async Task ShowErrorAlert_RoutesToBannerServiceReportError_WithTitleAndMessage()
    {
        var errorBannerService = Substitute.For<IErrorBannerService>();
        var infoBannerService = Substitute.For<IInfoBannerService>();
        var coordinator = Substitute.For<IModalCoordinator>();
        var standaloneCalled = false;

        var sut = new AlertDialogService(
            coordinator,
            PassthroughMainThread(),
            errorBannerService,
            infoBannerService,
            _ => { standaloneCalled = true; return Task.FromResult(false); },
            _ => Task.FromResult(string.Empty));

        await sut.ShowErrorAlert("Error Title", "Error Message");

        errorBannerService.Received(1).ReportError("Error Title", "Error Message");
        infoBannerService.DidNotReceive().ReportInfoBanner(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<BannerSeverity>());
        Assert.False(standaloneCalled);
        coordinator.DidNotReceive().TryGetInlineAlertHost(out Arg.Any<IInlineAlertHost?>());
    }

    [Fact]
    public async Task ShowErrorAlert_WithActionLabelAndAction_PassesThroughToBannerService()
    {
        var errorBannerService = Substitute.For<IErrorBannerService>();
        var infoBannerService = Substitute.For<IInfoBannerService>();
        Func<Task> action = () => Task.CompletedTask;

        var sut = new AlertDialogService(
            Substitute.For<IModalCoordinator>(),
            PassthroughMainThread(),
            errorBannerService,
            infoBannerService,
            _ => Task.FromResult(false),
            _ => Task.FromResult(string.Empty));

        await sut.ShowErrorAlert("Error Title", "Error Message", "Resolve", action);

        errorBannerService.Received(1).ReportError("Error Title", "Error Message", "Resolve", action);
        infoBannerService.DidNotReceive().ReportInfoBanner(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<BannerSeverity>());
    }

    private static IMainThreadService PassthroughMainThread() => new PassthroughMainThreadService();

    private sealed class PassthroughMainThreadService : IMainThreadService
    {
        public Task InvokeOnMainThread(Action action)
        {
            action();
            return Task.CompletedTask;
        }

        public Task InvokeOnMainThreadAsync(Func<Task> action) => action();
    }
}
