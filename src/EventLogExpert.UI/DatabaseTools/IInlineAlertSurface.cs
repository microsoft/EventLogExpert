// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Runtime.Alerts;

namespace EventLogExpert.UI.DatabaseTools;

/// <summary>
///     Cascading-value surface that lets a child component (e.g. ManageDatabasesTab) raise an inline alert through
///     its hosting modal. Implemented by <see cref="DatabaseToolsModal" /> via implicit interface satisfaction against the
///     inherited public <c>ShowInlineAlertAsync</c> from <c>ModalBase</c>.
/// </summary>
internal interface IInlineAlertSurface
{
    Task<InlineAlertResult> ShowInlineAlertAsync(
        InlineAlertRequest request,
        CancellationToken cancellationToken);
}
