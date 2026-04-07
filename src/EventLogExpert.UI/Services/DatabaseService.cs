// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.UI.Interfaces;
using System.Text.RegularExpressions;

namespace EventLogExpert.UI.Services;

public sealed partial class DatabaseService(
    IEnabledDatabaseCollectionProvider enabledDatabaseCollectionProvider,
    IPreferencesProvider preferences) : IDatabaseService
{
    private readonly IEnabledDatabaseCollectionProvider _enabledDatabaseCollectionProvider =
        enabledDatabaseCollectionProvider;
    private readonly IPreferencesProvider _preferences = preferences;

    public IEnumerable<string> LoadedDatabases
    {
        get
        {
            _loadedDatabases ??= SortDatabases(_enabledDatabaseCollectionProvider.GetEnabledDatabases());

            return _loadedDatabases;
        }
    }

    private IEnumerable<string>? _disabledDatabases;
    private IEnumerable<string>? _loadedDatabases;

    public IEnumerable<string> DisabledDatabases
    {
        get
        {
            _disabledDatabases ??= _preferences.DisabledDatabasesPreference;

            return _disabledDatabases;
        }
        private set
        {
            _disabledDatabases = value.ToList().AsReadOnly();
            _preferences.DisabledDatabasesPreference = _disabledDatabases;
        }
    }

    public EventHandler<IEnumerable<string>>? LoadedDatabasesChanged { get; set; }

    public void LoadDatabases()
    {
        _loadedDatabases = SortDatabases(_enabledDatabaseCollectionProvider.GetEnabledDatabases());

        LoadedDatabasesChanged?.Invoke(this, LoadedDatabases);
    }

    public void UpdateDisabledDatabases(IEnumerable<string> databases)
    {
        DisabledDatabases = databases;

        LoadDatabases();
    }

    private static IEnumerable<string> SortDatabases(IEnumerable<string> databases)
    {
        if (!databases.Any()) { return []; }

        var r = SplitFileName();

        return databases
            .Select(name =>
            {
                var m = r.Match(name);

                if (m.Success)
                {
                    var versionString = m.Groups[2].Value;

                    // Try to parse the version as a number for proper numeric ordering.
                    // This ensures "10" sorts after "2" rather than before it (lexicographic).
                    int? numericVersion = int.TryParse(versionString, out var parsed) ? parsed : null;

                    return new
                    {
                        FirstPart = m.Groups[1].Value + " ",
                        SecondPart = versionString,
                        NumericVersion = numericVersion
                    };
                }

                return new
                {
                    FirstPart = name,
                    SecondPart = "",
                    NumericVersion = (int?)null
                };
            })
            .OrderBy(n => n.FirstPart)
            .ThenByDescending(n => n.NumericVersion ?? int.MinValue)
            .ThenByDescending(n => n.SecondPart)
            .Select(n => n.FirstPart + n.SecondPart);
    }

    [GeneratedRegex("^(.+) (\\S+)$")]
    private static partial Regex SplitFileName();
}
