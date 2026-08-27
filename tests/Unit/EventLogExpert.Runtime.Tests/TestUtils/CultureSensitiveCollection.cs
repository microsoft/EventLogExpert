// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

namespace EventLogExpert.Runtime.Tests.TestUtils;

/// <summary>
///     Serializes culture-mutating test classes (<c>DisableParallelization</c>) so a forced <c>CurrentCulture</c>
///     cannot leak into a culture-sensitive assertion on a pooled thread. Runtime.Tests runs its collections in parallel
///     by default, so the localization culture guards must opt into this collection. Mirrors the equivalent in
///     <c>EventLogExpert.UI.Tests</c>; a <c>[CollectionDefinition]</c> only applies within its own assembly.
/// </summary>
[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class CultureSensitiveCollection
{
    public const string Name = "culture-sensitive";
}
