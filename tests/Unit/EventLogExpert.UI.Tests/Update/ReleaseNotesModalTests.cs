// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using Bunit;
using EventLogExpert.Localization;
using EventLogExpert.Runtime.Update.ReleaseNotes;
using EventLogExpert.UI.Modal;
using EventLogExpert.UI.Tests.TestUtils;
using EventLogExpert.UI.Update;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Localization;
using NSubstitute;

namespace EventLogExpert.UI.Tests.Update;

/// <summary>
///     Verifies the release-notes modal composes its heading + accessible name through the shared localizer (Runtime
///     supplies only the raw version as data). Uses <see cref="MarkerLocalizer" /> so the assertions pin the keys.
/// </summary>
public sealed class ReleaseNotesModalTests : BunitContext
{
    public ReleaseNotesModalTests()
    {
        Services.AddBannerHostDependencies();
        Services.AddMenuMocks();

        var modalService = Substitute.For<IModalService>();
        modalService.ActiveModalId.Returns(new ModalId(1L));
        Services.AddSingleton(modalService);
        Services.AddSingleton(Substitute.For<IModalCoordinator>());
        Services.AddSingleton<IStringLocalizer<SharedResource>>(new MarkerLocalizer());

        JSInterop.Mode = JSRuntimeMode.Loose;
    }

    [Fact]
    public void Render_ComposesLocalizedTitleWithVersion()
    {
        var component = Render<ReleaseNotesModal>(parameters => parameters
            .Add(modal => modal.Content, new ReleaseNotesContent("1.2.3", "## Body")));

        Assert.Contains("[[ReleaseNotes_TitleWithVersion(1.2.3)]]", component.Markup);
    }

    [Fact]
    public void Render_UsesReleaseNotesAriaLabelForDialog()
    {
        var component = Render<ReleaseNotesModal>(parameters => parameters
            .Add(modal => modal.Content, new ReleaseNotesContent("1.2.3", "## Body")));

        Assert.Equal("[[ReleaseNotes_AriaLabel]]", component.Find("dialog").GetAttribute("aria-label"));
    }
}
