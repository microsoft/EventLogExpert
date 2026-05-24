// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

namespace EventLogExpert.UI.DatabaseTools;

/// <summary>
///     Identifies the five DatabaseTools tabs in <see cref="DatabaseToolsModal" />. Used as both the tab order and
///     the discriminator the modal switches on when rendering the active panel.
/// </summary>
internal enum DatabaseToolsTab
{
    Show,
    Create,
    Merge,
    Diff,
    Upgrade
}
