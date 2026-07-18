// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Eventing.Common.Channels;
using EventLogExpert.Eventing.Common.Events;
using EventLogExpert.Filtering.Basic;
using EventLogExpert.Filtering.Common.Filtering;
using EventLogExpert.Filtering.Evaluation;
using EventLogExpert.Filtering.Persistence;
using EventLogExpert.Scenarios.Catalog;
using System.Collections.Immutable;
using System.Reflection;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace EventLogExpert.Scenarios.Serialization;

/// <summary>Reads and strictly validates the embedded scenario JSON catalog.</summary>
internal static partial class ScenarioCatalogLoader
{
    private const int SupportedSchemaVersion = 1;

    private static readonly ResolvedEvent[] s_evaluationProbes =
    [
        new("probe", LogPathType.Channel),
        new("probe", LogPathType.Channel)
        {
            Id = 7045,
            LogName = LogChannelNames.SystemLog,
            Source = "Service Control Manager",
            Level = "Error",
            ProcessId = 4,
            ThreadId = 8,
            RecordId = 15,
            ActivityId = Guid.Empty
        }
    ];

    /// <summary>Loads + validates the catalog from an assembly's embedded resources, throwing on any error.</summary>
    internal static ImmutableList<ScenarioDefinition> Load(Assembly assembly)
    {
        var result = TryLoad(assembly);

        return !result.Errors.IsEmpty ? throw new ScenarioCatalogException(result.Errors) : result.Scenarios;
    }

    /// <summary>Loads + validates without throwing; returns scenarios and the full error list.</summary>
    internal static ScenarioCatalogLoadResult TryLoad(Assembly assembly)
    {
        ArgumentNullException.ThrowIfNull(assembly);

        var sources = assembly.GetManifestResourceNames()
            .Where(name => name.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
            .OrderBy(name => name, StringComparer.Ordinal)
            .Select(name => (name, ReadResource(assembly, name)))
            .ToList();

        return TryLoad(sources);
    }

    internal static ScenarioCatalogLoadResult TryLoad(IEnumerable<(string Name, byte[] Content)> sources)
    {
        ArgumentNullException.ThrowIfNull(sources);

        var scenarios = ImmutableList.CreateBuilder<ScenarioDefinition>();
        var errors = ImmutableList.CreateBuilder<string>();
        var seenIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var seenGuids = new HashSet<Guid>();

        foreach (var (name, content) in sources)
        {
            ScenarioFileDto? file;

            try
            {
                file = JsonSerializer.Deserialize(content, ScenarioJsonContext.Default.ScenarioFileDto);
            }
            catch (JsonException exception)
            {
                errors.Add($"{name}: invalid JSON - {exception.Message}");

                continue;
            }

            if (file is null)
            {
                errors.Add($"{name}: deserialized to null.");

                continue;
            }

            if (file.SchemaVersion != SupportedSchemaVersion)
            {
                errors.Add($"{name}: unsupported schemaVersion {file.SchemaVersion} (expected {SupportedSchemaVersion}).");

                continue;
            }

            if (file.Scenarios is null || file.Scenarios.Count == 0)
            {
                errors.Add($"{name}: contains no scenarios.");

                continue;
            }

            for (var index = 0; index < file.Scenarios.Count; index++)
            {
                var scenario = file.Scenarios[index];

                if (scenario is null)
                {
                    errors.Add($"{name}[{index}]: scenario is null.");

                    continue;
                }

                ProcessScenario(name, index, scenario, scenarios, errors, seenIds, seenGuids);
            }
        }

        return new ScenarioCatalogLoadResult(scenarios.ToImmutable(), errors.ToImmutable());
    }

    private static List<ScenarioFilterRow> BuildRows(List<ScenarioFilterRowDto>? dtos, string context, List<string> errors)
    {
        List<ScenarioFilterRow> rows = [];

        if (dtos is null) { return rows; }

        for (var i = 0; i < dtos.Count; i++)
        {
            var rowContext = $"{context} row[{i}]";
            var dto = dtos[i];

            if (dto is null)
            {
                errors.Add($"{rowContext}: filter row is null.");

                continue;
            }

            if (!TryBuildComparison(dto.Comparison, $"{rowContext}.comparison", errors, out var root)) { continue; }

            var predicates = ImmutableList.CreateBuilder<FilterPredicate>();
            var predicatesOk = true;

            if (dto.Predicates is not null)
            {
                for (var p = 0; p < dto.Predicates.Count; p++)
                {
                    var predicate = dto.Predicates[p];

                    if (predicate is null)
                    {
                        errors.Add($"{rowContext}.predicates[{p}]: predicate is null.");
                        predicatesOk = false;

                        continue;
                    }

                    if (TryBuildComparison(predicate.Comparison, $"{rowContext}.predicates[{p}]", errors, out var pc))
                    {
                        predicates.Add(new FilterPredicate(pc, predicate.JoinWithAny));
                    }
                    else
                    {
                        predicatesOk = false;
                    }
                }
            }

            if (!predicatesOk) { continue; }

            var color = ParseEnum(dto.Color, rowContext, "color", required: false, errors, HighlightColor.None);

            rows.Add(new ScenarioFilterRow(new BasicFilter(root, predicates.ToImmutable()), dto.IsExcluded ?? false, color));
        }

        return rows;
    }

    [GeneratedRegex("^[a-z0-9]+(-[a-z0-9]+)*$")]
    private static partial Regex KebabCaseRegex();

    private static TEnum ParseEnum<TEnum>(
        string? token,
        string context,
        string field,
        bool required,
        List<string> errors,
        TEnum fallback) where TEnum : struct, Enum
    {
        if (string.IsNullOrWhiteSpace(token))
        {
            if (required) { errors.Add($"{context}: missing {field}."); }

            return fallback;
        }

        // Case-sensitive name membership rejects typos and display values.
        if (Enum.IsDefined(typeof(TEnum), token))
        {
            return Enum.Parse<TEnum>(token);
        }

        errors.Add($"{context}: '{token}' is not a valid {field} (expected one of: {string.Join(", ", Enum.GetNames<TEnum>())}).");

        return fallback;
    }

    private static TEnum ParseEnum<TEnum>(
        string? token,
        string context,
        string field,
        bool required,
        List<string> errors,
        out bool ok) where TEnum : struct, Enum
    {
        var before = errors.Count;
        var value = ParseEnum(token, context, field, required, errors, default(TEnum));
        ok = errors.Count == before;

        return value;
    }

    private static void ProcessScenario(
        string source,
        int index,
        ScenarioDto dto,
        ImmutableList<ScenarioDefinition>.Builder scenarios,
        ImmutableList<string>.Builder errors,
        HashSet<string> seenIds,
        HashSet<Guid> seenGuids)
    {
        var label = string.IsNullOrWhiteSpace(dto.Id) ? $"{source}[{index}]" : $"'{dto.Id}'";
        List<string> local = [];

        if (string.IsNullOrWhiteSpace(dto.Id)) { local.Add($"scenario {label}: missing id."); }
        else if (!KebabCaseRegex().IsMatch(dto.Id)) { local.Add($"scenario {label}: id is not kebab-case."); }

        if (string.IsNullOrWhiteSpace(dto.Name)) { local.Add($"scenario {label}: missing name."); }

        if (string.IsNullOrWhiteSpace(dto.Purpose)) { local.Add($"scenario {label}: missing purpose."); }

        var group = ParseEnum<ScenarioGroup>(dto.Group, $"scenario {label}", "group", required: true, local, out _);
        var origin = ParseEnum(dto.Origin, $"scenario {label}", "origin", required: false, local, ScenarioOrigin.BuiltIn);
        var gating = ParseEnum(dto.Gating, $"scenario {label}", "gating", required: false, local, ScenarioGating.ChannelPresence);

        if (gating == ScenarioGating.SourceRegistration)
        {
            local.Add($"scenario {label}: gating 'SourceRegistration' is not supported in a built-in scenario yet.");
        }

        ValidateChannels(dto.Channels, $"scenario {label}", "channels", required: true, local);
        ValidateChannels(dto.OptionalChannels, $"scenario {label}", "optionalChannels", required: false, local);

        if (dto.Filters is null || dto.Filters.Count == 0)
        {
            local.Add($"scenario {label}: must declare at least one filter row.");
        }

        var rows = BuildRows(dto.Filters, $"scenario {label}", local);

        if (local.Count == 0)
        {
            var definition = new ScenarioDefinition
            {
                Id = dto.Id!,
                Name = dto.Name!,
                Purpose = dto.Purpose!,
                Group = group,
                Channels = [.. dto.Channels!],
                OptionalChannels = [.. dto.OptionalChannels ?? []],
                Gating = gating,
                SourceGates = [.. dto.SourceGates ?? []],
                RequiresAdmin = dto.RequiresAdmin,
                ActivatesTimeline = dto.ActivatesTimeline,
                Filters = [.. rows],
                Priority = dto.Priority,
                Order = dto.Order,
                Version = dto.Version,
                Origin = origin
            };

            ValidateDefinition(definition, $"scenario {label}", local);

            if (!seenIds.Add(definition.Id)) { local.Add($"scenario {label}: duplicate id."); }

            if (!seenGuids.Add(definition.StableGuid)) { local.Add($"scenario {label}: duplicate StableGuid."); }

            if (local.Count == 0) { scenarios.Add(definition); }
        }

        errors.AddRange(local);
    }

    private static byte[] ReadResource(Assembly assembly, string name)
    {
        using var stream = assembly.GetManifestResourceStream(name)
            ?? throw new InvalidOperationException($"Embedded resource '{name}' could not be opened.");
        using var memory = new MemoryStream();
        stream.CopyTo(memory);

        return memory.ToArray();
    }

    private static bool RowReferencesLogName(ScenarioFilterRow row) =>
        row.Filter.Comparison.Property == EventProperty.LogName ||
        row.Filter.Predicates.Any(predicate => predicate.Comparison.Property == EventProperty.LogName);

    private static bool TryBuildComparison(
        ComparisonDto? dto,
        string context,
        List<string> errors,
        out FilterComparison comparison)
    {
        comparison = new FilterComparison();

        if (dto is null)
        {
            errors.Add($"{context}: comparison is missing.");

            return false;
        }

        var property = ParseEnum<EventProperty>(dto.Property, context, "property", required: true, errors, out var propertyOk);
        var op = ParseEnum(dto.Operator, context, "operator", required: false, errors, ComparisonOperator.Equals);
        var matchMode = ParseEnum(dto.MatchMode, context, "matchMode", required: false, errors, MatchMode.Single);

        if (!propertyOk) { return false; }

        if (property == EventProperty.EventData && string.IsNullOrWhiteSpace(dto.EventDataFieldName))
        {
            errors.Add($"{context}: an EventData comparison requires a non-empty eventDataFieldName.");

            return false;
        }

        if (property == EventProperty.UserData && string.IsNullOrWhiteSpace(dto.UserDataFieldName))
        {
            errors.Add($"{context}: a UserData comparison requires a non-empty userDataFieldName.");

            return false;
        }

        if (matchMode == MatchMode.Many && (dto.Values is null || dto.Values.Count == 0))
        {
            errors.Add($"{context}: a Many comparison must provide at least one value.");

            return false;
        }

        if (matchMode == MatchMode.Many &&
            op is ComparisonOperator.Contains or ComparisonOperator.NotContains &&
            dto.Values is not null &&
            dto.Values.Any(string.IsNullOrEmpty))
        {
            errors.Add(
                $"{context}: a Contains/NotContains multi-value comparison must not contain a null or empty value " +
                "(it would match every event).");

            return false;
        }

        comparison = new FilterComparison
        {
            Property = property,
            Operator = op,
            MatchMode = matchMode,
            Value = dto.Value,
            Values = dto.Values is null ? [] : [.. dto.Values],
            EventDataFieldName = property == EventProperty.EventData ? dto.EventDataFieldName : null,
            UserDataFieldName = property == EventProperty.UserData ? dto.UserDataFieldName : null
        };

        return true;
    }

    private static void ValidateChannels(List<string>? channels, string context, string field, bool required, List<string> errors)
    {
        if (channels is null || channels.Count == 0)
        {
            if (required) { errors.Add($"{context}: {field} must list at least one channel."); }

            return;
        }

        foreach (var channel in channels)
        {
            if (string.IsNullOrWhiteSpace(channel)) { errors.Add($"{context}: {field} contains a blank channel name."); }
            else if (channel.Contains('*')) { errors.Add($"{context}: {field} channel '{channel}' must not contain a wildcard."); }
        }
    }

    private static void ValidateDefinition(ScenarioDefinition definition, string context, List<string> errors)
    {
        var requiresAdmin = definition.Channels.Any(LogChannelNames.AdminOnlyLiveChannels.Contains);

        if (requiresAdmin && !definition.RequiresAdmin)
        {
            errors.Add($"{context}: targets an admin-only channel but requiresAdmin is false.");
        }

        var isCombined = definition.Channels.Length > 1;

        if (definition.Filters.Length == 1 && definition.Filters[0].Color != HighlightColor.None)
        {
            errors.Add($"{context}: single-row scenarios must not use highlight colors.");
        }

        for (var i = 0; i < definition.Filters.Length; i++)
        {
            var row = definition.Filters[i];
            var rowContext = $"{context} row[{i}]";

            if (isCombined && !RowReferencesLogName(row))
            {
                errors.Add($"{rowContext}: combined scenario rows must be LogName-scoped.");
            }

            ValidateRow(row, rowContext, errors);
        }
    }

    private static void ValidateRow(ScenarioFilterRow row, string context, List<string> errors)
    {
        if (row.IsExcluded && row.Color != HighlightColor.None)
        {
            errors.Add($"{context}: excluded rows cannot have a highlight color.");
        }

        if (!BasicFilterFormatter.TryFormat(row.Filter, strictPredicates: true, out var text))
        {
            errors.Add($"{context}: filter did not format (a comparison or predicate is incomplete).");

            return;
        }

        var saved = SavedFilter.TryCreate(text, row.Filter, isExcluded: row.IsExcluded, mode: FilterMode.Basic);

        if (saved?.Compiled is null)
        {
            errors.Add($"{context}: filter '{text}' did not compile.");

            return;
        }

        foreach (var probe in s_evaluationProbes)
        {
            try
            {
                _ = saved.Compiled.Predicate(probe);
            }
            catch (Exception exception)
            {
                errors.Add($"{context}: filter '{text}' threw on evaluation - {exception.Message}");

                return;
            }
        }

        var roundTripped = SavedFilter.TryCreate(text, basicFilter: null, mode: FilterMode.Basic);

        if (roundTripped?.BasicFilter is null)
        {
            errors.Add($"{context}: filter '{text}' did not round-trip to a Basic filter.");

            return;
        }

        if (!BasicFilterFormatter.TryFormat(roundTripped.BasicFilter, strictPredicates: true, out var reformatted) ||
            !string.Equals(reformatted, text, StringComparison.Ordinal))
        {
            errors.Add($"{context}: filter '{text}' is not canonical (reformats to '{reformatted}').");
        }
    }
}
