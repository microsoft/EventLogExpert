// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using System.Reflection;
using System.Runtime.Serialization;

namespace EventLogExpert.Filtering.Persistence;

public static class HighlightColorExtensions
{
    private static readonly IReadOnlyDictionary<HighlightColor, string?> s_cssNames = BuildCssNameMap();

    public static string? ToCssName(this HighlightColor color) =>
        s_cssNames.TryGetValue(color, out var name) ? name : null;

    private static IReadOnlyDictionary<HighlightColor, string?> BuildCssNameMap()
    {
        var type = typeof(HighlightColor);
        var map = new Dictionary<HighlightColor, string?>();
        var seen = new HashSet<string>(StringComparer.Ordinal);

        foreach (var color in Enum.GetValues<HighlightColor>())
        {
            if (color == HighlightColor.None)
            {
                map[color] = null;
                continue;
            }

            var field = type.GetField(color.ToString())
                ?? throw new InvalidOperationException(
                    $"HighlightColor.{color} field reflection failed; CSS-name contract cannot be built.");

            var attribute = field.GetCustomAttribute<EnumMemberAttribute>()
                ?? throw new InvalidOperationException(
                    $"HighlightColor.{color} is missing [EnumMember(Value=\"...\")] required for the CSS-name contract.");

            var value = attribute.Value;

            if (string.IsNullOrWhiteSpace(value))
            {
                throw new InvalidOperationException(
                    $"HighlightColor.{color} has [EnumMember] with a null or blank Value.");
            }

            if (!seen.Add(value))
            {
                throw new InvalidOperationException(
                    $"HighlightColor.{color} has duplicate [EnumMember] Value '{value}'; CSS names must be unique.");
            }

            map[color] = value;
        }

        return map;
    }
}
