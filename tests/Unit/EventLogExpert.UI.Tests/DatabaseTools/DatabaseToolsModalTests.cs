// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using Bunit;
using EventLogExpert.UI.Menu;
using EventLogExpert.UI.Modal;
using EventLogExpert.UI.Tests.TestUtils;
using Microsoft.Extensions.DependencyInjection;

namespace EventLogExpert.UI.Tests.DatabaseTools;

public sealed class DatabaseToolsModalTests : BunitContext
{
    public DatabaseToolsModalTests()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;

        Services.AddBannerHostDependencies();
        Services.AddMenuServiceMock();
        Services.AddEventLogUIServices();
    }

    [Fact]
    public async Task DisposingActiveHost_ViaMenuHostDisposeAsync_UnregistersFromRegistry()
    {
        var registry = Services.GetRequiredService<IMenuHostRegistry>();
        var component = Render<ModalChrome>(parameters => parameters
            .Add(p => p.Title, "DatabaseTools-stand-in")
            .AddChildContent("<p>tab content</p>"));

        var activeHost = registry.ActiveHost;
        Assert.NotNull(activeHost);

        await activeHost!.DisposeAsync();

        Assert.Null(registry.ActiveHost);
    }

    [Fact]
    public async Task DisposingTopmostHost_RestoresPreviousAsActive()
    {
        var registry = Services.GetRequiredService<IMenuHostRegistry>();

        var first = Render<ModalChrome>(parameters => parameters
            .Add(p => p.Title, "first")
            .AddChildContent("<p>1</p>"));
        var firstHost = registry.ActiveHost;

        var second = Render<ModalChrome>(parameters => parameters
            .Add(p => p.Title, "second")
            .AddChildContent("<p>2</p>"));
        var secondHost = registry.ActiveHost;
        Assert.NotSame(firstHost, secondHost);

        await secondHost!.DisposeAsync();

        Assert.Same(firstHost, registry.ActiveHost);
    }

    [Fact]
    public void OpeningModalThroughModalChrome_RegistersInnerMenuHostAsActive()
    {
        var registry = Services.GetRequiredService<IMenuHostRegistry>();
        Assert.Null(registry.ActiveHost);

        var component = Render<ModalChrome>(parameters => parameters
            .Add(p => p.Title, "DatabaseTools-stand-in")
            .AddChildContent("<p>tab content</p>"));

        Assert.NotNull(registry.ActiveHost);
    }

    [Fact]
    public void StackedModals_TopmostBecomesActive()
    {
        var registry = Services.GetRequiredService<IMenuHostRegistry>();
        Assert.Null(registry.ActiveHost);

        var first = Render<ModalChrome>(parameters => parameters
            .Add(p => p.Title, "first")
            .AddChildContent("<p>1</p>"));
        var firstHost = registry.ActiveHost;
        Assert.NotNull(firstHost);

        var second = Render<ModalChrome>(parameters => parameters
            .Add(p => p.Title, "second")
            .AddChildContent("<p>2</p>"));

        Assert.NotNull(registry.ActiveHost);
        Assert.NotSame(firstHost, registry.ActiveHost);
    }
}
