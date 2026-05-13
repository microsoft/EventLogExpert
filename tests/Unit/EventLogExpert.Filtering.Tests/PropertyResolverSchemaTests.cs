// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Eventing.Common.Events;

namespace EventLogExpert.Filtering.Tests;

public sealed class PropertyResolverSchemaTests
{
    /// <summary>
    ///     Properties on <see cref="ResolvedEvent" /> that the filter grammar deliberately does not surface. If a new
    ///     property is added to <c>ResolvedEvent</c>, this test fails until either (a) the property is added to
    ///     <c>PropertyResolver</c>, or (b) it is added here with a comment explaining why it is not filterable.
    /// </summary>
    private static readonly HashSet<string> s_notFilterable =
    [
        "OwningLog", // Internal: identifies the log file/live channel; not user-visible filter target.
        "LogPathType", // Enum discriminator paired with OwningLog; not a user-facing filter target.
        "KeywordsDisplayName" // Computed convenience accessor; users filter via Keywords directly.
    ];

    [Fact]
    public void EveryResolvedEventProperty_IsEitherFilterable_OrExplicitlyExcluded()
    {
        var properties = typeof(ResolvedEvent).GetProperties();

        Assert.NotEmpty(properties);

        var unaccounted = new List<string>();

        foreach (var property in properties)
        {
            if (s_notFilterable.Contains(property.Name)) { continue; }

            // Use the .ToString() comparison shape because it normalizes every property type to a string compare,
            // sidestepping per-type literal coercion concerns. This is purely a property-name resolution check.
            var ok = FilterParser.TryValidate($"{property.Name}.ToString() == \"x\"", out _);

            if (!ok)
            {
                unaccounted.Add(property.Name);
            }
        }

        Assert.True(
            unaccounted.Count == 0,
            $"ResolvedEvent has properties not handled by the filter grammar: {string.Join(", ", unaccounted)}. " +
            "Add them to PropertyResolver, or add them to NotFilterable with a justification comment.");
    }

    [Fact]
    public void NotFilterableList_OnlyContainsExistingProperties()
    {
        var propertyNames = typeof(ResolvedEvent)
            .GetProperties()
            .Select(p => p.Name)
            .ToHashSet(StringComparer.Ordinal);

        var stale = s_notFilterable.Where(name => !propertyNames.Contains(name)).ToList();

        Assert.True(
            stale.Count == 0,
            $"NotFilterable contains names that are no longer on ResolvedEvent: {string.Join(", ", stale)}. " +
            "Remove these stale entries.");
    }
}
