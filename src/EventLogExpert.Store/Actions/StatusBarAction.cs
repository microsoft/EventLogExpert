// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

namespace EventLogExpert.Store.Actions;

public record StatusBarAction
{
    public record SetEventsLoaded(int EventCount);
}
