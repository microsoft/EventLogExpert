// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Eventing.Common.Events;
using EventLogExpert.Eventing.Tests.TestUtils;
using System.Globalization;

namespace EventLogExpert.Eventing.Tests.Common.Events;

/// <summary>
///     Locks <see cref="ResolutionStatusTokens" /> against regional culture: the tokens are persisted into the column
///     store and compiled into saved-filter predicates, so <see cref="ResolutionStatusTokens.Format" /> must return the
///     byte-frozen token regardless of the OS <see cref="CultureInfo.CurrentCulture" />.
/// </summary>
/// <remarks>
///     <c>Format</c> is a <c>const</c> switch reaching no localizer, so this asserts CurrentCulture-independence
///     only; UICulture localizer-independence is not asserted (there is no localizer to engage, and a qps-ploc comparison
///     is vacuous). Contrast culture is <c>fi-FI</c> for consistency with the copy/export guards. The neutral byte
///     contract lives in <c>ResolutionStatusTokensTests.Format_ForEachStatus_ReturnsItsFrozenToken</c>.
/// </remarks>
[Collection(CultureSensitiveCollection.Name)]
public sealed class ResolutionStatusTokensCultureTests
{
    [Theory]
    [InlineData(EventResolutionStatus.Resolved, ResolutionStatusTokens.Resolved)]
    [InlineData(EventResolutionStatus.NoProvider, ResolutionStatusTokens.NoProvider)]
    [InlineData(EventResolutionStatus.NoMessage, ResolutionStatusTokens.NoMessage)]
    [InlineData(EventResolutionStatus.Failed, ResolutionStatusTokens.Failed)]
    public void Format_UnderForeignCulture_ReturnsFrozenToken(EventResolutionStatus status, string expectedToken)
    {
        CultureInfo priorCulture = CultureInfo.CurrentCulture;
        CultureInfo priorUiCulture = CultureInfo.CurrentUICulture;

        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("fi-FI");
            CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo("en"); // isolate the regional axis from the localization axis

            Assert.Equal(expectedToken, ResolutionStatusTokens.Format(status));
        }
        finally
        {
            CultureInfo.CurrentCulture = priorCulture;
            CultureInfo.CurrentUICulture = priorUiCulture;
        }
    }
}
