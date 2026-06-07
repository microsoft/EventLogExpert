// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using Bunit;
using EventLogExpert.Runtime.Menu;
using EventLogExpert.UI.Menu;
using EventLogExpert.UI.Modal;
using EventLogExpert.UI.Tests.TestUtils;
using Microsoft.Extensions.DependencyInjection;
using TestContext = Xunit.TestContext;

namespace EventLogExpert.UI.Tests.Menu;

public sealed class MenuHostHandoffTests : BunitContext
{
    private readonly FakeMenuService _menuService = new();
    private readonly BunitJSModuleInterop _overlayModule;

    public MenuHostHandoffTests()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        _overlayModule = JSInterop.SetupModule("./_content/EventLogExpert.UI/Menu/MenuOverlay.js");

        Services.AddBannerHostDependencies();
        Services.AddSingleton<IMenuService>(_menuService);
        Services.AddEventLogUiServices();
    }

    [Fact]
    public async Task SecondHostRegistersWhileMenuOpen_ClosingMenu_DetachesViewportListenersExactlyOnce()
    {
        var registry = Services.GetRequiredService<IMenuHostRegistry>();

        var firstModal = Render<ModalChrome>(parameters => parameters
            .Add(p => p.Title, "first")
            .AddChildContent("<p>1</p>"));
        var hostA = registry.ActiveHost;
        Assert.NotNull(hostA);

        await firstModal.InvokeAsync(() => _menuService.Open(MakeItems()));
        await WaitForJsInvocationAsync("attachMenuViewportListeners");
        Assert.Equal(1, _overlayModule.Invocations.Count(i => i.Identifier == "attachMenuViewportListeners"));

        var secondModal = Render<ModalChrome>(parameters => parameters
            .Add(p => p.Title, "second")
            .AddChildContent("<p>2</p>"));
        var hostB = registry.ActiveHost;
        Assert.NotSame(hostA, hostB);

        await secondModal.InvokeAsync(() => _menuService.Close());
        await WaitForJsInvocationAsync("detachMenuViewportListeners");

        Assert.Equal(1, _overlayModule.Invocations.Count(i => i.Identifier == "detachMenuViewportListeners"));
    }

    [Fact]
    public async Task SecondHostRegistersWhileMenuOpen_DisposingFirstAfterClose_DoesNotDoubleDetach()
    {
        var registry = Services.GetRequiredService<IMenuHostRegistry>();

        var firstModal = Render<ModalChrome>(parameters => parameters
            .Add(p => p.Title, "first")
            .AddChildContent("<p>1</p>"));
        var hostA = registry.ActiveHost;
        Assert.NotNull(hostA);

        await firstModal.InvokeAsync(() => _menuService.Open(MakeItems()));
        await WaitForJsInvocationAsync("attachMenuViewportListeners");

        var secondModal = Render<ModalChrome>(parameters => parameters
            .Add(p => p.Title, "second")
            .AddChildContent("<p>2</p>"));
        var hostB = registry.ActiveHost;
        Assert.NotSame(hostA, hostB);

        await secondModal.InvokeAsync(() => _menuService.Close());
        await WaitForJsInvocationAsync("detachMenuViewportListeners");

        var detachAfterClose = _overlayModule.Invocations.Count(i => i.Identifier == "detachMenuViewportListeners");

        await hostA!.DisposeAsync();

        var detachAfterDispose = _overlayModule.Invocations.Count(i => i.Identifier == "detachMenuViewportListeners");
        Assert.Equal(detachAfterClose, detachAfterDispose);
    }

    private static IReadOnlyList<MenuItem> MakeItems() =>
        new List<MenuItem> { MenuItem.Item("Sample", () => Task.CompletedTask) };

    private async Task WaitForJsInvocationAsync(string identifier)
    {
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(2);
        while (DateTime.UtcNow < deadline)
        {
            if (_overlayModule.Invocations.Any(i => i.Identifier == identifier)) { return; }
            await Task.Delay(25, TestContext.Current.CancellationToken);
        }
        Assert.Fail($"Expected '{identifier}' JS interop call did not occur within timeout. Observed: [{string.Join(",", _overlayModule.Invocations.Select(i => i.Identifier))}]");
    }

    private sealed class FakeMenuService : IMenuService
    {
        private long _nextId;

        public event Action<int>? NavigateBarRequested;

        public event Action? StateChanged;

        public bool ActiveCaptureOpener { get; private set; } = true;

        public bool ActiveFocusFirst { get; private set; } = true;

        public IReadOnlyList<MenuItem>? ActiveItems { get; private set; }

        public long ActiveMenuId { get; private set; }

        public double PositionX { get; private set; }

        public double PositionY { get; private set; }

        public void Close()
        {
            if (ActiveItems is null) { return; }

            ActiveItems = null;
            ActiveMenuId = 0;
            PositionX = 0;
            PositionY = 0;
            ActiveCaptureOpener = true;
            ActiveFocusFirst = true;
            StateChanged?.Invoke();
        }

        public void NavigateBar(int direction) => NavigateBarRequested?.Invoke(direction);

        public void Open(IReadOnlyList<MenuItem> items, double x = 10, double y = 10) =>
            OpenAt(x, y, items);

        public void OpenAt(
            double x,
            double y,
            IReadOnlyList<MenuItem> items,
            bool focusFirst = true,
            bool captureOpener = true)
        {
            ArgumentNullException.ThrowIfNull(items);

            _nextId++;
            ActiveMenuId = _nextId;
            ActiveItems = items;
            PositionX = x;
            PositionY = y;
            ActiveFocusFirst = focusFirst;
            ActiveCaptureOpener = captureOpener;
            StateChanged?.Invoke();
        }
    }
}
