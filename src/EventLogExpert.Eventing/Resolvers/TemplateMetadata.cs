// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using System.Collections.Immutable;

namespace EventLogExpert.Eventing.Resolvers;

/// <summary>
///     Immutable metadata for a Windows event manifest template, derived by <see cref="TemplateAnalyzer.Analyze" />
///     in a single parse pass and cached per template string.
/// </summary>
/// <param name="VisiblePropertyCount">
///     Count of template &lt;data&gt; nodes Windows surfaces as separate user properties
///     through EvtRender. Excludes length-provider nodes that Windows consumes internally to size a sibling binary-data
///     node.
/// </param>
/// <param name="AllOutTypes">
///     The outType attribute string for every &lt;data&gt; node in the template, in document order.
///     Empty string when a node has no outType attribute.
/// </param>
/// <param name="VisibleOutTypes">
///     The outType attribute strings restricted to the visible nodes (length-provider nodes
///     filtered out), in document order. Equals <paramref name="AllOutTypes" /> when no length-provider nodes are present.
/// </param>
/// <param name="AllMaps">
///     The map attribute string (manifest valueMap / bitMap symbolic name) for every &lt;data&gt; node
///     in the template, in document order. Empty string when a node has no map attribute.
/// </param>
/// <param name="VisibleMaps">
///     The map attribute strings restricted to the visible nodes, in document order. Equals
///     <paramref name="AllMaps" /> when no length-provider nodes are present.
/// </param>
internal readonly record struct TemplateMetadata(
    int VisiblePropertyCount,
    ImmutableArray<string> AllOutTypes,
    ImmutableArray<string> VisibleOutTypes,
    ImmutableArray<string> AllMaps,
    ImmutableArray<string> VisibleMaps);
