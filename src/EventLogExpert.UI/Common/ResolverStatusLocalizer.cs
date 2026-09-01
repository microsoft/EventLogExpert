// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Localization;
using EventLogExpert.Runtime.StatusBar;
using Microsoft.Extensions.Localization;

namespace EventLogExpert.UI.Common;

internal static class ResolverStatusLocalizer
{
    internal static string Describe(IStringLocalizer<SharedResource> localizer, ResolverStatus status) =>
        DescribeCore(localizer, status.Reason, status.LogDescription);

    internal static string DescribeCore(
        IStringLocalizer<SharedResource> localizer,
        ResolverStatusReason reason,
        string? logDescription) =>
        (reason, logDescription) switch
        {
            (ResolverStatusReason.FailedToOpen, { } description) => localizer["StatusBar_Resolver_FailedToOpen", description],
            (ResolverStatusReason.FailedToLoad, { } description) => localizer["StatusBar_Resolver_FailedToLoad", description],
            (ResolverStatusReason.NoResolver, _) => localizer["StatusBar_Resolver_NoResolver"],
            (ResolverStatusReason.None, _) => string.Empty,
            _ => throw new ArgumentOutOfRangeException(nameof(reason), reason, null)
        };
}
