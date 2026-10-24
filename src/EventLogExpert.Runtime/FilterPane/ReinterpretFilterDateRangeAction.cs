// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

namespace EventLogExpert.Runtime.FilterPane;

/// <summary>
///     Re-anchors the pane date filter after a display time zone change so its wall-clock readings stay the same and
///     are interpreted in <paramref name="NewZone" />.
/// </summary>
internal sealed record ReinterpretFilterDateRangeAction(TimeZoneInfo PreviousZone, TimeZoneInfo NewZone);
