// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

namespace EventLogExpert.Filtering.Tests.EventData;

/// <summary>
///     Serializes the test classes that share <c>EventPropertyValuesCache</c>'s process-wide static caches (each
///     calls <c>Clear()</c> in setup/teardown). Without a shared collection, xUnit runs these classes in parallel and one
///     class's <c>Clear()</c> can wipe another class's cache entries mid-test, causing intermittent failures.
/// </summary>
[CollectionDefinition("EventPropertyValuesCache")]
public sealed class EventPropertyValuesCacheCollection;
