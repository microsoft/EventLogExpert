// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Localization;
using EventLogExpert.Runtime.Common.Files;
using Microsoft.Extensions.Localization;

namespace EventLogExpert.Adapters.FileSave;

public sealed class MauiFileSaveService(
    IFilePickerService filePickerService,
    IStringLocalizer<SharedResource> localizer) : IFileSaveService
{
    private readonly IFilePickerService _filePickerService = filePickerService;
    private readonly IStringLocalizer<SharedResource> _localizer = localizer;

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
        var destinationPath = await _filePickerService.PickSaveAsync(
            _localizer["FilePicker_Title_SaveAs"], extensions, suggestedFileName);

        if (destinationPath is null) { return null; }

        await AtomicFileWriter.WriteAsync(destinationPath, writeContent, cancellationToken);

        return destinationPath;
    }

    private IReadOnlyList<string> FlattenExtensions(
        IReadOnlyDictionary<string, IReadOnlyList<string>> fileTypes)
    {
        if (fileTypes.Count == 0)
        {
            throw new ArgumentException(_localizer["FilePicker_Error_NoFileTypeChoice"], nameof(fileTypes));
        }

        var extensions = fileTypes.Values
            .SelectMany(group => group)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        return extensions.Count == 0 ?
            throw new ArgumentException(_localizer["FilePicker_Error_NoExtensions"], nameof(fileTypes)) : extensions;
    }
}
