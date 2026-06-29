// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.DatabaseTools.CreateDatabase;
using EventLogExpert.Logging.Abstractions;
using EventLogExpert.Logging.Abstractions.Handlers;
using Microsoft.Extensions.Logging;

namespace EventLogExpert.DatabaseTools.Tests.CreateDatabase;

/// <summary>
///     Locks the kind-aware request validation for offline images. These combinations are pure (no <c>wimgapi</c>):
///     they guard against a WIM option being silently ignored (which would build from the wrong source) and against
///     opening a directory as a WIM or vice versa. The actual WIM extraction outcomes are covered by the seam-driven
///     <c>OfflineWimImageTests</c> in the Eventing test project and the manual real-WIM E2E.
/// </summary>
public sealed class CreateDatabaseWimValidationTests
{
    [Fact]
    public void Validate_AutoDetectsDirectory_WhenNoKindGiven_Accepts()
    {
        using var workspace = new TempFiles();
        var logger = new CapturingTraceLogger();
        var request = Request(offlineImagePath: workspace.Directory, kind: null);

        Assert.True(CreateDatabaseOperation.ValidateOfflineImageRequest(request, logger));
    }

    [Fact]
    public void Validate_AutoDetectsIso_FromExtension_WithIndex_Accepts()
    {
        using var workspace = new TempFiles();
        string iso = workspace.CreateFile("image.iso");
        var logger = new CapturingTraceLogger();
        var request = Request(offlineImagePath: iso, kind: null, wimIndex: 1);

        Assert.True(CreateDatabaseOperation.ValidateOfflineImageRequest(request, logger));
    }

    [Fact]
    public void Validate_AutoDetectsWim_FromExtension_WithIndex_Accepts()
    {
        using var workspace = new TempFiles();
        string wim = workspace.CreateFile("image.wim");
        var logger = new CapturingTraceLogger();
        var request = Request(offlineImagePath: wim, kind: null, wimIndex: 1);

        Assert.True(CreateDatabaseOperation.ValidateOfflineImageRequest(request, logger));
    }

    [Fact]
    public void Validate_AutoDetectsWim_FromExtension_WithoutIndex_Rejects()
    {
        using var workspace = new TempFiles();
        string wim = workspace.CreateFile("image.esd");
        var logger = new CapturingTraceLogger();
        var request = Request(offlineImagePath: wim, kind: null);

        Assert.False(CreateDatabaseOperation.ValidateOfflineImageRequest(request, logger));
        Assert.Contains(logger.Errors, error => error.Contains("--wim-index", StringComparison.Ordinal));
    }

    [Fact]
    public void Validate_AutoDetectUnknownExtension_Rejects()
    {
        using var workspace = new TempFiles();
        string unknown = workspace.CreateFile("image.dat");
        var logger = new CapturingTraceLogger();
        var request = Request(offlineImagePath: unknown, kind: null);

        Assert.False(CreateDatabaseOperation.ValidateOfflineImageRequest(request, logger));
        Assert.Contains(logger.Errors, error => error.Contains("determine", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Validate_WhenDirectoryExists_Accepts()
    {
        using var workspace = new TempFiles();
        var logger = new CapturingTraceLogger();
        var request = Request(offlineImagePath: workspace.Directory, kind: OfflineImageKind.Directory);

        Assert.True(CreateDatabaseOperation.ValidateOfflineImageRequest(request, logger));
    }

    [Fact]
    public void Validate_WhenDirectoryKindGivenAFile_RejectsWithActionableMessage()
    {
        using var workspace = new TempFiles();
        string wim = workspace.CreateFile("image.wim");
        var logger = new CapturingTraceLogger();
        var request = Request(offlineImagePath: wim, kind: OfflineImageKind.Directory);

        // A .wim passed without --image-kind wim must not say "directory not found" (the file exists); the error should
        // identify it as a file and point at --image-kind wim.
        Assert.False(CreateDatabaseOperation.ValidateOfflineImageRequest(request, logger));
        Assert.Contains(logger.Errors, error =>
            error.Contains("file", StringComparison.OrdinalIgnoreCase) && error.Contains("--image-kind wim", StringComparison.Ordinal));
    }

    [Fact]
    public void Validate_WhenDirectoryMissing_Rejects()
    {
        var logger = new CapturingTraceLogger();
        var request = Request(
            offlineImagePath: Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N")), kind: OfflineImageKind.Directory);

        Assert.False(CreateDatabaseOperation.ValidateOfflineImageRequest(request, logger));
    }

    [Fact]
    public void Validate_WhenDirectoryWithWimIndex_Rejects()
    {
        using var workspace = new TempFiles();
        var logger = new CapturingTraceLogger();
        var request = Request(offlineImagePath: workspace.Directory, kind: OfflineImageKind.Directory, wimIndex: 2);

        Assert.False(CreateDatabaseOperation.ValidateOfflineImageRequest(request, logger));
        Assert.Contains(logger.Errors, error => error.Contains("--wim-index", StringComparison.Ordinal));
    }

    [Fact]
    public void Validate_WhenImageAndSource_Rejects()
    {
        using var workspace = new TempFiles();
        var logger = new CapturingTraceLogger();
        var request = Request(offlineImagePath: workspace.Directory, source: @"C:\src.db");

        Assert.False(CreateDatabaseOperation.ValidateOfflineImageRequest(request, logger));
    }

    [Fact]
    public void Validate_WhenIsoFileMissing_Rejects()
    {
        var logger = new CapturingTraceLogger();
        var request = Request(offlineImagePath: @"C:\missing.iso", kind: OfflineImageKind.Iso, wimIndex: 1);

        Assert.False(CreateDatabaseOperation.ValidateOfflineImageRequest(request, logger));
        Assert.Contains(logger.Errors, error => error.Contains("not found", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Validate_WhenIsoKindOnNonIsoFile_Rejects()
    {
        using var workspace = new TempFiles();
        string notIso = workspace.CreateFile("image.dat");
        var logger = new CapturingTraceLogger();
        var request = Request(offlineImagePath: notIso, kind: OfflineImageKind.Iso, wimIndex: 1);

        Assert.False(CreateDatabaseOperation.ValidateOfflineImageRequest(request, logger));
        Assert.Contains(logger.Errors, error => error.Contains(".iso", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Validate_WhenIsoWithoutIndex_Rejects()
    {
        using var workspace = new TempFiles();
        string iso = workspace.CreateFile("image.iso");
        var logger = new CapturingTraceLogger();
        var request = Request(offlineImagePath: iso, kind: OfflineImageKind.Iso);

        Assert.False(CreateDatabaseOperation.ValidateOfflineImageRequest(request, logger));
        Assert.Contains(logger.Errors, error => error.Contains("--wim-index", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Validate_WhenNoImageAndNoWimOptions_Accepts()
    {
        var logger = new CapturingTraceLogger();
        var request = Request(offlineImagePath: null, kind: null);

        Assert.True(CreateDatabaseOperation.ValidateOfflineImageRequest(request, logger));
    }

    [Fact]
    public void Validate_WhenNoImageButImageKindWim_Rejects()
    {
        var logger = new CapturingTraceLogger();
        var request = Request(offlineImagePath: null, kind: OfflineImageKind.Wim);

        Assert.False(CreateDatabaseOperation.ValidateOfflineImageRequest(request, logger));
        Assert.Contains(logger.Errors, error => error.Contains("--image-kind", StringComparison.Ordinal));
    }

    [Fact]
    public void Validate_WhenNoImageButWimIndexSet_Rejects()
    {
        var logger = new CapturingTraceLogger();
        var request = Request(offlineImagePath: null, kind: null, wimIndex: 1);

        Assert.False(CreateDatabaseOperation.ValidateOfflineImageRequest(request, logger));
        Assert.Contains(logger.Errors, error => error.Contains("--wim-index", StringComparison.Ordinal));
    }

    [Fact]
    public void Validate_WhenWimFileMissing_Rejects()
    {
        var logger = new CapturingTraceLogger();
        var request = Request(
            offlineImagePath: Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N") + ".wim"),
            kind: OfflineImageKind.Wim,
            wimIndex: 1);

        Assert.False(CreateDatabaseOperation.ValidateOfflineImageRequest(request, logger));
        Assert.Contains(logger.Errors, error => error.Contains("not found", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Validate_WhenWimHasWrongExtension_Rejects()
    {
        using var workspace = new TempFiles();
        string notWim = workspace.CreateFile("image.dat");
        var logger = new CapturingTraceLogger();
        var request = Request(offlineImagePath: notWim, kind: OfflineImageKind.Wim, wimIndex: 1);

        Assert.False(CreateDatabaseOperation.ValidateOfflineImageRequest(request, logger));
        Assert.Contains(logger.Errors, error => error.Contains(".wim", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Validate_WhenWimWithIndexAndExtension_Accepts()
    {
        using var workspace = new TempFiles();
        string wim = workspace.CreateFile("image.esd");
        var logger = new CapturingTraceLogger();
        var request = Request(offlineImagePath: wim, kind: OfflineImageKind.Wim, wimIndex: 1);

        // Validation passes on the request shape; the index range, elevation, and extraction are checked by the apply.
        Assert.True(CreateDatabaseOperation.ValidateOfflineImageRequest(request, logger));
    }

    [Fact]
    public void Validate_WhenWimWithoutIndex_Rejects()
    {
        using var workspace = new TempFiles();
        string wim = workspace.CreateFile("image.wim");
        var logger = new CapturingTraceLogger();
        var request = Request(offlineImagePath: wim, kind: OfflineImageKind.Wim);

        Assert.False(CreateDatabaseOperation.ValidateOfflineImageRequest(request, logger));
        Assert.Contains(logger.Errors, error => error.Contains("--wim-index", StringComparison.Ordinal));
    }

    private static CreateDatabaseRequest Request(
        string? offlineImagePath,
        string? source = null,
        OfflineImageKind? kind = null,
        int? wimIndex = null) =>
        new(@"C:\out.db", source, FilterRegex: null, SkipProvidersInFile: null, offlineImagePath, kind, wimIndex);

    private sealed class CapturingTraceLogger : ITraceLogger
    {
        public List<string> Errors { get; } = [];

        public LogLevel MinimumLevel => LogLevel.Trace;

        public void Critical(CriticalLogHandler handler) => handler.ToStringAndClear();

        public void Debug(DebugLogHandler handler) => handler.ToStringAndClear();

        public void Error(ErrorLogHandler handler) => Errors.Add(handler.ToStringAndClear());

        public void Information(InformationLogHandler handler) => handler.ToStringAndClear();

        public void Trace(TraceLogHandler handler) => handler.ToStringAndClear();

        public void Warning(WarningLogHandler handler) => handler.ToStringAndClear();
    }

    private sealed class TempFiles : IDisposable
    {
        public TempFiles()
        {
            Directory = Path.Combine(Path.GetTempPath(), "elx_wimvalidate_" + Guid.NewGuid().ToString("N"));
            System.IO.Directory.CreateDirectory(Directory);
        }

        public string Directory { get; }

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
                // Best-effort cleanup.
            }
        }
    }
}
