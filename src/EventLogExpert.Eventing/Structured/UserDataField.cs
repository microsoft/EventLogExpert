// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using System.Collections.Immutable;

namespace EventLogExpert.Eventing.Structured;

/// <summary>
///     One deduped nested-UserData field on a resolved event: the storage-key path (local-name chain under
///     <c>UserData</c>, <c>/@attr</c> for an attribute) and its values. <see cref="IsTruncated" /> flags a field whose
///     values exceeded the per-field cap, keeping the retained values visible to the keep-visible filter fail-safe.
/// </summary>
public readonly record struct UserDataField(string Path, ImmutableArray<string> Values, bool IsTruncated);
