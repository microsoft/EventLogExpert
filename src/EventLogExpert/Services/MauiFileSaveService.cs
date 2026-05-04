// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.UI.Interfaces;
using Windows.Storage;
using Windows.Storage.Pickers;
using Windows.Storage.Provider;
using WinRT.Interop;

namespace EventLogExpert.Services;

public sealed class MauiFileSaveService : IFileSaveService
{
    public async Task<string?> SaveAsync(
        string suggestedFileName,
        IReadOnlyDictionary<string, IReadOnlyList<string>> fileTypes,
        Func<Stream, Task> writeContent)
    {
        ArgumentNullException.ThrowIfNull(suggestedFileName);
        ArgumentNullException.ThrowIfNull(fileTypes);
        ArgumentNullException.ThrowIfNull(writeContent);

        if (fileTypes.Count == 0)
        {
            throw new ArgumentException(
                "At least one file-type choice must be supplied.", nameof(fileTypes));
        }

        var pickedFile = await MainThread.InvokeOnMainThreadAsync(async () =>
        {
            var picker = new FileSavePicker
            {
                SuggestedStartLocation = PickerLocationId.DocumentsLibrary,
                SuggestedFileName = suggestedFileName
            };

            foreach ((string label, IReadOnlyList<string> extensions) in fileTypes)
            {
                picker.FileTypeChoices.Add(label, [.. extensions]);
            }

            var current = Application.Current;
            var hostWindow = current?.Windows.Count > 0 ? current.Windows[0] : null;

            if (hostWindow?.Handler?.PlatformView is not MauiWinUIWindow window)
            {
                throw new InvalidOperationException(
                    "No MAUI host window is available to present the Save As dialog.");
            }

            InitializeWithWindow.Initialize(picker, window.WindowHandle);

            return await picker.PickSaveFileAsync();
        });

        if (pickedFile is null)
        {
            return null;
        }

        // Buffer first; picked file stays untouched if writeContent throws.
        using var buffer = new MemoryStream();
        await writeContent(buffer);
        buffer.Position = 0;

        // Required for provider-backed destinations (e.g., OneDrive).
        CachedFileManager.DeferUpdates(pickedFile);

        FileUpdateStatus status;

        try
        {
            await using var fileStream = await pickedFile.OpenStreamForWriteAsync();
            // OpenStreamForWriteAsync does not truncate; without this, larger files leave stale trailing bytes.
            fileStream.SetLength(0);
            await buffer.CopyToAsync(fileStream);
        }
        finally
        {
            status = await CachedFileManager.CompleteUpdatesAsync(pickedFile);
        }

        return status is not (FileUpdateStatus.Complete or FileUpdateStatus.CompleteAndRenamed) ?
            throw new IOException($"Save failed with status '{status}'.") : pickedFile.Path;
    }
}
