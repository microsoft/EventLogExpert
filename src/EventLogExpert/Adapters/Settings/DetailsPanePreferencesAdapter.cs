// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Runtime.DetailsPane;

namespace EventLogExpert.Adapters.Settings;

internal sealed class DetailsPanePreferencesAdapter : IDetailsPanePreferencesProvider
{
    private const string DetailsPaneHeight = "details-pane-height";
    private const string DisplaySelectionEnabled = "display-selection-enabled";

    public int DetailsPaneHeightPreference
    {
        get => Preferences.Default.Get(DetailsPaneHeight, 0);
        set => Preferences.Default.Set(DetailsPaneHeight, value);
    }

    public bool DisplayPaneSelectionPreference
    {
        get => Preferences.Default.Get(DisplaySelectionEnabled, false);
        set => Preferences.Default.Set(DisplaySelectionEnabled, value);
    }
}
