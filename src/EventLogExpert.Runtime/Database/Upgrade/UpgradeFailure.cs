// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

namespace EventLogExpert.Runtime.Database.Upgrade;

public sealed record UpgradeFailure(string FileName, DatabaseFailureReason Reason);
