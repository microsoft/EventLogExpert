// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Eventing.Common.Events;
using EventLogExpert.Runtime.ResolutionCoverage;
using EventLogExpert.UI.Common;
using EventLogExpert.UI.Tests.TestUtils;

namespace EventLogExpert.UI.Tests.LogTable.Resolution;

/// <summary>
///     Pins every coverage-modal localizer switch arm to its own resource key. <see cref="MarkerLocalizer" /> echoes
///     each key as <c>[[key]]</c>, so asserting an arm returns <c>[[{stem}{member}]]</c> proves the mapping is not just
///     present but correct - the enum-mapped guard (key set) and the neutral-drift guard (key values) both pass when two
///     valid arms are transposed; these do not. The null severity slot maps to the standalone <c>Severity_Unknown</c>
///     chrome key rather than an enum member.
/// </summary>
public sealed class ResolutionCoverageLocalizerMappingTests
{
    private readonly MarkerLocalizer _localizer = new();

    [Fact]
    public void CoverageStatusLocalizer_RoutesEachStatusToItsOwnKey()
    {
        foreach (CoverageStatus status in Enum.GetValues<CoverageStatus>())
        {
            Assert.Equal($"[[Coverage_Status_{status}]]", CoverageStatusLocalizer.Label(_localizer, status));
        }
    }

    [Fact]
    public void SeverityLevelLocalizer_RoutesEachLevelToItsOwnKey()
    {
        foreach (SeverityLevel level in Enum.GetValues<SeverityLevel>())
        {
            Assert.Equal($"[[Severity_Level_{level}]]", SeverityLevelLocalizer.Label(_localizer, level));
        }
    }

    [Fact]
    public void SeverityLevelLocalizer_RoutesNullLevelToUnknownKey()
    {
        Assert.Equal("[[Severity_Unknown]]", SeverityLevelLocalizer.Label(_localizer, null));
    }
}
