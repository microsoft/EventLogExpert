// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Runtime.Common.AppTitle;
using EventLogExpert.UI.Common.AppTitle;
using EventLogExpert.UI.Tests.TestUtils;
using NSubstitute;

namespace EventLogExpert.UI.Tests.Common.AppTitle;

/// <summary>
///     Verifies the UI composer localizes the window-title qualifiers/progress through the shared localizer and
///     composes them around the (English) product name, version, and log name in the expected order/separators. Uses
///     <see cref="MarkerLocalizer" /> so the assertions pin which key drives each segment; the byte-exact English values
///     are guarded separately by the localization infrastructure tests.
/// </summary>
public sealed class AppTitleTextComposerTests
{
    private const string LogName = "Application";
    private const string Version = "23.1.1.1";

    [Fact]
    public void Compose_WhenDevBuildAndAdmin_ShouldSetTitle()
    {
        var titleProvider = Compose(new AppTitleState(
            Progress: null, IsDevBuild: true, IsPrerelease: false, IsAdmin: true, Version, LogName: null));

        titleProvider.Received(1).SetTitle(
            "EventLogExpert[[AppTitle_Qualifier_Development]][[AppTitle_Qualifier_Admin]]");
    }

    [Fact]
    public void Compose_WhenDevBuild_ShouldSetTitle()
    {
        var titleProvider = Compose(new AppTitleState(
            Progress: null, IsDevBuild: true, IsPrerelease: false, IsAdmin: false, Version, LogName: null));

        titleProvider.Received(1).SetTitle("EventLogExpert[[AppTitle_Qualifier_Development]]");
    }

    [Fact]
    public void Compose_WhenInstallingProgressAndAdmin_ShouldSetTitle()
    {
        var titleProvider = Compose(new AppTitleState(
            Progress: new AppTitleProgress.Installing(50),
            IsDevBuild: false, IsPrerelease: false, IsAdmin: true, Version, LogName: null));

        titleProvider.Received(1).SetTitle(
            "[[AppTitle_Progress_Installing(50)]] - EventLogExpert 23.1.1.1[[AppTitle_Qualifier_Admin]]");
    }

    [Fact]
    public void Compose_WhenInstallingProgressAndLogName_ShouldSetTitle()
    {
        var titleProvider = Compose(new AppTitleState(
            Progress: new AppTitleProgress.Installing(50),
            IsDevBuild: false, IsPrerelease: false, IsAdmin: false, Version, LogName));

        titleProvider.Received(1).SetTitle(
            "[[AppTitle_Progress_Installing(50)]] - EventLogExpert 23.1.1.1 - Application");
    }

    [Fact]
    public void Compose_WhenInstallingProgress_ShouldSetTitle()
    {
        var titleProvider = Compose(new AppTitleState(
            Progress: new AppTitleProgress.Installing(50),
            IsDevBuild: false, IsPrerelease: false, IsAdmin: false, Version, LogName: null));

        titleProvider.Received(1).SetTitle("[[AppTitle_Progress_Installing(50)]] - EventLogExpert 23.1.1.1");
    }

    [Fact]
    public void Compose_WhenNoProgress_ShouldSetTitleWithoutProgressPrefix()
    {
        var titleProvider = Compose(new AppTitleState(
            Progress: null, IsDevBuild: false, IsPrerelease: false, IsAdmin: false, Version, LogName: null));

        titleProvider.Received(1).SetTitle("EventLogExpert 23.1.1.1");
    }

    [Fact]
    public void Compose_WhenPrereleaseAdminAndLogName_ShouldSetTitle()
    {
        var titleProvider = Compose(new AppTitleState(
            Progress: null, IsDevBuild: false, IsPrerelease: true, IsAdmin: true, Version, LogName));

        titleProvider.Received(1).SetTitle(
            "EventLogExpert[[AppTitle_Qualifier_Preview]] 23.1.1.1[[AppTitle_Qualifier_Admin]] - Application");
    }

    [Fact]
    public void Compose_WhenPrereleaseAndLogName_ShouldSetTitle()
    {
        var titleProvider = Compose(new AppTitleState(
            Progress: null, IsDevBuild: false, IsPrerelease: true, IsAdmin: false, Version, LogName));

        titleProvider.Received(1).SetTitle(
            "EventLogExpert[[AppTitle_Qualifier_Preview]] 23.1.1.1 - Application");
    }

    [Fact]
    public void Compose_WhenRelaunchProgress_ShouldSetTitle()
    {
        var titleProvider = Compose(new AppTitleState(
            Progress: new AppTitleProgress.RelaunchToApply(),
            IsDevBuild: false, IsPrerelease: false, IsAdmin: false, Version, LogName: null));

        titleProvider.Received(1).SetTitle("[[AppTitle_Progress_Relaunch]] - EventLogExpert 23.1.1.1");
    }

    [Fact]
    public void Compose_WhenReleaseAdminAndLogName_ShouldSetTitle()
    {
        var titleProvider = Compose(new AppTitleState(
            Progress: null, IsDevBuild: false, IsPrerelease: false, IsAdmin: true, Version, LogName));

        titleProvider.Received(1).SetTitle(
            "EventLogExpert 23.1.1.1[[AppTitle_Qualifier_Admin]] - Application");
    }

    [Fact]
    public void Compose_WhenReleaseAndLogName_ShouldSetTitle()
    {
        var titleProvider = Compose(new AppTitleState(
            Progress: null, IsDevBuild: false, IsPrerelease: false, IsAdmin: false, Version, LogName));

        titleProvider.Received(1).SetTitle("EventLogExpert 23.1.1.1 - Application");
    }

    [Fact]
    public void Compose_WhenReleaseAndNoLogName_ShouldSetTitle()
    {
        var titleProvider = Compose(new AppTitleState(
            Progress: null, IsDevBuild: false, IsPrerelease: false, IsAdmin: false, Version, LogName: null));

        titleProvider.Received(1).SetTitle("EventLogExpert 23.1.1.1");
    }

    private static ITitleProvider Compose(AppTitleState state)
    {
        var titleProvider = Substitute.For<ITitleProvider>();
        var composer = new AppTitleTextComposer(new MarkerLocalizer(), titleProvider);

        composer.Compose(state);

        return titleProvider;
    }
}
