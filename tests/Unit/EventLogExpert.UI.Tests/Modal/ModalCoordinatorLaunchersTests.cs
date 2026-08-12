// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Runtime.Update.ReleaseNotes;
using EventLogExpert.UI.Database;
using EventLogExpert.UI.DatabaseTools;
using EventLogExpert.UI.DebugLog;
using EventLogExpert.UI.FilterLibrary;
using EventLogExpert.UI.Modal;
using EventLogExpert.UI.Settings;
using EventLogExpert.UI.Update;
using NSubstitute;

namespace EventLogExpert.UI.Tests.Modal;

public sealed class ModalCoordinatorLaunchersTests
{
    [Fact]
    public async Task OpenDatabaseRecoveryAsync_DelegatesToPushAsync()
    {
        var coordinator = Substitute.For<IModalCoordinator>();
        coordinator.PushAsync<DatabaseRecoveryModal, bool>(Arg.Any<IDictionary<string, object?>?>())
            .Returns(new ModalOpenResult<bool>(false, WasOpened: true));

        await coordinator.OpenDatabaseRecoveryAsync();

        await coordinator.Received(1).PushAsync<DatabaseRecoveryModal, bool>(Arg.Any<IDictionary<string, object?>?>());
    }

    [Fact]
    public async Task OpenDatabaseToolsAsync_DelegatesToPushAsync()
    {
        var coordinator = Substitute.For<IModalCoordinator>();
        coordinator.PushAsync<DatabaseToolsModal, bool>(Arg.Any<IDictionary<string, object?>?>())
            .Returns(new ModalOpenResult<bool>(false, WasOpened: true));

        await coordinator.OpenDatabaseToolsAsync();

        await coordinator.Received(1).PushAsync<DatabaseToolsModal, bool>(Arg.Any<IDictionary<string, object?>?>());
    }

    [Fact]
    public async Task OpenDebugLogsAsync_DelegatesToPushAsync()
    {
        var coordinator = Substitute.For<IModalCoordinator>();
        coordinator.PushAsync<DebugLogModal, bool>(Arg.Any<IDictionary<string, object?>?>())
            .Returns(new ModalOpenResult<bool>(false, WasOpened: true));

        await coordinator.OpenDebugLogsAsync();

        await coordinator.Received(1).PushAsync<DebugLogModal, bool>(Arg.Any<IDictionary<string, object?>?>());
    }

    [Fact]
    public async Task OpenFilterLibraryAsync_DelegatesToPushAsync()
    {
        var coordinator = Substitute.For<IModalCoordinator>();
        coordinator.PushAsync<FilterLibraryModal, bool>(Arg.Any<IDictionary<string, object?>?>())
            .Returns(new ModalOpenResult<bool>(false, WasOpened: true));

        await coordinator.OpenFilterLibraryAsync();

        await coordinator.Received(1).PushAsync<FilterLibraryModal, bool>(Arg.Any<IDictionary<string, object?>?>());
    }

    [Fact]
    public async Task OpenFilterLibraryAsync_WithInitialTab_PassesTabViaParameters()
    {
        var coordinator = Substitute.For<IModalCoordinator>();
        coordinator.PushAsync<FilterLibraryModal, bool>(Arg.Any<IDictionary<string, object?>?>())
            .Returns(new ModalOpenResult<bool>(false, WasOpened: true));

        await coordinator.OpenFilterLibraryAsync(LibraryTab.Favorites);

        await coordinator.Received(1).PushAsync<FilterLibraryModal, bool>(
            Arg.Is<IDictionary<string, object?>?>(p =>
                p != null
                && p.ContainsKey(nameof(FilterLibraryModal.InitialTab))
                && (LibraryTab)p[nameof(FilterLibraryModal.InitialTab)]! == LibraryTab.Favorites));
    }

    [Fact]
    public async Task OpenReleaseNotesAsync_PassesContentParameter()
    {
        var coordinator = Substitute.For<IModalCoordinator>();
        coordinator.PushAsync<ReleaseNotesModal, bool>(Arg.Any<IDictionary<string, object?>?>())
            .Returns(new ModalOpenResult<bool>(false, WasOpened: true));
        var content = new ReleaseNotesContent("v1.0", "## Notes");

        await coordinator.OpenReleaseNotesAsync(content);

        await coordinator.Received(1).PushAsync<ReleaseNotesModal, bool>(
            Arg.Is<IDictionary<string, object?>?>(d => d != null && d.ContainsKey(nameof(ReleaseNotesModal.Content)) && content.Equals((ReleaseNotesContent)d[nameof(ReleaseNotesModal.Content)]!)));
    }

    [Fact]
    public async Task OpenSettingsAsync_DelegatesToPushAsync()
    {
        var coordinator = Substitute.For<IModalCoordinator>();
        coordinator.PushAsync<SettingsModal, bool>(Arg.Any<IDictionary<string, object?>?>())
            .Returns(new ModalOpenResult<bool>(false, WasOpened: true));

        await coordinator.OpenSettingsAsync();

        await coordinator.Received(1).PushAsync<SettingsModal, bool>(Arg.Any<IDictionary<string, object?>?>());
    }

    [Fact]
    public void OpenSettingsAsync_NullCoordinator_ThrowsArgumentNullException()
    {
        Assert.Throws<ArgumentNullException>(static () => { _ = ModalCoordinatorLaunchers.OpenSettingsAsync(coordinator: null!); });
    }

    [Fact]
    public async Task OpenSettingsAsync_WhenActiveModalVetoesPreemption_ReturnsNotOpened()
    {
        var coordinator = Substitute.For<IModalCoordinator>();
        coordinator.PushAsync<SettingsModal, bool>(Arg.Any<IDictionary<string, object?>?>())
            .Returns(new ModalOpenResult<bool>(false, WasOpened: false));

        ModalOpenResult<bool> result = await coordinator.OpenSettingsAsync();

        Assert.False(result.WasOpened);
    }
}
