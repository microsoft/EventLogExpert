// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

namespace EventLogExpert.Store.Actions;

public record FilterPaneAction
{
    public record AddRecentFilter(string FilterText);
}
