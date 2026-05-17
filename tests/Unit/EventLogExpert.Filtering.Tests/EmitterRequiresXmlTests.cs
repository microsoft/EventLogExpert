// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Filtering.Parsing;
using EventLogExpert.Filtering.Tests.TestUtils;

namespace EventLogExpert.Filtering.Tests;

/// <summary>
///     The emitter's <c>RequiresXml</c> flag drives whether the eventing layer pre-renders <c>ResolvedEvent.Xml</c>
///     for the entire stream — a non-trivial extra cost we want to avoid when no filter touches XML. These tests pin the
///     flag's contract independently of the parity suite (which would catch a mismatch between Dynamic.Core and the
///     emitter, but not a regression where both started returning the wrong value).
/// </summary>
public sealed class EmitterRequiresXmlTests
{
    [Theory]
    [InlineData("Id == 100")]
    [InlineData("Source == \"TestSource\"")]
    [InlineData("Source.Contains(\"Test\", StringComparison.OrdinalIgnoreCase)")]
    [InlineData("Id == 100 && Source == \"TestSource\"")]
    [InlineData("(new[] {\"100\", \"200\"}).Contains(Id.ToString())")]
    [InlineData("Keywords.Any(e => string.Equals(e, \"Audit\", StringComparison.OrdinalIgnoreCase))")]
    [InlineData("UserId != null && UserId.Value == \"S-1-5-18\"")]
    public void TryCompile_WhenFilterDoesNotReferenceXml_SetsRequiresXmlFalse(string filter)
    {
        Assert.True(FilterParser.TryCompile(filter, out var compiled, out var error), error);

        Assert.False(compiled.RequiresXml);
    }

    [Fact]
    public void TryCompile_WhenFilterIsXmlOnly_PredicateActuallyEvaluatesXmlContent()
    {
        Assert.True(
            FilterParser.TryCompile(
                "Xml.Contains(\"data inside\", StringComparison.OrdinalIgnoreCase)",
                out var compiled,
                out var error),
            error);

        Assert.True(compiled.RequiresXml);
        Assert.True(compiled.Predicate(EventUtils.FullyPopulated));
        Assert.False(compiled.Predicate(EventUtils.NoNullables));
    }

    [Theory]
    [InlineData("Xml.Contains(\"data\", StringComparison.OrdinalIgnoreCase)")]
    [InlineData("Xml == \"<x/>\"")]
    [InlineData("Id == 100 && Xml.Contains(\"data\", StringComparison.OrdinalIgnoreCase)")]
    [InlineData("Source == \"X\" || Xml.Contains(\"y\", StringComparison.OrdinalIgnoreCase)")]
    [InlineData("!(Xml == \"<x/>\")")]
    [InlineData("(new[] {\"a\", \"b\"}).Contains(Xml)")]
    public void TryCompile_WhenFilterReferencesXml_SetsRequiresXmlTrue(string filter)
    {
        Assert.True(FilterParser.TryCompile(filter, out var compiled, out var error), error);

        Assert.True(compiled.RequiresXml);
    }
}
