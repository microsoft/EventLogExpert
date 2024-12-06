// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

namespace EventLogExpert.UI.Interfaces;

public interface IDatabaseService
{
    IEnumerable<string> DisabledDatabases { get; }

    IEnumerable<string> LoadedDatabases { get; }

    EventHandler<IEnumerable<string>>? LoadedDatabasesChanged { get; set; }

    void LoadDatabases();

    void UpdateDisabledDatabases(IEnumerable<string> databases);
}
