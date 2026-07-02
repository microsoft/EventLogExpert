// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.DatabaseTools.CreateDatabase;
using EventLogExpert.Logging.Abstractions;
using EventLogExpert.Logging.Sinks;
using Microsoft.Extensions.Logging;

namespace EventLogExpert.DatabaseTools.Tests.CreateDatabase;

// Offline validation failures must carry a fine Offline.* category (not the base operation category) so the
// debug-log filter can isolate them. A real StreamingTraceLogger is used because a category-blind double cannot
// observe re-categorization (ForCategory on a non-overriding double returns self).
public sealed class CreateDatabaseOfflineCategoryTests
{
    [Fact]
    public void ValidateOfflineImageRequest_WhenIsoRejected_StampsTheOfflineIsoCategory()
    {
        using var workspace = new TempImageFiles();
        string iso = workspace.CreateFile("image.iso");
        var captured = new List<LogRecord>();
        ITraceLogger logger = new StreamingTraceLogger(new RecordingProgress(captured.Add), LogLevel.Trace);
        var request = Request(offlineImagePath: iso, kind: OfflineImageKind.Iso);

        Assert.False(CreateDatabaseOperation.ValidateOfflineImageRequest(request, logger));
        Assert.NotEmpty(captured);
        Assert.All(captured, record => Assert.Equal(LogCategories.OfflineIso, record.Category));
    }

    [Fact]
    public void ValidateOfflineImageRequest_WhenOrphanImageKindWithoutImage_StampsTheOfflineRootCategory()
    {
        var captured = new List<LogRecord>();
        ITraceLogger logger = new StreamingTraceLogger(new RecordingProgress(captured.Add), LogLevel.Trace);
        var request = Request(offlineImagePath: null, kind: OfflineImageKind.Wim);

        Assert.False(CreateDatabaseOperation.ValidateOfflineImageRequest(request, logger));
        Assert.NotEmpty(captured);
        Assert.All(captured, record => Assert.Equal(LogCategories.Offline, record.Category));
    }

    [Fact]
    public void ValidateOfflineImageRequest_WhenVhdxRejected_StampsTheOfflineVhdxCategory()
    {
        using var workspace = new TempImageFiles();
        string vhdx = workspace.CreateFile("disk.vhdx");
        var captured = new List<LogRecord>();
        ITraceLogger logger = new StreamingTraceLogger(new RecordingProgress(captured.Add), LogLevel.Trace);
        var request = Request(offlineImagePath: vhdx, kind: OfflineImageKind.Vhdx, wimIndex: 1);

        Assert.False(CreateDatabaseOperation.ValidateOfflineImageRequest(request, logger));
        Assert.NotEmpty(captured);
        Assert.All(captured, record => Assert.Equal(LogCategories.OfflineVhdx, record.Category));
    }

    [Fact]
    public void ValidateOfflineImageRequest_WhenWimRejected_StampsTheOfflineWimCategory()
    {
        using var workspace = new TempImageFiles();
        string wim = workspace.CreateFile("image.wim");
        var captured = new List<LogRecord>();
        ITraceLogger logger = new StreamingTraceLogger(new RecordingProgress(captured.Add), LogLevel.Trace);
        var request = Request(offlineImagePath: wim, kind: OfflineImageKind.Wim);

        Assert.False(CreateDatabaseOperation.ValidateOfflineImageRequest(request, logger));
        Assert.NotEmpty(captured);
        Assert.All(captured, record => Assert.Equal(LogCategories.OfflineWim, record.Category));
    }

    private static CreateDatabaseRequest Request(
        string? offlineImagePath,
        OfflineImageKind? kind = null,
        int? wimIndex = null) =>
        new(@"C:\out.db", null, FilterRegex: null, SkipProvidersInFile: null, offlineImagePath, kind, wimIndex);

    private sealed class RecordingProgress(Action<LogRecord> onReport) : IProgress<LogRecord>
    {
        public void Report(LogRecord value) => onReport(value);
    }

    private sealed class TempImageFiles : IDisposable
    {
        public TempImageFiles() => System.IO.Directory.CreateDirectory(Directory);

        public string Directory { get; } = Path.Combine(Path.GetTempPath(), "elx_offlinecat_" + Guid.NewGuid().ToString("N"));

        public string CreateFile(string name)
        {
            string path = Path.Combine(Directory, name);
            File.WriteAllText(path, "placeholder");

            return path;
        }

        public void Dispose()
        {
            try
            {
                if (System.IO.Directory.Exists(Directory)) { System.IO.Directory.Delete(Directory, recursive: true); }
            }
            catch (IOException)
            {
            }
        }
    }
}
