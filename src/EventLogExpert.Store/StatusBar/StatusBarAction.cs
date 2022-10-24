// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

namespace EventLogExpert.Store.StatusBar;

public record StatusBarAction
{
    public record SetEventsLoaded(int EventCount);
}
