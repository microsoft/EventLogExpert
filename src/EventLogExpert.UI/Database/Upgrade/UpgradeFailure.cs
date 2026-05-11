// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

namespace EventLogExpert.UI.Database.Upgrade;

public sealed record UpgradeFailure(string FileName, string Message);
