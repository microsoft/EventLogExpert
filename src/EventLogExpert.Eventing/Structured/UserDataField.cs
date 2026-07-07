// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Eventing.Common.Events;
using EventLogExpert.Eventing.Readers;
using System.Collections.Immutable;
using System.Runtime.InteropServices;

namespace EventLogExpert.Eventing.Structured;

/// <summary>
///     One deduped nested-UserData field on a resolved event: the storage-key path (local-name chain under
///     <c>UserData</c>, <c>/@attr</c> for an attribute) and its values. <see cref="IsTruncated" /> flags a field whose
///     values exceeded the per-field cap, keeping the retained values visible to the keep-visible filter fail-safe.
/// </summary>
public readonly record struct UserDataField(string Path, ImmutableArray<string> Values, bool IsTruncated)
{
    /// <summary>
    ///     Projects this stored field into a <see cref="StructuredFieldResult" /> so a filter can evaluate it the same
    ///     way as a point-looked-up field. Kept in the Eventing assembly because it builds an <see cref="EventFieldValue" />
    ///     from the internal property primitives, which the filtering layer cannot construct across the assembly boundary.
    ///     <paramref name="forceTruncated" /> lets a caller fold an event-level "extraction was capped" signal into this
    ///     field's truncation, mirroring the point-lookup fold in <c>ResolvedEvent.TryGetUserDataValues</c>.
    /// </summary>
    public StructuredFieldResult ToFieldResult(bool forceTruncated = false) =>
        new(
            EventFieldValue.FromProperty(
                EventProperty.FromReference(ImmutableCollectionsMarshal.AsArray(Values) ?? [])),
            IsTruncated || forceTruncated);
}
