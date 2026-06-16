// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Filtering.Basic;
using EventLogExpert.Filtering.Evaluation;
using EventLogExpert.Filtering.Persistence;
using System.Collections.Immutable;

namespace EventLogExpert.Scenarios.Catalog;

/// <summary>Aggregates scenario sources into the immutable catalog and materialises filter sets.</summary>
public sealed class BuiltInScenarioRegistry
{
    private readonly ImmutableList<ScenarioDefinition> _scenarios;

    public BuiltInScenarioRegistry(IEnumerable<IScenarioSource> sources)
    {
        ArgumentNullException.ThrowIfNull(sources);

        var builder = ImmutableList.CreateBuilder<ScenarioDefinition>();
        var seenIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var errors = ImmutableList.CreateBuilder<string>();

        foreach (var source in sources)
        {
            foreach (var scenario in source.GetScenarios())
            {
                if (seenIds.Add(scenario.Id))
                {
                    builder.Add(scenario);
                }
                else
                {
                    errors.Add($"Scenario id '{scenario.Id}' is provided by more than one source (packs may not shadow built-ins).");
                }
            }
        }

        if (errors.Count > 0) { throw new ScenarioCatalogException(errors.ToImmutable()); }

        _scenarios =
        [
            .. builder
                .OrderBy(scenario => scenario.Group)
                .ThenBy(scenario => scenario.Priority)
                .ThenBy(scenario => scenario.Order)
                .ThenBy(scenario => scenario.Id, StringComparer.Ordinal)
        ];
    }

    public IReadOnlyList<ScenarioDefinition> Scenarios => _scenarios;

    /// <summary>Materialises a scenario's rows into an applyable Basic <c>SavedFilter</c> set.</summary>
    public ImmutableList<SavedFilter> BuildFilterSet(ScenarioDefinition scenario)
    {
        ArgumentNullException.ThrowIfNull(scenario);

        var builder = ImmutableList.CreateBuilder<SavedFilter>();

        foreach (var row in scenario.Filters)
        {
            if (!BasicFilterFormatter.TryFormat(row.Filter, strictPredicates: true, out var comparisonText))
            {
                throw new InvalidOperationException(
                    $"Scenario '{scenario.Id}' has a filter row that could not be formatted; the catalog should reject it at load.");
            }

            var saved = SavedFilter.TryCreate(comparisonText,
                    row.Filter,
                    color: row.Color,
                    isExcluded: row.IsExcluded,
                    isEnabled: true,
                    mode: FilterMode.Basic) ??
                throw new InvalidOperationException(
                    $"Scenario '{scenario.Id}' has a filter row '{comparisonText}' that could not be compiled; the catalog should reject it at load.");

            builder.Add(saved);
        }

        return builder.ToImmutable();
    }
}
