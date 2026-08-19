// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Runtime.Stats;

namespace EventLogExpert.Adapters.Settings;

internal sealed class StatsDrawerPreferencesAdapter : IStatsDrawerPreferencesProvider
{
    private const string StatsDrawerHeight = "stats-drawer-height";

    public int StatsDrawerHeightPreference
    {
        get => Preferences.Default.Get(StatsDrawerHeight, 0);
        set => Preferences.Default.Set(StatsDrawerHeight, value);
    }
}
