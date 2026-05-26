// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Runtime.Modal;
using EventLogExpert.Runtime.Update.ReleaseNotes;
using EventLogExpert.UI.Database;
using EventLogExpert.UI.DatabaseTools;
using EventLogExpert.UI.DebugLog;
using EventLogExpert.UI.FilterCache;
using EventLogExpert.UI.FilterGroup;
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
        // Arrange
        var coordinator = Substitute.For<IModalCoordinator>();
        coordinator.PushAsync<DatabaseRecoveryDialog, bool>(Arg.Any<IDictionary<string, object?>?>())
            .Returns(new ModalOpenResult<bool>(false, WasOpened: true));

        // Act
        await coordinator.OpenDatabaseRecoveryAsync();

        // Assert
        await coordinator.Received(1).PushAsync<DatabaseRecoveryDialog, bool>(Arg.Any<IDictionary<string, object?>?>());
    }

    [Fact]
    public async Task OpenDatabaseToolsAsync_DelegatesToPushAsync()
    {
        // Arrange
        var coordinator = Substitute.For<IModalCoordinator>();
        coordinator.PushAsync<DatabaseToolsModal, bool>(Arg.Any<IDictionary<string, object?>?>())
            .Returns(new ModalOpenResult<bool>(false, WasOpened: true));

        // Act
        await coordinator.OpenDatabaseToolsAsync();

        // Assert
        await coordinator.Received(1).PushAsync<DatabaseToolsModal, bool>(Arg.Any<IDictionary<string, object?>?>());
    }

    [Fact]
    public async Task OpenDebugLogsAsync_DelegatesToPushAsync()
    {
        // Arrange
        var coordinator = Substitute.For<IModalCoordinator>();
        coordinator.PushAsync<DebugLogModal, bool>(Arg.Any<IDictionary<string, object?>?>())
            .Returns(new ModalOpenResult<bool>(false, WasOpened: true));

        // Act
        await coordinator.OpenDebugLogsAsync();

        // Assert
        await coordinator.Received(1).PushAsync<DebugLogModal, bool>(Arg.Any<IDictionary<string, object?>?>());
    }

    [Fact]
    public async Task OpenFilterCacheAsync_DelegatesToPushAsync()
    {
        // Arrange
        var coordinator = Substitute.For<IModalCoordinator>();
        coordinator.PushAsync<FilterCacheModal, bool>(Arg.Any<IDictionary<string, object?>?>())
            .Returns(new ModalOpenResult<bool>(false, WasOpened: true));

        // Act
        await coordinator.OpenFilterCacheAsync();

        // Assert
        await coordinator.Received(1).PushAsync<FilterCacheModal, bool>(Arg.Any<IDictionary<string, object?>?>());
    }

    [Fact]
    public async Task OpenFilterGroupAsync_DelegatesToPushAsync()
    {
        // Arrange
        var coordinator = Substitute.For<IModalCoordinator>();
        coordinator.PushAsync<FilterGroupModal, bool>(Arg.Any<IDictionary<string, object?>?>())
            .Returns(new ModalOpenResult<bool>(false, WasOpened: true));

        // Act
        await coordinator.OpenFilterGroupAsync();

        // Assert
        await coordinator.Received(1).PushAsync<FilterGroupModal, bool>(Arg.Any<IDictionary<string, object?>?>());
    }

    [Fact]
    public async Task OpenReleaseNotesAsync_PassesContentParameter()
    {
        // Arrange
        var coordinator = Substitute.For<IModalCoordinator>();
        coordinator.PushAsync<ReleaseNotesModal, bool>(Arg.Any<IDictionary<string, object?>?>())
            .Returns(new ModalOpenResult<bool>(false, WasOpened: true));
        var content = new ReleaseNotesContent("v1.0", "## Notes");

        // Act
        await coordinator.OpenReleaseNotesAsync(content);

        // Assert
        await coordinator.Received(1).PushAsync<ReleaseNotesModal, bool>(
            Arg.Is<IDictionary<string, object?>?>(d => d != null && d.ContainsKey(nameof(ReleaseNotesModal.Content)) && content.Equals((ReleaseNotesContent)d[nameof(ReleaseNotesModal.Content)]!)));
    }

    [Fact]
    public async Task OpenSettingsAsync_DelegatesToPushAsync()
    {
        // Arrange
        var coordinator = Substitute.For<IModalCoordinator>();
        coordinator.PushAsync<SettingsModal, bool>(Arg.Any<IDictionary<string, object?>?>())
            .Returns(new ModalOpenResult<bool>(false, WasOpened: true));

        // Act
        await coordinator.OpenSettingsAsync();

        // Assert
        await coordinator.Received(1).PushAsync<SettingsModal, bool>(Arg.Any<IDictionary<string, object?>?>());
    }

    [Fact]
    public void OpenSettingsAsync_NullCoordinator_ThrowsArgumentNullException()
    {
        // Arrange + Act + Assert — discard the Task to avoid xUnit2014; the throw happens synchronously in the guard.
        Assert.Throws<ArgumentNullException>(static () => { _ = ModalCoordinatorLaunchers.OpenSettingsAsync(coordinator: null!); });
    }

    [Fact]
    public async Task OpenSettingsAsync_WhenActiveModalVetoesPreemption_ReturnsNotOpened()
    {
        // Arrange — simulates PR 4's veto-preempt path: PushAsync returns WasOpened=false when the existing modal
        // vetoes via OnRequestCloseAsync (e.g., SettingsModal IsCloseBlocked, DatabaseToolsModal AnyTabIsRunning).
        var coordinator = Substitute.For<IModalCoordinator>();
        coordinator.PushAsync<SettingsModal, bool>(Arg.Any<IDictionary<string, object?>?>())
            .Returns(new ModalOpenResult<bool>(false, WasOpened: false));

        // Act
        ModalOpenResult<bool> result = await coordinator.OpenSettingsAsync();

        // Assert
        Assert.False(result.WasOpened);
    }
}
