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

        _fileOverrides = BuildOverrides(options, LoggingOptions.FileLogSink);
        _globalBaseline = globalBaseline;
    }

    public LogLevel FileMinimumFor(string category)
    {
        if (string.IsNullOrEmpty(category)) { return _globalBaseline; }

        if (TryMatchLongestPrefix(_runtimeOverrides, category, out LogLevel runtimeLevel)) { return runtimeLevel; }

        return TryMatchLongestPrefix(_fileOverrides, category, out LogLevel fileLevel) ? fileLevel : _globalBaseline;
    }

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

    public LogLevel UIMinimumFor(bool verbose) => verbose ? LogLevel.Trace : LogLevel.Information;

    public void UpdateGlobalBaseline(LogLevel level) => _globalBaseline = level;

    private static IReadOnlyList<CategoryOverride> BuildOverrides(LoggingOptions options, string sinkName)
    {
        if (!options.Sinks.TryGetValue(sinkName, out LogSinkOptions? sink)) { return []; }

        return [.. sink.Categories
            .Select(static pair => new CategoryOverride(pair.Key, pair.Value))
            .OrderByDescending(static entry => entry.Prefix.Length)];
    }

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
