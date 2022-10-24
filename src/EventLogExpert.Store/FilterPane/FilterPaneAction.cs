// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

namespace EventLogExpert.Store.FilterPane;

public record FilterPaneAction
{
    public record AddRecentFilter(string FilterText);
}
