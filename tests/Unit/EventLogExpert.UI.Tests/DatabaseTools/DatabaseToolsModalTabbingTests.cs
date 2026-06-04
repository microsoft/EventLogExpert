// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using Bunit;
using EventLogExpert.Logging.Abstractions;
using EventLogExpert.Runtime.Alerts;
using EventLogExpert.Runtime.Announcement;
using EventLogExpert.Runtime.Banner;
using EventLogExpert.Runtime.Common.Clipboard;
using EventLogExpert.Runtime.Common.Files;
using EventLogExpert.Runtime.Common.Versioning;
using EventLogExpert.Runtime.Database;
using EventLogExpert.Runtime.DatabaseTools;
using EventLogExpert.Runtime.DatabaseTools.Elevation;
using EventLogExpert.Runtime.EventLog;
using EventLogExpert.Runtime.Menu;
using EventLogExpert.Runtime.Modal;
using EventLogExpert.UI.DatabaseTools;
using EventLogExpert.UI.Tests.DatabaseTools.Tabs;
using EventLogExpert.UI.Tests.TestUtils;
using Fluxor;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;

namespace EventLogExpert.UI.Tests.DatabaseTools;

public sealed class DatabaseToolsModalTabbingTests : BunitContext
{
    private readonly IAnnouncementService _announcementService = Substitute.For<IAnnouncementService>();
    private readonly IDatabaseOperationCoordinator _coordinator = Substitute.For<IDatabaseOperationCoordinator>();
    private readonly FakeDatabaseService _databaseService = new();
    private readonly ILogReloadCoordinator _logReloadCoordinator = Substitute.For<ILogReloadCoordinator>();
    private readonly IModalCoordinator _modalCoordinator = Substitute.For<IModalCoordinator>();
    private readonly ModalId _modalId = new(1L);
    private readonly IModalService _modalService = Substitute.For<IModalService>();
    private readonly IProgressBannerService _progressBannerService = Substitute.For<IProgressBannerService>();
    private readonly ITraceLogger _traceLogger = Substitute.For<ITraceLogger>();

    public DatabaseToolsModalTabbingTests()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;

        _progressBannerService.ManageDatabasesProgress.Returns((BannerProgressEntry?)null);
        _progressBannerService.BackgroundProgress.Returns((BannerProgressEntry?)null);
        _logReloadCoordinator.HasActiveLogs.Returns(false);
        _modalService.ActiveModalId.Returns(_modalId);

        Services.AddBannerHostDependencies();
        Services.AddMenuMocks();

        Services.AddSingleton(_announcementService);
        Services.AddSingleton(_coordinator);
        Services.AddSingleton<IDatabaseService>(_databaseService);
        Services.AddSingleton(_logReloadCoordinator);
        Services.AddSingleton(_progressBannerService);
        Services.AddSingleton(_traceLogger);
        Services.AddSingleton(_modalCoordinator);
        Services.AddSingleton(_modalService);

        Services.AddSingleton(Substitute.For<IDatabaseToolsService>());
        Services.AddSingleton(Substitute.For<IElevatedDatabaseToolsRunner>());
        Services.AddSingleton(Substitute.For<IFilePickerService>());
        Services.AddSingleton(Substitute.For<IFileSaveService>());
        Services.AddSingleton(Substitute.For<IClipboardService>());
        Services.AddSingleton(Substitute.For<IAlertDialogService>());
        Services.AddSingleton(Substitute.For<ICurrentVersionProvider>());
        Services.AddSingleton(Substitute.For<IMenuActionService>());

        Services.AddFluxor(options => options.ScanAssemblies(typeof(DatabaseToolsModal).Assembly));
    }

    [Fact]
    public void ActiveTabpanel_HasNoDisplayNone()
    {
        var component = Render<DatabaseToolsModal>();

        var panels = component.FindAll("[role='tabpanel']");
        var activeStyle = panels[0].GetAttribute("style") ?? string.Empty;
        Assert.DoesNotContain("display: none", activeStyle);
    }

    [Fact]
    public async Task ArrowDown_FromManage_ActivatesShow()
    {
        var component = Render<DatabaseToolsModal>();

        await component.FindAll("[role='tab']")[0]
            .KeyDownAsync(new KeyboardEventArgs { Key = "ArrowDown" });

        Assert.Equal("true", component.FindAll("[role='tab']")[1].GetAttribute("aria-selected"));
    }

    [Fact]
    public async Task ArrowLeft_FromShow_IsIgnored_StaysOnShow()
    {
        var component = Render<DatabaseToolsModal>();
        await component.FindAll("[role='tab']")[1].ClickAsync(new MouseEventArgs());

        await component.FindAll("[role='tab']")[1]
            .KeyDownAsync(new KeyboardEventArgs { Key = "ArrowLeft" });

        Assert.Equal("true", component.FindAll("[role='tab']")[1].GetAttribute("aria-selected"));
    }

    [Fact]
    public async Task ArrowRight_FromManage_IsIgnored_StaysOnManage()
    {
        var component = Render<DatabaseToolsModal>();

        await component.FindAll("[role='tab']")[0]
            .KeyDownAsync(new KeyboardEventArgs { Key = "ArrowRight" });

        Assert.Equal("true", component.FindAll("[role='tab']")[0].GetAttribute("aria-selected"));
    }

    [Fact]
    public async Task ArrowUp_FromShow_ActivatesManage()
    {
        var component = Render<DatabaseToolsModal>();
        await component.FindAll("[role='tab']")[1].ClickAsync(new MouseEventArgs());

        await component.FindAll("[role='tab']")[1]
            .KeyDownAsync(new KeyboardEventArgs { Key = "ArrowUp" });

        Assert.Equal("true", component.FindAll("[role='tab']")[0].GetAttribute("aria-selected"));
    }

    [Fact]
    public async Task End_FromManage_ActivatesUpgrade()
    {
        var component = Render<DatabaseToolsModal>();

        await component.FindAll("[role='tab']")[0]
            .KeyDownAsync(new KeyboardEventArgs { Key = "End" });

        Assert.Equal("true", component.FindAll("[role='tab']")[5].GetAttribute("aria-selected"));
    }

    [Fact]
    public async Task Home_FromUpgrade_ActivatesManage()
    {
        var component = Render<DatabaseToolsModal>();
        await component.FindAll("[role='tab']")[5].ClickAsync(new MouseEventArgs());

        await component.FindAll("[role='tab']")[5]
            .KeyDownAsync(new KeyboardEventArgs { Key = "Home" });

        Assert.Equal("true", component.FindAll("[role='tab']")[0].GetAttribute("aria-selected"));
    }

    [Fact]
    public void InactiveTabpanels_HaveDisplayNoneInlineStyle()
    {
        var component = Render<DatabaseToolsModal>();

        var panels = component.FindAll("[role='tabpanel']");
        Assert.Equal(6, panels.Count);
        for (int i = 1; i < panels.Count; i++)
        {
            var style = panels[i].GetAttribute("style") ?? string.Empty;
            Assert.Contains("display: none", style);
        }
    }

    [Fact]
    public void InitialRender_HasManageActive_AndAriaSelectedTrue()
    {
        var component = Render<DatabaseToolsModal>();

        var tabs = component.FindAll("[role='tab']");
        Assert.Equal(6, tabs.Count);
        Assert.Equal("true", tabs[0].GetAttribute("aria-selected"));
        Assert.Equal("Manage", tabs[0].TextContent.Trim());
        for (int i = 1; i < tabs.Count; i++)
        {
            Assert.Equal("false", tabs[i].GetAttribute("aria-selected"));
        }
    }

    [Fact]
    public async Task TabClick_OnShow_ActivatesShow_AndUpdatesAriaSelected()
    {
        var component = Render<DatabaseToolsModal>();

        await component.FindAll("[role='tab']")[1].ClickAsync(new MouseEventArgs());

        var tabs = component.FindAll("[role='tab']");
        Assert.Equal("false", tabs[0].GetAttribute("aria-selected"));
        Assert.Equal("true", tabs[1].GetAttribute("aria-selected"));
    }
}
