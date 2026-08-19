// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Runtime.LogTable;
using EventLogExpert.Runtime.Stats;
using EventLogExpert.Runtime.Update.ReleaseNotes;
using EventLogExpert.UI.Database;
using EventLogExpert.UI.DatabaseTools;
using EventLogExpert.UI.DebugLog;
using EventLogExpert.UI.FilterLibrary;
using EventLogExpert.UI.LogTable.Stats;
using EventLogExpert.UI.Settings;
using EventLogExpert.UI.Update;

namespace EventLogExpert.UI.Modal;

public static class ModalCoordinatorLaunchers
{
    extension(IModalCoordinator coordinator)
    {
        public Task<ModalOpenResult<bool>> OpenDatabaseRecoveryAsync()
        {
            ArgumentNullException.ThrowIfNull(coordinator);

            return coordinator.PushAsync<DatabaseRecoveryModal, bool>();
        }

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

        public Task<ModalOpenResult<bool>> OpenFilterLibraryAsync(LibraryTab? initialTab = null)
        {
            ArgumentNullException.ThrowIfNull(coordinator);

            if (initialTab is { } tab)
            {
                return coordinator.PushAsync<FilterLibraryModal, bool>(
                    new Dictionary<string, object?> { [nameof(FilterLibraryModal.InitialTab)] = tab });
            }

            return coordinator.PushAsync<FilterLibraryModal, bool>();
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

        public Task<ModalOpenResult<bool>> OpenStatsDetailAsync(StatsDimension dimension, IEventColumnView view, string? originLog)
        {
            ArgumentNullException.ThrowIfNull(coordinator);
            ArgumentNullException.ThrowIfNull(view);

            return coordinator.PushAsync<StatsDetailModal, bool>(new Dictionary<string, object?>
            {
                [nameof(StatsDetailModal.Dimension)] = dimension,
                [nameof(StatsDetailModal.View)] = view,
                [nameof(StatsDetailModal.OriginLog)] = originLog
            });
        }
    }
}
