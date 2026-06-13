// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

namespace EventLogExpert.Runtime.LogTable;

/// <summary>Build-time / static-initialization flag gating the one-time RecordId column-index reset migration.</summary>
/// <remarks>
///     <para>
///         When <see cref="Enabled" /> is <see langword="true" /> (default), the real <see cref="ColumnResetMigrator" />
///         runs once at column-load time and clears the numeric-keyed <c>enabled-event-table-columns</c> and
///         <c>column-order</c> preferences so they fall back to defaults after <see cref="ColumnName.RecordId" /> was
///         inserted at enum value 0 (which renumbered every other column). When <see langword="false" />, the
///         <see cref="NoOpColumnResetMigrator" /> fills the slot and the reset never runs.
///     </para>
///     <para>
///         Staged-deletion contract: once enough releases have shipped that no install predates the RecordId column, this
///         migration can be removed. The mechanical removal procedure is:
///         <list type="number">
///             <item>Flip <see cref="Enabled" /> to <see langword="false" /> as a smoke test.</item>
///             <item>
///                 Delete the <c>if (ColumnResetMigrationFeature.IsEnabled)</c> branch in
///                 <see cref="LogTableServiceCollectionExtensions" /> (keep the <c>NoOp</c> registration only) and the
///                 <c>ColumnResetMigrationFeature.Enabled</c> condition added to the <c>ILegacyPreferences</c>
///                 registration in <c>MauiProgram</c>.
///             </item>
///             <item>
///                 Delete: <see cref="IColumnResetMigrator" />, <see cref="ColumnResetMigrator" />,
///                 <see cref="NoOpColumnResetMigrator" />, the <c>AddColumnResetMigration</c> DI extension + its call in
///                 <c>MauiProgram</c>, and the <c>columnResetMigrator</c> ctor parameter + <c>ShouldRunMigration</c>
///                 branch in <c>Effects.HandleLoadColumns</c>.
///             </item>
///             <item>Delete this file.</item>
///         </list>
///     </para>
/// </remarks>
public static class ColumnResetMigrationFeature
{
    public static readonly bool Enabled = true;

    private static readonly AsyncLocal<bool?> s_testOverride = new();

    internal static bool IsEnabled => s_testOverride.Value ?? Enabled;

    internal static IDisposable Override(bool value)
    {
        s_testOverride.Value = value;

        return new OverrideScope();
    }

    private sealed class OverrideScope : IDisposable
    {
        public void Dispose() => s_testOverride.Value = null;
    }
}
