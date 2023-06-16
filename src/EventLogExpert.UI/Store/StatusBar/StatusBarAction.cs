// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

namespace EventLogExpert.UI.Store.StatusBar;

public record StatusBarAction
{
    public record SetResolverStatus(string ResolverStatus);
}
