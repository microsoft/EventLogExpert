// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Eventing.Common.Channels;
using EventLogExpert.Filtering.Basic;
using EventLogExpert.Filtering.Common.Filtering;
using EventLogExpert.Filtering.Persistence;
using EventLogExpert.Scenarios.Serialization;
using System.Collections.Immutable;
using System.Text;
using System.Text.Json;

namespace EventLogExpert.Scenarios.Catalog;

/// <summary>
///     Inverts the catalog loader: turns live Basic filter rows into ready-to-edit scenario-catalog JSON. A developer
///     authoring aid - the emitted skeleton uses TODO placeholders for unknown metadata, and the returned warnings surface
///     any row that would not load (canonicalization, color, or scoping issues).
/// </summary>
public static class ScenarioExporter
{
    public const string NoLiveChannelsWarning =
        "No live channels detected; the skeleton's channels[] is empty - fill it in before adding to the catalog.";

    public static ScenarioExportResult Export(IReadOnlyList<ScenarioExportRow> rows, ScenarioExportMetadata metadata)
    {
        ArgumentNullException.ThrowIfNull(rows);
        ArgumentNullException.ThrowIfNull(metadata);

        var channels = metadata.Channels.ToList();
        var requiresAdmin = channels.Any(LogChannelNames.AdminOnlyLiveChannels.Contains);
        var rowDtos = rows.Select(ToRowDto).ToList();

        var emit = new ScenarioDto
        {
            Id = metadata.Id ?? "TODO-scenario-id",
            Name = metadata.Name ?? "TODO name",
            Purpose = metadata.Purpose ?? "TODO purpose",
            Group = metadata.Group?.ToString() ?? "TODO",
            Channels = channels,
            RequiresAdmin = requiresAdmin,
            Priority = 0,
            Order = 0,
            Version = 1,
            Filters = rowDtos
        };

        var json = JsonSerializer.Serialize(
            new ScenarioFileDto { SchemaVersion = 1, Scenarios = [emit] },
            ScenarioJsonContext.Default.ScenarioFileDto);

        var warnings = ImmutableList.CreateBuilder<string>();
        warnings.AddRange(SelfValidate(metadata, channels, requiresAdmin, rowDtos));

        if (channels.Count == 0)
        {
            warnings.Add(NoLiveChannelsWarning);
        }

        return new ScenarioExportResult(json, warnings.ToImmutable(), rowDtos.Count);
    }

    /// <summary>
    ///     Loads the emitted rows under VALID sentinel metadata (kebab id, real group, a non-empty channel set) so the
    ///     loader's errors are about the ROWS - canonicalization, single-row color, excluded-row color, combined-channel
    ///     LogName scoping - rather than the human-facing TODO placeholders.
    /// </summary>
    private static IReadOnlyList<string> SelfValidate(
        ScenarioExportMetadata metadata,
        List<string> channels,
        bool requiresAdmin,
        List<ScenarioFilterRowDto> rowDtos)
    {
        if (rowDtos.Count == 0) { return []; }

        var validationChannels = channels.Count > 0 ? channels : ["Application"];

        var validation = new ScenarioDto
        {
            Id = "todo-scenario-id",
            Name = "TODO name",
            Purpose = "TODO purpose",
            Group = (metadata.Group ?? ScenarioGroup.SystemHealth).ToString(),
            Channels = validationChannels,
            RequiresAdmin = channels.Count > 0 && requiresAdmin,
            Priority = 0,
            Order = 0,
            Version = 1,
            Filters = rowDtos
        };

        var validationJson = JsonSerializer.Serialize(
            new ScenarioFileDto { SchemaVersion = 1, Scenarios = [validation] },
            ScenarioJsonContext.Default.ScenarioFileDto);

        return ScenarioCatalogLoader.TryLoad([("export.json", Encoding.UTF8.GetBytes(validationJson))]).Errors;
    }

    private static ComparisonDto ToComparisonDto(FilterComparison comparison)
    {
        var isMany = comparison.MatchMode == MatchMode.Many;

        return new ComparisonDto
        {
            Property = comparison.Property.ToString(),
            EventDataFieldName = comparison.Property == EventProperty.EventData ? comparison.EventDataFieldName : null,
            UserDataFieldName = comparison.Property == EventProperty.UserData ? comparison.UserDataFieldName : null,
            Operator = comparison.Operator == ComparisonOperator.Equals ? null : comparison.Operator.ToString(),
            MatchMode = isMany ? comparison.MatchMode.ToString() : null,
            Value = isMany ? null : comparison.Value,
            Values = isMany && comparison.Values.Count > 0 ? [.. comparison.Values] : null
        };
    }

    private static ScenarioFilterRowDto ToRowDto(ScenarioExportRow row) =>
        new()
        {
            Color = row.Color == HighlightColor.None ? null : row.Color.ToString(),
            Comparison = ToComparisonDto(row.Filter.Comparison),
            IsExcluded = row.IsExcluded ? true : null,
            Predicates = row.Filter.Predicates.Count == 0
                ? null
                :
                [
                    .. row.Filter.Predicates.Select(predicate => new PredicateDto
                    {
                        Comparison = ToComparisonDto(predicate.Comparison),
                        JoinWithAny = predicate.JoinWithAny
                    })
                ]
        };
}
