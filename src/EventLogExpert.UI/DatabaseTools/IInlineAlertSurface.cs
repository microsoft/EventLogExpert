// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.UI.Alerts;

namespace EventLogExpert.UI.DatabaseTools;

internal interface IInlineAlertSurface
{
    Task<InlineAlertResult> ShowInlineAlertAsync(
        InlineAlertRequest request,
        CancellationToken cancellationToken);
}
