// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Eventing.Structured;

namespace EventLogExpert.Eventing.Common.Events;

/// <summary>
///     One enumerated nested-UserData field: its storage-key <paramref name="Path" /> and the
///     <see cref="StructuredFieldResult" /> a filter evaluates, with the event-level "extraction was capped" flag already
///     folded into the result's truncation (mirroring <see cref="UserDataField.ToFieldResult" />). The element type of
///     <see cref="UserDataFieldEnumerator" />.
/// </summary>
public readonly record struct UserDataFieldEntry(string Path, StructuredFieldResult Result);
