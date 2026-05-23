// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Logging.Abstractions;
using EventLogExpert.Runtime.Common.Files;
using EventLogExpert.Runtime.Database.Upgrade;
using System.Collections.Immutable;
using System.IO.Compression;

namespace EventLogExpert.Runtime.Database;

internal sealed class DatabaseImportService(
    DatabaseRegistry registry,
    DatabaseClassificationService classificationService,
    DatabaseUpgradeService upgradeService,
    FileLocationOptions fileLocationOptions,
    ITraceLogger traceLogger)
{
    private readonly DatabaseClassificationService _classificationService = classificationService;
    private readonly FileLocationOptions _fileLocationOptions = fileLocationOptions;
    private readonly DatabaseRegistry _registry = registry;
    private readonly ITraceLogger _traceLogger = traceLogger;
    private readonly DatabaseUpgradeService _upgradeService = upgradeService;

    public async Task<IReadOnlyList<string>> EnumerateZipDbEntryNamesAsync(
        string sourceZipPath,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(sourceZipPath);

        ZipArchive archive;

        try
        {
            archive = await ZipFile.OpenReadAsync(sourceZipPath, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _traceLogger.Warning(
                $"{nameof(DatabaseImportService)}.{nameof(EnumerateZipDbEntryNamesAsync)} failed to open '{sourceZipPath}': {ex}");

            return [];
        }

        await using (archive)
        {
            var names = new List<string>();

            foreach (var entry in archive.Entries)
            {
                cancellationToken.ThrowIfCancellationRequested();

                if (string.IsNullOrEmpty(entry.Name)) { continue; }

                if (!Path.GetExtension(entry.Name).Equals(".db", StringComparison.OrdinalIgnoreCase)) { continue; }

                names.Add(entry.Name);
            }

            return names;
        }
    }

    public async Task<ImportResult> ImportAsync(
        IEnumerable<string> sourceFilePaths,
        CancellationToken cancellationToken = default) =>
        await ImportAsync(sourceFilePaths, ImmutableHashSet<string>.Empty, cancellationToken)
            .ConfigureAwait(false);

    public async Task<ImportResult> ImportAsync(
        IEnumerable<string> sourceFilePaths,
        IReadOnlySet<string> skipFileNames,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(sourceFilePaths);
        ArgumentNullException.ThrowIfNull(skipFileNames);

        var skipSet = skipFileNames as HashSet<string> is { Comparer: var comparer } &&
            comparer.Equals(StringComparer.OrdinalIgnoreCase)
                ? skipFileNames
                : skipFileNames.ToHashSet(StringComparer.OrdinalIgnoreCase);

        var importedCount = 0;
        var failures = new List<ImportFailure>();
        var importedNames = new List<string>();
        var freshlyImportedNames = new List<string>();

        Directory.CreateDirectory(_fileLocationOptions.DatabasePath);

        var existingFileNames = _registry.Entries
            .Select(entry => entry.FileName)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        foreach (var sourceFilePath in sourceFilePaths)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (string.IsNullOrEmpty(sourceFilePath)) { continue; }

            var fileName = Path.GetFileName(sourceFilePath);

            if (string.IsNullOrEmpty(fileName)) { continue; }

            if (Path.GetExtension(fileName).Equals(".zip", StringComparison.OrdinalIgnoreCase))
            {
                var (zipImported, zipFailures, zipImportedNames) = await ImportZipAsync(
                        sourceFilePath,
                        fileName,
                        skipSet,
                        cancellationToken)
                    .ConfigureAwait(false);

                importedCount += zipImported;
                failures.AddRange(zipFailures);

                foreach (var name in zipImportedNames)
                {
                    importedNames.Add(name);

                    if (!existingFileNames.Contains(name)) { freshlyImportedNames.Add(name); }
                }
            }
            else
            {
                if (skipSet.Contains(fileName)) { continue; }

                try
                {
                    var destinationPath = Path.Join(_fileLocationOptions.DatabasePath, fileName);

                    using (_registry.ReserveFileOperation(fileName, nameof(ImportAsync)))
                    {
                        File.Copy(sourceFilePath, destinationPath, true);
                    }

                    importedCount++;
                    importedNames.Add(fileName);

                    if (!existingFileNames.Contains(fileName)) { freshlyImportedNames.Add(fileName); }
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    failures.Add(new ImportFailure(fileName, ex.Message));

                    _traceLogger.Warning(
                        $"{nameof(DatabaseImportService)}.{nameof(ImportAsync)} failed to copy '{fileName}': {ex}");
                }
            }
        }

        if (importedCount <= 0)
        {
            return new ImportResult(importedCount, failures, []);
        }

        _registry.MarkFreshlyImportedDisabled(freshlyImportedNames);

        _registry.Refresh();

        try
        {
            await _classificationService.ClassifyEntriesAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _traceLogger.Warning(
                $"{nameof(DatabaseImportService)}.{nameof(ImportAsync)} post-import classification failed: {ex}");
        }

        IReadOnlyList<ImportFailure> upgradeFailures = [];

        var importedSet = importedNames.ToHashSet(StringComparer.OrdinalIgnoreCase);

        var upgradeNeeded = _registry.Entries
            .Where(entry => importedSet.Contains(entry.FileName) && entry.Status == DatabaseStatus.UpgradeRequired)
            .Select(entry => entry.FileName)
            .ToList();

        if (upgradeNeeded.Count <= 0)
        {
            return new ImportResult(importedCount, failures, upgradeFailures);
        }

        var batchResult = await _upgradeService.UpgradeBatchAsync(
                upgradeNeeded,
                UpgradeProgressScope.Background,
                cancellationToken)
            .ConfigureAwait(false);

        upgradeFailures = batchResult.Failed
            .Select(failure => new ImportFailure(failure.FileName, failure.Message))
            .ToList();

        return new ImportResult(importedCount, failures, upgradeFailures);
    }

    private async Task<(int Imported, IReadOnlyList<ImportFailure> Failures, IReadOnlyList<string> ImportedNames)>
        ImportZipAsync(
            string sourceZipPath,
            string zipFileName,
            IReadOnlySet<string> skipFileNames,
            CancellationToken cancellationToken)
    {
        var imported = 0;
        var failures = new List<ImportFailure>();
        var importedNames = new List<string>();

        ZipArchive archive;

        try
        {
            archive = await ZipFile.OpenReadAsync(sourceZipPath, cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            failures.Add(new ImportFailure(zipFileName, $"Could not open archive: {ex.Message}"));

            _traceLogger.Warning(
                $"{nameof(DatabaseImportService)}.{nameof(ImportZipAsync)} failed to open '{zipFileName}': {ex}");

            return (imported, failures, importedNames);
        }

        await using (archive)
        {
            foreach (var entry in archive.Entries)
            {
                cancellationToken.ThrowIfCancellationRequested();

                if (string.IsNullOrEmpty(entry.Name)) { continue; }

                if (!Path.GetExtension(entry.Name).Equals(".db", StringComparison.OrdinalIgnoreCase)) { continue; }

                if (skipFileNames.Contains(entry.Name)) { continue; }

                var destinationPath = Path.Join(_fileLocationOptions.DatabasePath, entry.Name);

                try
                {
                    using (_registry.ReserveFileOperation(entry.Name, nameof(ImportZipAsync)))
                    {
                        await using (var entryStream = await entry.OpenAsync(cancellationToken))
                        {
                            await using (var fileStream = File.Create(destinationPath))
                            {
                                await entryStream.CopyToAsync(fileStream, cancellationToken).ConfigureAwait(false);
                            }
                        }
                    }

                    imported++;
                    importedNames.Add(entry.Name);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    failures.Add(new ImportFailure(entry.Name, ex.Message));

                    _traceLogger.Warning(
                        $"{nameof(DatabaseImportService)}.{nameof(ImportZipAsync)} failed to extract '{entry.Name}' from '{zipFileName}': {ex}");

                    try
                    {
                        if (File.Exists(destinationPath))
                        {
                            File.Delete(destinationPath);
                        }
                    }
                    catch (Exception cleanupEx)
                    {
                        _traceLogger.Warning(
                            $"{nameof(DatabaseImportService)}.{nameof(ImportZipAsync)} failed to clean up partial extract '{destinationPath}': {cleanupEx}");
                    }
                }
            }
        }

        return (imported, failures, importedNames);
    }
}
