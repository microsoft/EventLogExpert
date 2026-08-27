// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Localization;
using Microsoft.Extensions.Localization;

namespace EventLogExpert.UI.Tests.TestUtils;

/// <summary>
///     Test <see cref="IStringLocalizer{T}" /> that returns each key as a <c>[[key]]</c> marker (and
///     <c>[[key(args)]]</c> for parameterized calls) instead of the real value, so a wiring test can assert a surface
///     routed its text through the localizer rather than emitting a hardcoded literal.
/// </summary>
internal sealed class MarkerLocalizer : IStringLocalizer<SharedResource>
{
    public LocalizedString this[string name] => new(name, $"[[{name}]]", resourceNotFound: false);

    public LocalizedString this[string name, params object[] arguments] =>
        new(name,
            arguments.Length == 0 ? $"[[{name}]]" : $"[[{name}({string.Join("|", arguments)})]]",
            resourceNotFound: false);

    public IEnumerable<LocalizedString> GetAllStrings(bool includeParentCultures) => [];
}
