// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

namespace EventLogExpert.UI.FilterPane;

internal enum DateFilterBoundOrigin
{
    /// <summary>Copied from the applied range; follows the applied range until the user changes it.</summary>
    Applied,

    /// <summary>Supplied as an exact instant by a quick range the user picked.</summary>
    Preset,

    /// <summary>Suggested by Add Date Filter and replaced by any applied range that appears before the user edits it.</summary>
    Suggested,

    /// <summary>Typed or cleared by the user; converted from its wall-clock reading on apply.</summary>
    Edited
}
