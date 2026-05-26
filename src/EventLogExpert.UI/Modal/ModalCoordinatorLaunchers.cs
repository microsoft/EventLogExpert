// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Runtime.Modal;
using EventLogExpert.Runtime.Update.ReleaseNotes;
using EventLogExpert.UI.DatabaseTools;
using EventLogExpert.UI.DebugLog;
using EventLogExpert.UI.FilterCache;
using EventLogExpert.UI.FilterGroup;
using EventLogExpert.UI.Settings;
using EventLogExpert.UI.Update;

namespace EventLogExpert.UI.Modal;

/// <summary>Typed launchers that route production modal opens through the coordinator's veto pipeline.</summary>
public static class ModalCoordinatorLaunchers
{
    extension(IModalCoordinator coordinator)
    {
        public Task<ModalOpenResult<bool>> OpenDatabaseToolsAsync()
        {
            ArgumentNullException.ThrowIfNull(coordinator);

            return coordinator.PushAsync<DatabaseToolsModal, bool>();
        }

        public Task<ModalOpenResult<bool>> OpenDebugLogsAsync()
        {
            ArgumentNullException.ThrowIfNull(coordinator);

            return coordinator.PushAsync<DebugLogModal, bool>();
        }

        public Task<ModalOpenResult<bool>> OpenFilterCacheAsync()
        {
            ArgumentNullException.ThrowIfNull(coordinator);

            return coordinator.PushAsync<FilterCacheModal, bool>();
        }

        public Task<ModalOpenResult<bool>> OpenFilterGroupAsync()
        {
            ArgumentNullException.ThrowIfNull(coordinator);

            return coordinator.PushAsync<FilterGroupModal, bool>();
        }

        public Task<ModalOpenResult<bool>> OpenReleaseNotesAsync(ReleaseNotesContent content)
        {
            ArgumentNullException.ThrowIfNull(coordinator);

            return coordinator.PushAsync<ReleaseNotesModal, bool>(new Dictionary<string, object?> { [nameof(ReleaseNotesModal.Content)] = content });
        }

        public Task<ModalOpenResult<bool>> OpenSettingsAsync()
        {
            ArgumentNullException.ThrowIfNull(coordinator);

            return coordinator.PushAsync<SettingsModal, bool>();
        }
    }
}
