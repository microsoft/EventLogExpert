// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

namespace EventLogExpert.Runtime.Menu;

/// <summary>
///     Bounding rectangle returned by the <c>getMenuElementRect</c> JS interop helper. Used by menu trigger anchors
///     (top-level menu bar items, FilterPane chevron, and any future split-button or dropdown opener) to position the
///     popup at the bottom-left of the trigger element.
/// </summary>
/// <remarks>
///     Promoted from per-component duplicates so JS interop deserialization stays consistent across all named
///     consumers (≥2 callers per the shared-types policy).
/// </remarks>
public sealed record MenuAnchorRect(
    double Left,
    double Top,
    double Right,
    double Bottom,
    double Width,
    double Height);
