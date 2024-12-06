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

    public IEnumerable<string> LoadedDatabases { get; private set; } = [];

    private IEnumerable<string>? _disabledDatabases;

    public IEnumerable<string> DisabledDatabases
    {
        get
        {
            _disabledDatabases ??= _preferences.DisabledDatabasesPreference;

            return _disabledDatabases;
        }
        private set
        {
            _disabledDatabases = value;
            _preferences.DisabledDatabasesPreference = value;
        }
    }

    public EventHandler<IEnumerable<string>>? LoadedDatabasesChanged { get; set; }

    public void LoadDatabases()
    {
        LoadedDatabases = SortDatabases(_enabledDatabaseCollectionProvider.GetEnabledDatabases());

        LoadedDatabasesChanged?.Invoke(this, LoadedDatabases);
    }

    public void UpdateDisabledDatabases(IEnumerable<string> databases)
    {
        DisabledDatabases = databases;

        LoadDatabases();
    }

    private static IEnumerable<string> SortDatabases(IEnumerable<string> databases)
    {
        var r = SplitFileName();

        return databases
            .Select(name =>
            {
                var m = r.Match(name);

                return m.Success
                    ? new { FirstPart = m.Groups[1].Value + " ", SecondPart = m.Groups[2].Value }
                    : new { FirstPart = name, SecondPart = "" };
            })
            .OrderBy(n => n.FirstPart)
            .ThenByDescending(n => n.SecondPart)
            .Select(n => n.FirstPart + n.SecondPart);
    }

    [GeneratedRegex("^(.+) (\\S+)$")]
    private static partial Regex SplitFileName();
}
