// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

namespace EventLogExpert.UI.DetailsPane;

public interface IDetailsPanePreferencesProvider
{
    int DetailsPaneHeightPreference { get; set; }

    bool DisplayPaneSelectionPreference { get; set; }
}
