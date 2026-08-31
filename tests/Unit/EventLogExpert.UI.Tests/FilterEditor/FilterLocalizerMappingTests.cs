// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Filtering.Common.Filtering;
using EventLogExpert.UI.Common;
using EventLogExpert.UI.FilterEditor.Comparison;
using EventLogExpert.UI.Tests.TestUtils;

namespace EventLogExpert.UI.Tests.FilterEditor;

/// <summary>
///     Pins every filter localizer switch arm to its own resource key. <see cref="MarkerLocalizer" /> echoes each key
///     as <c>[[key]]</c>, and both families key on <c>{stem}{member}</c>, so asserting the arm returns
///     <c>[[{stem}{member}]]</c> proves the mapping is not just present but correct. The enum-mapped guard (key set) and
///     the neutral-drift guard (key values) both pass when two valid arms are transposed; these do not.
/// </summary>
public sealed class FilterLocalizerMappingTests
{
    private readonly MarkerLocalizer _localizer = new();

    [Fact]
    public void ComparisonOperatorSelect_RoutesEachKindToItsOwnKey()
    {
        foreach (ComparisonOperatorSelect.ComparisonKind kind in Enum.GetValues<ComparisonOperatorSelect.ComparisonKind>())
        {
            Assert.Equal($"[[FilterEditor_Comparison_{kind}]]", ComparisonOperatorSelect.KindLabel(_localizer, kind));
        }
    }

    [Fact]
    public void FilterLensLabelFormatter_RoutesEachPropertyToItsOwnKey()
    {
        foreach (EventProperty property in Enum.GetValues<EventProperty>())
        {
            Assert.Equal($"[[FilterLens_Property_{property}]]", FilterLensLabelFormatter.PropertyName(_localizer, property));
        }
    }
}
