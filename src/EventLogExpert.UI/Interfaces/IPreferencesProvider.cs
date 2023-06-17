// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

namespace EventLogExpert.UI.Interfaces;

public interface IPreferencesProvider
{
    IList<string> DisabledDatabasesPreference { get; set; }

    bool DisplayPaneSelectionPreference { get; set; }

    bool PrereleasePreference { get; set; }

    string TimeZonePreference { get; set; }
}
