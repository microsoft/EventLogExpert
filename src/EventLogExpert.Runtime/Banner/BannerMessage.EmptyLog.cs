// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

namespace EventLogExpert.Runtime.Banner;

public sealed record EmptyLogs(IReadOnlyList<string> DisplayNames) : BannerMessage
{
    public IReadOnlyList<string> DisplayNames { get; } = Validate(DisplayNames);

    private static IReadOnlyList<string> Validate(IReadOnlyList<string> displayNames)
    {
        ArgumentNullException.ThrowIfNull(displayNames);

        if (displayNames.Count == 0)
        {
            throw new ArgumentException("At least one display name is required.", nameof(displayNames));
        }

        return [.. displayNames];
    }
}
