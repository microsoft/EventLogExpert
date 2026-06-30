// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

namespace EventLogExpert.Provider.Schema;

public sealed record ProviderDatabaseOsStamp(int? Build, int? Revision, string? Edition, string? DisplayVersion);
