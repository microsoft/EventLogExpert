// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Eventing.Common.Events;

namespace EventLogExpert.Runtime.LogTable;

/// <summary>
///     One viewport row: its stable <see cref="EventLocator" /> paired with the lean-rehydrated event. The locator is
///     the selection / highlight / rank identity the no-index <c>Virtualize</c> template renders against, because a lean
///     <see cref="ResolvedEvent" /> carries no reference identity or embedded row index. The lean event fills the grid
///     scalars plus <see cref="ResolvedEvent.Description" />, leaving the detail-only payloads (UserData, XML, EventData)
///     empty until a full <c>GetDetail</c> materializes them.
/// </summary>
/// <param name="Loc">The stable locator addressing this row's physical position in the backing store.</param>
/// <param name="Lean">The lean event: grid scalars plus Description, with the detail-only payloads left empty.</param>
internal readonly record struct DisplayRow(EventLocator Loc, ResolvedEvent Lean);
