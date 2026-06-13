// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

namespace EventLogExpert.Runtime.LogTable;

internal sealed class NoOpColumnResetMigrator : IColumnResetMigrator
{
    public void RunMigration() { }

    public bool ShouldRunMigration() => false;
}
