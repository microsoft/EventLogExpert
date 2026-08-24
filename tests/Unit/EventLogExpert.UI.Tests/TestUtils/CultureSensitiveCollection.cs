// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

namespace EventLogExpert.UI.Tests.TestUtils;

/// <summary>
///     Serializes culture-mutating test classes (<c>DisableParallelization</c>) so a pinned/forced culture cannot
///     leak into a culture-sensitive assertion on a pooled thread.
/// </summary>
[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class CultureSensitiveCollection
{
    public const string Name = "culture-sensitive";
}
