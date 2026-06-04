// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

namespace EventLogExpert.DatabaseTools.UpgradeDatabase;

/// <summary>Upgrades the schema of an existing provider database to the current version.</summary>
/// <param name="DatabasePath">Path to the .db file to upgrade. Must exist.</param>
public sealed record UpgradeDatabaseRequest(string DatabasePath);
