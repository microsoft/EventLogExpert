// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Eventing.EventResolvers;
using EventLogExpert.Eventing.Helpers;
using EventLogExpert.UI.Interfaces;
using EventLogExpert.UI.Options;
using System.Collections.Immutable;

namespace EventLogExpert.UI.Services;

public interface IEnabledDatabaseCollectionProvider
{
    IList<string> GetEnabledDatabases();
}

public class EnabledDatabaseCollectionProvider : IDatabaseCollectionProvider, IEnabledDatabaseCollectionProvider
{
    private FileLocationOptions _fileLocationOptions;
    private IPreferencesProvider _preferencesProvider;
    private ITraceLogger _traceLogger;

    public EnabledDatabaseCollectionProvider(
        FileLocationOptions fileLocationOptions, 
        IPreferencesProvider preferencesProvider, 
        ITraceLogger traceLogger)
    {
        _fileLocationOptions = fileLocationOptions;
        _preferencesProvider = preferencesProvider;
        _traceLogger = traceLogger;
        SetActiveDatabases(GetEnabledDatabases());
    }

    public ImmutableList<string> ActiveDatabases { get; private set; } = ImmutableList<string>.Empty;

    public IList<string> GetEnabledDatabases()
    {
        List<string> databases = new();

        try
        {
            if (Directory.Exists(_fileLocationOptions.DatabasePath))
            {
                foreach (var item in Directory.EnumerateFiles(_fileLocationOptions.DatabasePath, "*.db"))
                {
                    databases.Add(Path.GetFileName(item));
                }
            }
        }
        catch (Exception ex)
        {
            _traceLogger.Trace($"{nameof(EnabledDatabaseCollectionProvider)}.{nameof(GetEnabledDatabases)} failed: {ex}");
        }

        var disabledDatabases = _preferencesProvider.DisabledDatabasesPreference;

        if (disabledDatabases?.Any() is true)
        {
            databases.RemoveAll(enabled => disabledDatabases
                .Any(disabled => string.Equals(enabled, disabled, StringComparison.InvariantCultureIgnoreCase)));
        }

        return databases;
    }

    public void SetActiveDatabases(IEnumerable<string> activeDatabases)
    {
        _traceLogger.Trace($"{nameof(EnabledDatabaseCollectionProvider)}.{nameof(SetActiveDatabases)} was called with {activeDatabases.Count()} databases.");
        ActiveDatabases = activeDatabases.ToImmutableList();
    }
}
