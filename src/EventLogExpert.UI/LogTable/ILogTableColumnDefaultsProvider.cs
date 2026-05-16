// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using System.Collections.Frozen;
using System.Collections.Immutable;

namespace EventLogExpert.UI.LogTable;

/// <summary>
///     Host-facing seam exposing the LogTable's factory default column visibility, ordering, and widths. Stateless
///     and effectively immutable; safe as a singleton.
/// </summary>
public interface ILogTableColumnDefaultsProvider
{
    /// <summary>Canonical column order before any user-driven reordering.</summary>
    ImmutableList<ColumnName> ColumnOrder { get; }

    /// <summary>Default pixel widths keyed by column.</summary>
    FrozenDictionary<ColumnName, int> ColumnWidths { get; }

    /// <summary>Columns that are visible by default on a fresh install.</summary>
    ImmutableList<ColumnName> EnabledColumns { get; }

    /// <summary>Returns the default pixel width for <paramref name="column" />, or 100 if unknown.</summary>
    int GetColumnWidth(ColumnName column);
}
