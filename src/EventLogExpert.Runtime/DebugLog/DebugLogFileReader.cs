// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Logging.Sinks;
using EventLogExpert.Runtime.Common.Files;
using System.Runtime.CompilerServices;

namespace EventLogExpert.Runtime.DebugLog;

/// <summary>
///     Reads the debug log for the viewer. <see cref="LoadAsync" /> reads the (startup-bounded) file and yields its
///     lines NEWEST-first so the viewer pins the newest entry at the top; <see cref="ClearAsync" /> delegates to the
///     shared <see cref="FileLogSink" /> so truncation coordinates with the sink's active writer and interprocess mutex.
///     Does NOT dispose the sink (the DI container owns its lifetime).
/// </summary>
internal sealed class DebugLogFileReader(FileLocationOptions fileLocationOptions, FileLogSink fileSink) : IDebugLogReader
{
    public Task ClearAsync() => fileSink.ClearAsync();

    public async IAsyncEnumerable<string> LoadAsync(
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        // ReadWrite + Delete share: concurrent writers (second app instance) and rotation-driven deletion.
        var options = new FileStreamOptions
        {
            Mode = FileMode.Open,
            Access = FileAccess.Read,
            Share = FileShare.ReadWrite | FileShare.Delete,
            Options = FileOptions.Asynchronous | FileOptions.SequentialScan,
            BufferSize = 4096
        };

        FileStream stream;

        try
        {
            stream = new FileStream(fileLocationOptions.LoggingPath, options);
        }
        catch (Exception ex) when (ex is FileNotFoundException or DirectoryNotFoundException)
        {
            // Log not yet created (fresh install) or rotation deleted it; treat as empty.
            yield break;
        }

        var lines = new List<string>();

        await using (stream)
        {
            using var reader = new StreamReader(stream);

            while (await reader.ReadLineAsync(cancellationToken) is { } line)
            {
                lines.Add(line);
            }
        }

        for (var i = lines.Count - 1; i >= 0; i--)
        {
            cancellationToken.ThrowIfCancellationRequested();

            yield return lines[i];
        }
    }
}
