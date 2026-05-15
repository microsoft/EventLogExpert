// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

namespace EventLogExpert.UI.Database;

public interface IDatabasePreferencesProvider
{
    IEnumerable<string> DisabledDatabasesPreference { get; set; }
}
