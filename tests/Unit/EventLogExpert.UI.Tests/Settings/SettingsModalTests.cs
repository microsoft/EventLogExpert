// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using Bunit;
using EventLogExpert.Logging.Abstractions;
using EventLogExpert.Runtime.Announcement;
using EventLogExpert.Runtime.Banner;
using EventLogExpert.Runtime.Database;
using EventLogExpert.Runtime.DetailsPane;
using EventLogExpert.Runtime.EventLog;
using EventLogExpert.Runtime.Modal;
using EventLogExpert.Runtime.Settings;
using EventLogExpert.UI.Settings;
using Fluxor;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;

namespace EventLogExpert.UI.Tests.Settings;

public sealed class SettingsModalTests : BunitContext
{
    private readonly IAnnouncementService _announcementService = Substitute.For<IAnnouncementService>();
    private readonly IDatabaseOperationCoordinator _coordinator = Substitute.For<IDatabaseOperationCoordinator>();
    private readonly IDatabaseService _databaseService = Substitute.For<IDatabaseService>();
    private readonly IDetailsPanePreferencesProvider _detailsPanePreferences = Substitute.For<IDetailsPanePreferencesProvider>();
    private readonly ILogReloadCoordinator _logReloadCoordinator = Substitute.For<ILogReloadCoordinator>();
    private readonly IModalCoordinator _modalCoordinator = Substitute.For<IModalCoordinator>();
    private readonly IModalService _modalService = Substitute.For<IModalService>();
    private readonly IProgressBannerService _progressBannerService = Substitute.For<IProgressBannerService>();
    private readonly ISettingsService _settings = Substitute.For<ISettingsService>();
    private readonly ITraceLogger _traceLogger = Substitute.For<ITraceLogger>();

    public SettingsModalTests()
    {
        _modalService.ActiveModalId.Returns(new ModalId(1L));

        _databaseService.Entries.Returns([]);
        _databaseService.InitialClassificationTask.Returns(Task.CompletedTask);
        _progressBannerService.SettingsProgress.Returns((BannerProgressEntry?)null);
        _settings.TimeZoneId.Returns(string.Empty);

        Services.AddSingleton(_announcementService);
        Services.AddSingleton(_coordinator);
        Services.AddSingleton(_databaseService);
        Services.AddSingleton(_detailsPanePreferences);
        Services.AddSingleton(_logReloadCoordinator);
        Services.AddSingleton(_modalCoordinator);
        Services.AddSingleton(_modalService);
        Services.AddSingleton(_progressBannerService);
        Services.AddSingleton(_settings);
        Services.AddSingleton(_traceLogger);
        Services.AddFluxor(options => options.ScanAssemblies(typeof(SettingsModal).Assembly));

        JSInterop.Mode = JSRuntimeMode.Loose;
    }

    [Fact]
    public void SettingsModal_HiddenSectionHeadingsExist_ForScreenReaderNavigation()
    {
        var component = Render<SettingsModal>();

        var hiddenHeadings = component.FindAll("h3.visually-hidden");
        Assert.Equal(2, hiddenHeadings.Count);
        Assert.Equal("Databases", hiddenHeadings[0].TextContent.Trim());
        Assert.Equal("Preferences", hiddenHeadings[1].TextContent.Trim());
    }

    [Fact]
    public void SettingsModal_ImportDatabaseButton_ReceivesInitialFocus()
    {
        // SettingsModal.razor.cs OnAfterRenderAsync(firstRender) calls _importButtonRef.FocusAsync().
        // bUnit Loose JS interop no-ops ElementReference.FocusAsync; this test verifies the structural
        // prerequisite: the focus target exists with the expected id so the focus call has a target.
        var component = Render<SettingsModal>();

        var importButton = component.Find("#settings-import-button");
        Assert.Equal("button", importButton.LocalName);
    }

    [Fact]
    public async Task SettingsModal_ImportSuccess_AnnouncesDatabaseImportedNotSettingsSaved()
    {
        // ImportOutcome positional ctor: ImportedCount=1 yields DatabaseStateChanged=true via the
        // computed property. The split SaveSettingsAsync/announce paths ensure import announces
        // "Database imported" (not "Settings saved") so SR users hear the user-initiated action.
        _coordinator.ImportAsync(Arg.Any<Func<string, CancellationToken, Task<bool>>?>(), Arg.Any<CancellationToken>())
            .Returns(new ImportOutcome(1, [], []));

        var component = Render<SettingsModal>();
        await component.Find("#settings-import-button").ClickAsync(new MouseEventArgs());

        _announcementService.Received(1).Announce("Database imported");
        _announcementService.DidNotReceive().Announce("Settings saved");
    }

    [Fact]
    public void SettingsModal_NoDatabasesAndClassificationComplete_RendersEmptyStateChild()
    {
        var component = Render<SettingsModal>();

        Assert.Single(component.FindAll(".settings-databases-empty"));
        Assert.Empty(component.FindAll(".db-entry-row"));
    }

    [Fact]
    public void SettingsModal_NoDatabasesButClassificationPending_SuppressesEmptyStateChild()
    {
        _databaseService.InitialClassificationTask.Returns(new TaskCompletionSource().Task);

        var component = Render<SettingsModal>();

        Assert.Empty(component.FindAll(".settings-databases-empty"));
        Assert.Single(component.FindAll(".classification-pending"));
    }

    [Fact]
    public async Task SettingsModal_OnSaveSuccess_AnnouncesSettingsSaved()
    {
        var component = Render<SettingsModal>();
        // Internal test-only forwarder bypasses ModalChrome footer markup coupling.
        await component.InvokeAsync(() => component.Instance.InvokeOnSaveAsyncForTests());

        _announcementService.Received(1).Announce("Settings saved");
    }

    [Fact]
    public void SettingsModal_PreReleaseBuilds_LivesInFooterNotBody()
    {
        var component = Render<SettingsModal>();

        var footerExtra = component.Find(".footer-extra");
        Assert.Contains("Pre-release Builds", footerExtra.TextContent);
    }

    [Fact]
    public void SettingsModal_TimeZone_PrecedesDatabasesRow_InMarkupOrder()
    {
        var component = Render<SettingsModal>();

        var markup = component.Markup;
        var tzIdx = markup.IndexOf("class=\"flex-center-aligned-row tz-row\"", StringComparison.Ordinal);
        var databasesHeadingIdx = markup.IndexOf(">Databases<", StringComparison.Ordinal);
        Assert.True(tzIdx >= 0, "Time Zone row not found in markup");
        Assert.True(databasesHeadingIdx > tzIdx,
            $"Time Zone row at index {tzIdx} must precede Databases heading at index {databasesHeadingIdx}");
    }

    [Fact]
    public void SettingsModal_TimeZoneInput_HasLabelForAssociation()
    {
        var component = Render<SettingsModal>();

        var label = component.Find("label[for='settings-timezone']");
        Assert.Equal("Time Zone:", label.TextContent.Trim());
        var input = component.Find("#settings-timezone");
        Assert.Equal("settings-timezone", input.Id);
    }

    [Fact]
    public void SettingsModal_UsesAriaLabelNotTitleForUtilityModalConvention()
    {
        var component = Render<SettingsModal>();

        var dialog = component.Find("dialog");
        Assert.Equal("Settings", dialog.GetAttribute("aria-label"));
        Assert.False(dialog.HasAttribute("aria-labelledby"));
    }
}
