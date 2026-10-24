// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

namespace EventLogExpert.Runtime.FilterPane;

/// <summary>
///     Intersects the enabled pane date filter with the supplied bounds, so no hidden event reappears; a disabled or
///     missing filter is replaced by an enabled one holding only the supplied bounds.
/// </summary>
internal sealed record NarrowFilterDateRangeAction(DateTime? After, DateTime? Before);
