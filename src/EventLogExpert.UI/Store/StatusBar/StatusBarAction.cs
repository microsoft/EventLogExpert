// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

namespace EventLogExpert.UI.Store.StatusBar;

public sealed record StatusBarAction
{
    public sealed record SetResolverStatus(string ResolverStatus);
}
