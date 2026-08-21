// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

namespace EventLogExpert.Runtime.Memory;

/// <summary>
///     The advisory memory-usage band shown by the status-bar indicator. Purely presentational: it drives the chip
///     color and the screen-reader announcement, and never changes app behavior.
/// </summary>
public enum MemoryUsageLevel
{
    Normal,
    Elevated,
    High
}
