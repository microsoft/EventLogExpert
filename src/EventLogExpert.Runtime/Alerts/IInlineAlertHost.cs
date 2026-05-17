// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

namespace EventLogExpert.Runtime.Alerts;

/// <summary>
///     Implemented by an active modal so alerts can be routed as inline banners instead of opening a separate alert
///     modal (which would cancel the active one).
/// </summary>
public interface IInlineAlertHost
{
    /// <summary>Show <paramref name="request" /> inline. Replaces any prior pending inline alert (its task is canceled).</summary>
    Task<InlineAlertResult> ShowInlineAlertAsync(InlineAlertRequest request, CancellationToken cancellationToken);
}
