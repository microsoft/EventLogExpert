// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

namespace EventLogExpert.Runtime.Database;

public sealed record ImportFailure(string FileName, DatabaseFailureReason Reason);
