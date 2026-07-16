// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

namespace EventLogExpert.Runtime.Histogram;

public interface IHistogramPreferencesProvider
{
    bool HistogramVisiblePreference { get; set; }
}
