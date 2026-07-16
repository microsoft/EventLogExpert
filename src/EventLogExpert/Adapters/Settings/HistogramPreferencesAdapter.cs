// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Runtime.Histogram;

namespace EventLogExpert.Adapters.Settings;

internal sealed class HistogramPreferencesAdapter : IHistogramPreferencesProvider
{
    private const string HistogramVisible = "histogram-visible";

    public bool HistogramVisiblePreference
    {
        get => Preferences.Default.Get(HistogramVisible, true);
        set => Preferences.Default.Set(HistogramVisible, value);
    }
}
