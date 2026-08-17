// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Runtime.LogTable;

namespace EventLogExpert.Runtime.Tests.LogTable;

public sealed class ColumnDefaultsTests
{
    private static readonly ColumnDefaults s_defaults = new();

    [Fact]
    public void ColumnNameOrdinals_StayStable_SoPersistedPreferencesKeepMapping()
    {
        // Preferences serialize ColumnName by ordinal (LogTablePreferencesAdapter's default literal is [1,2,6,7,8]);
        // appending Opcode must leave the existing members' ordinals untouched.
        Assert.Equal(1, (int)ColumnName.Level);
        Assert.Equal(2, (int)ColumnName.DateAndTime);
        Assert.Equal(6, (int)ColumnName.Source);
        Assert.Equal(7, (int)ColumnName.EventId);
        Assert.Equal(8, (int)ColumnName.TaskCategory);
        Assert.Equal(13, (int)ColumnName.Opcode);
    }

    [Fact]
    public void ColumnOrder_PlacesOpcodeAfterTaskCategory()
    {
        Assert.Contains(ColumnName.Opcode, s_defaults.ColumnOrder);
        Assert.Equal(
            s_defaults.ColumnOrder.IndexOf(ColumnName.TaskCategory) + 1,
            s_defaults.ColumnOrder.IndexOf(ColumnName.Opcode));
    }

    [Fact]
    public void ColumnWidths_IncludeOpcode() =>
        Assert.True(s_defaults.ColumnWidths.ContainsKey(ColumnName.Opcode));

    [Fact]
    public void EnabledColumns_ExcludesOpcode_SoTheColumnIsOptIn() =>
        Assert.DoesNotContain(ColumnName.Opcode, s_defaults.EnabledColumns);
}
