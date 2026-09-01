// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

namespace EventLogExpert.Runtime.StatusBar;

/// <summary>
///     A typed resolver-status value for the status bar: a <see cref="ResolverStatusReason" /> plus the verbatim log
///     description the UI formats into the localized message. Runtime holds no display English. The factories are the only
///     construction path, so a templated reason always carries its description; <c>default</c> is <c>None</c>.
/// </summary>
public readonly record struct ResolverStatus
{
    private ResolverStatus(ResolverStatusReason reason, string? logDescription)
    {
        Reason = reason;
        LogDescription = logDescription;
    }

    public static ResolverStatus None => default;

    public static ResolverStatus NoResolver { get; } = new(ResolverStatusReason.NoResolver, logDescription: null);

    public ResolverStatusReason Reason { get; }

    public string? LogDescription { get; }

    public static ResolverStatus FailedToOpen(string logDescription)
    {
        ArgumentNullException.ThrowIfNull(logDescription);

        return new ResolverStatus(ResolverStatusReason.FailedToOpen, logDescription);
    }

    public static ResolverStatus FailedToLoad(string logDescription)
    {
        ArgumentNullException.ThrowIfNull(logDescription);

        return new ResolverStatus(ResolverStatusReason.FailedToLoad, logDescription);
    }
}
