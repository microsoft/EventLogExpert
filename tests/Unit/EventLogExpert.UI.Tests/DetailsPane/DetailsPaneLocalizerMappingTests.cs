// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Eventing.Common.Events;
using EventLogExpert.Runtime.DetailsPane;
using EventLogExpert.UI.Common;
using EventLogExpert.UI.Tests.TestUtils;

namespace EventLogExpert.UI.Tests.DetailsPane;

/// <summary>
///     Pins every localizer switch arm to its own resource key. <see cref="MarkerLocalizer" /> echoes each key as
///     <c>[[key]]</c>, and each of these families keys on <c>{stem}{member}</c>, so asserting the arm returns
///     <c>[[{stem}{member}]]</c> proves the mapping is not just present but correct. The enum-mapped guard (key set) and
///     the neutral-drift guard (key values) both pass when two valid arms are transposed; these do not.
/// </summary>
public sealed class DetailsPaneLocalizerMappingTests
{
    private readonly MarkerLocalizer _localizer = new();

    [Fact]
    public void DetailsPropertyLocalizer_RoutesEachLabelToItsOwnKey()
    {
        foreach (DetailsPropertyLabel label in Enum.GetValues<DetailsPropertyLabel>())
        {
            Assert.Equal($"[[Details_Property_{label}]]", DetailsPropertyLocalizer.Label(_localizer, label));
        }
    }

    [Fact]
    public void GlossaryLocalizer_RoutesEachTermToItsOwnKey()
    {
        foreach (GlossaryTerm term in Enum.GetValues<GlossaryTerm>())
        {
            Assert.Equal($"[[Explain_{term}]]", GlossaryLocalizer.Description(_localizer, term));
        }
    }

    [Fact]
    public void ResolutionStatusLocalizer_RoutesEachStatusToItsOwnKey()
    {
        foreach (EventResolutionStatus status in Enum.GetValues<EventResolutionStatus>())
        {
            Assert.Equal($"[[ResolutionStatus_{status}]]", ResolutionStatusLocalizer.Display(_localizer, status));
        }
    }
}
