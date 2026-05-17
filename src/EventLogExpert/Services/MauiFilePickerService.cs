// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Runtime.Common.Files;

namespace EventLogExpert.Services;

public sealed class MauiFilePickerService : IFilePickerService
{
    public Task<string?> PickAsync(string pickerTitle, IReadOnlyList<string> extensions)
    {
        ArgumentNullException.ThrowIfNull(pickerTitle);
        ArgumentNullException.ThrowIfNull(extensions);

        if (extensions.Count == 0)
        {
            throw new ArgumentException(
                "At least one extension must be supplied.", nameof(extensions));
        }

        return MainThread.InvokeOnMainThreadAsync(async () =>
        {
            var options = BuildOptions(pickerTitle, extensions);
            var result = await FilePicker.Default.PickAsync(options);
            return string.IsNullOrEmpty(result?.FullPath) ? null : result.FullPath;
        });
    }

    public Task<IReadOnlyList<string>> PickMultipleAsync(
        string pickerTitle,
        IReadOnlyList<string> extensions)
    {
        ArgumentNullException.ThrowIfNull(pickerTitle);
        ArgumentNullException.ThrowIfNull(extensions);

        if (extensions.Count == 0)
        {
            throw new ArgumentException(
                "At least one extension must be supplied.", nameof(extensions));
        }

        return MainThread.InvokeOnMainThreadAsync<IReadOnlyList<string>>(async () =>
        {
            var options = BuildOptions(pickerTitle, extensions);
            var results = await FilePicker.Default.PickMultipleAsync(options) ?? [];
            var paths = new List<string>();

            foreach (var result in results)
            {
                var path = result?.FullPath;

                if (!string.IsNullOrEmpty(path))
                {
                    paths.Add(path);
                }
            }

            return paths;
        });
    }

    private static PickOptions BuildOptions(string pickerTitle, IReadOnlyList<string> extensions) =>
        new()
        {
            PickerTitle = pickerTitle,
            FileTypes = new FilePickerFileType(
                new Dictionary<DevicePlatform, IEnumerable<string>>
                {
                    { DevicePlatform.WinUI, extensions }
                })
        };
}
