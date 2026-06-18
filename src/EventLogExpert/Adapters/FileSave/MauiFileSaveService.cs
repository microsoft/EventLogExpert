// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Platforms.Windows;
using EventLogExpert.Runtime.Common.Files;
using System.Runtime.InteropServices;
using Windows.Storage;
using Windows.Storage.Pickers;
using Windows.Storage.Provider;

namespace EventLogExpert.Adapters.FileSave;

public sealed class MauiFileSaveService : IFileSaveService
{
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

        var pickedFile = await PickSaveFileAsync(suggestedFileName, fileTypes);

        if (pickedFile is null) { return null; }

        var parentFolder = await TryGetParentAsync(pickedFile);
        var tempInParent = parentFolder is null
            ? null
            : await TryCreateTempFileAsync(parentFolder, pickedFile.Name);

        // Primary path requires write access to the picked file's folder; otherwise fall back to a local temp + copy.
        return tempInParent is not null
            ? await SaveByReplacingAsync(pickedFile, tempInParent, writeContent, cancellationToken)
            : await SaveByLocalTempCopyAsync(pickedFile, writeContent, cancellationToken);
    }

    private static async Task<StorageFile?> PickSaveFileAsync(
        string suggestedFileName, IReadOnlyDictionary<string, IReadOnlyList<string>> fileTypes)
    {
        if (fileTypes.Count == 0)
        {
            throw new ArgumentException("At least one file-type choice must be supplied.", nameof(fileTypes));
        }

        return await MainThread.InvokeOnMainThreadAsync(async () =>
        {
            var picker = new FileSavePicker
            {
                SuggestedStartLocation = PickerLocationId.ComputerFolder,
                SuggestedFileName = suggestedFileName
            };

            foreach ((string label, IReadOnlyList<string> extensions) in fileTypes)
            {
                picker.FileTypeChoices.Add(label, [.. extensions]);
            }

            PickerHostWindow.Initialize(picker);

            return await picker.PickSaveFileAsync();
        });
    }

    private static async Task<string?> SaveByLocalTempCopyAsync(
        StorageFile pickedFile, Func<Stream, CancellationToken, Task> writeContent, CancellationToken cancellationToken)
    {
        string localTempPath = Path.Combine(Path.GetTempPath(), $"eventlogexpert-export-{Guid.NewGuid():N}.tmp");
        bool deferred = false;
        bool completed = false;
        FileUpdateStatus status = FileUpdateStatus.Failed;

        try
        {
            await using (var tempStream = File.Create(localTempPath))
            {
                await writeContent(tempStream, cancellationToken);
            }

            cancellationToken.ThrowIfCancellationRequested();

            var localTempFile = await StorageFile.GetFileFromPathAsync(localTempPath);

            CachedFileManager.DeferUpdates(pickedFile);
            deferred = true;

            // Final cancellation gate before the uncancellable commit: if cancelled here, the finally resolves the
            // deferred-update contract and the picked file is left untouched (CopyAndReplaceAsync never runs).
            cancellationToken.ThrowIfCancellationRequested();

            // Replace the picked file from the fully-staged temp in a single platform-mediated operation rather than
            // truncating then re-filling it ourselves, which would guarantee a zero-length window on any copy fault.
            // This is best-effort rather than atomic on provider-backed destinations (see IFileSaveService docs).
            await localTempFile.CopyAndReplaceAsync(pickedFile);

            status = await CachedFileManager.CompleteUpdatesAsync(pickedFile);
            completed = true;
        }
        finally
        {
            if (deferred && !completed)
            {
                try { await CachedFileManager.CompleteUpdatesAsync(pickedFile); }
                catch (Exception) { /* best-effort: resolve the provider update contract on failure. */ }
            }

            try { File.Delete(localTempPath); }
            catch (Exception) { /* best-effort: the local temp is a copy source, never the saved output. */ }
        }

        return ToResultPath(pickedFile, status);
    }

    private static async Task<string?> SaveByReplacingAsync(
        StorageFile pickedFile,
        StorageFile tempFile,
        Func<Stream, CancellationToken, Task> writeContent,
        CancellationToken cancellationToken)
    {
        bool deferred = false;
        bool completed = false;
        bool replaced = false;
        FileUpdateStatus status = FileUpdateStatus.Failed;

        try
        {
            CachedFileManager.DeferUpdates(pickedFile);
            deferred = true;

            await using (var tempStream = await tempFile.OpenStreamForWriteAsync())
            {
                tempStream.SetLength(0);
                await writeContent(tempStream, cancellationToken);
            }

            cancellationToken.ThrowIfCancellationRequested();

            // Atomic commit: the picked file is replaced only after the write succeeds.
            await tempFile.MoveAndReplaceAsync(pickedFile);
            replaced = true;

            status = await CachedFileManager.CompleteUpdatesAsync(pickedFile);
            completed = true;
        }
        finally
        {
            if (deferred && !completed)
            {
                try { await CachedFileManager.CompleteUpdatesAsync(pickedFile); }
                catch (Exception) { /* best-effort: resolve the provider update contract on failure. */ }
            }

            if (!replaced)
            {
                // After a successful replace the temp IS the saved output and must not be deleted.
                try { await tempFile.DeleteAsync(); }
                catch (Exception) { /* best-effort cleanup. */ }
            }
        }

        return ToResultPath(pickedFile, status);
    }

    private static string ToResultPath(StorageFile pickedFile, FileUpdateStatus status) =>
        status is FileUpdateStatus.Complete or FileUpdateStatus.CompleteAndRenamed
            ? pickedFile.Path
            : throw new IOException($"Save failed with status '{status}'.");

    private static async Task<StorageFile?> TryCreateTempFileAsync(StorageFolder folder, string pickedName)
    {
        try
        {
            // The save picker may grant access only to the picked file, not its folder; fall back when creation is denied.
            return await folder.CreateFileAsync($"{pickedName}.tmp", CreationCollisionOption.GenerateUniqueName);
        }
        catch (Exception exception) when (exception is UnauthorizedAccessException or COMException)
        {
            // The parent folder already resolved, so a creation failure here is an access/permission issue; fall back
            // to the local-temp copy. Other (unexpected) exceptions propagate so a genuine fault is not masked by
            // silently choosing the non-atomic path.
            return null;
        }
    }

    private static async Task<StorageFolder?> TryGetParentAsync(StorageFile file)
    {
        try
        {
            return await file.GetParentAsync();
        }
        catch (Exception)
        {
            return null;
        }
    }
}
