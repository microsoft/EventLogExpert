// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

namespace EventLogExpert.Provider.Schema;

/// <summary>
///     A distinct source-OS stamp read from a provider database's <c>ProviderDetails</c> rows: the OS edition,
///     display version, and build/revision of the machine or image the providers were captured from. A neutral
///     provider-layer DTO (the richer <c>SourceOsProvenance</c> lives in the higher Eventing assembly, which this layer
///     must not depend on). Any field can be <see langword="null" /> for a legacy row that predates OS stamping or where
///     the value was unavailable.
/// </summary>
public sealed record ProviderDatabaseOsStamp(int? Build, int? Revision, string? Edition, string? DisplayVersion);
