// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Logging.Configuration;
using Microsoft.Extensions.Logging;

namespace EventLogExpert.Logging.Routing;

public sealed class LogRoutingPolicy
{
    private readonly IReadOnlyList<CategoryOverride> _fileOverrides;
    private readonly Lock _writeLock = new();

    private volatile LogLevel _globalBaseline;
    private volatile IReadOnlyList<CategoryOverride> _runtimeOverrides = [];

    public LogRoutingPolicy(LoggingOptions options, LogLevel globalBaseline)
    {
        ArgumentNullException.ThrowIfNull(options);

        _fileOverrides = BuildOverrides(options, LoggingOptions.FileSink);
        _globalBaseline = globalBaseline;
    }

    // The file sink writes a category at its configured throttle where one is set (channel-authoritative); every other
    // category follows the live global baseline, so raising the global level never un-floors a configured throttle.
    // Precedence returns on the first matching tier: runtime overrides (troubleshooting toggles) beat shipped
    // throttles, which beat the global baseline.
    public LogLevel FileMinimumFor(string category)
    {
        if (TryMatchLongestPrefix(_runtimeOverrides, category, out LogLevel runtimeLevel)) { return runtimeLevel; }

        return TryMatchLongestPrefix(_fileOverrides, category, out LogLevel fileLevel) ? fileLevel : _globalBaseline;
    }

    // Runtime per-category override (e.g. the verbose-resolution troubleshooting toggle): raise or reset a category
    // live without touching the shipped throttles or the global baseline. The read-modify-write runs under _writeLock
    // so concurrent writers cannot lose updates; readers take a single lock-free volatile snapshot. Because
    // FileMinimumFor returns on the first matching tier, a broad runtime prefix (e.g. "Resolution") shadows a narrower
    // shipped override ("Resolution.Sub") regardless of specificity - intended for the toggle, and harmless today
    // because no shipped "Resolution.*" override exists.
    public void SetCategoryOverride(string category, LogLevel? level)
    {
        ArgumentException.ThrowIfNullOrEmpty(category);

        lock (_writeLock)
        {
            IEnumerable<CategoryOverride> updated = _runtimeOverrides
                .Where(entry => !string.Equals(entry.Prefix, category, StringComparison.Ordinal));

            if (level.HasValue)
            {
                updated = updated.Append(new CategoryOverride(category, level.Value));
            }

            _runtimeOverrides = [.. updated.OrderByDescending(static entry => entry.Prefix.Length)];
        }
    }

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
        for (int index = 0; index < overrides.Count; index++)
        {
            CategoryOverride entry = overrides[index];

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
