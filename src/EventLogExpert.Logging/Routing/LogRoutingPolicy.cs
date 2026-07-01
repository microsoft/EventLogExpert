// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Logging.Configuration;
using Microsoft.Extensions.Logging;

namespace EventLogExpert.Logging.Routing;

public sealed class LogRoutingPolicy
{
    private readonly IReadOnlyList<CategoryOverride> _fileOverrides;

    private volatile LogLevel _globalBaseline;

    public LogRoutingPolicy(LoggingOptions options, LogLevel globalBaseline)
    {
        ArgumentNullException.ThrowIfNull(options);

        _fileOverrides = BuildOverrides(options, LoggingOptions.FileSink);
        _globalBaseline = globalBaseline;
    }

    // The file sink writes a category at its configured throttle where one is set (channel-authoritative); every other
    // category follows the live global baseline, so raising the global level never un-floors a configured throttle.
    public LogLevel FileMinimumFor(string category) =>
        TryMatchLongestPrefix(_fileOverrides, category, out LogLevel level) ? level : _globalBaseline;

    public LogLevel UiMinimumFor(bool verbose) => verbose ? LogLevel.Trace : LogLevel.Information;

    public void UpdateGlobalBaseline(LogLevel level) => _globalBaseline = level;

    private static IReadOnlyList<CategoryOverride> BuildOverrides(LoggingOptions options, string sinkName)
    {
        if (!options.Sinks.TryGetValue(sinkName, out LogSinkOptions? sink)) { return []; }

        return [.. sink.Categories
            .Select(static pair => new CategoryOverride(pair.Key, pair.Value))
            .OrderByDescending(static entry => entry.Prefix.Length)];
    }

    // Segment-boundary match: "Offline" covers "Offline.Wim" but not "OfflineExtras". Overrides are pre-sorted
    // longest-first, so the first match is the most specific.
    private static bool IsSegmentPrefix(string prefix, string category)
    {
        if (!category.StartsWith(prefix, StringComparison.Ordinal)) { return false; }

        return category.Length == prefix.Length || category[prefix.Length] == '.';
    }

    private static bool TryMatchLongestPrefix(IReadOnlyList<CategoryOverride> overrides, string category, out LogLevel level)
    {
        foreach (CategoryOverride entry in overrides)
        {
            if (IsSegmentPrefix(entry.Prefix, category))
            {
                level = entry.Level;

                return true;
            }
        }

        level = default;

        return false;
    }

    private readonly record struct CategoryOverride(string Prefix, LogLevel Level);
}
