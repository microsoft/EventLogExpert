// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Runtime.Common.Files;

namespace EventLogExpert.Adapters.FileSave;

public sealed class MauiFileSaveService(IFilePickerService filePickerService) : IFileSaveService
{
    private readonly IFilePickerService _filePickerService = filePickerService;

    public async Task<string?> SaveStreamingAsync(
        string suggestedFileName,
        IReadOnlyDictionary<string, IReadOnlyList<string>> fileTypes,
        Func<Stream, CancellationToken, Task> writeContent,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(suggestedFileName);
        ArgumentNullException.ThrowIfNull(fileTypes);
        ArgumentNullException.ThrowIfNull(writeContent);

        cancellationToken.ThrowIfCancellationRequested();

        var extensions = FlattenExtensions(fileTypes);
        var destinationPath = await _filePickerService.PickSaveAsync("Save As", extensions, suggestedFileName);

        if (destinationPath is null) { return null; }

        await AtomicFileWriter.WriteAsync(destinationPath, writeContent, cancellationToken);

        return destinationPath;
    }

    private static IReadOnlyList<string> FlattenExtensions(
        IReadOnlyDictionary<string, IReadOnlyList<string>> fileTypes)
    {
        if (fileTypes.Count == 0)
        {
            throw new ArgumentException("At least one file-type choice must be supplied.", nameof(fileTypes));
        }

        var extensions = fileTypes.Values
            .SelectMany(group => group)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        return extensions.Count == 0 ?
            throw new ArgumentException("At least one extension must be supplied.", nameof(fileTypes)) : extensions;
    }
}
