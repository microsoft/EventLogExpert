// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Runtime.Banner;

namespace EventLogExpert.UI.Banner;

public sealed record Preformatted(string Title, string Message, string? ActionLabel = null) : BannerMessage
{
    public string? ActionLabel { get; init; } = string.IsNullOrWhiteSpace(ActionLabel) ? null : ActionLabel;

    public override bool RequiresAction => ActionLabel is not null;
}
