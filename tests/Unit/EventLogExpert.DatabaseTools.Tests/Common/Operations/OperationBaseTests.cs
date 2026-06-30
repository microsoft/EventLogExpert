// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.DatabaseTools.Common.Operations;
using EventLogExpert.Logging.Abstractions;
using EventLogExpert.Logging.Abstractions.Handlers;
using Microsoft.Extensions.Logging;

namespace EventLogExpert.DatabaseTools.Tests.Common.Operations;

public sealed class OperationBaseTests
{
    [Fact]
    public async Task CleanupPartialDatabaseAsync_WhenSidecarsExist_DeletesDatabaseWalAndShm()
    {
        using TestDirectory workspace = new();
        string databasePath = workspace.CreateFile("partial.db");
        string walPath = workspace.CreateFile("partial.db-wal");
        string shmPath = workspace.CreateFile("partial.db-shm");

        await OperationBaseHarness.CleanupAsync(new NullTraceLogger(), databasePath);

        Assert.False(File.Exists(databasePath));
        Assert.False(File.Exists(walPath));
        Assert.False(File.Exists(shmPath));
    }

    private sealed class NullTraceLogger : ITraceLogger
    {
        public LogLevel MinimumLevel => LogLevel.Trace;

        public void Critical(CriticalLogHandler handler) => handler.ToStringAndClear();

        public void Debug(DebugLogHandler handler) => handler.ToStringAndClear();

        public void Error(ErrorLogHandler handler) => handler.ToStringAndClear();

        public void Information(InformationLogHandler handler) => handler.ToStringAndClear();

        public void Trace(TraceLogHandler handler) => handler.ToStringAndClear();

        public void Warning(WarningLogHandler handler) => handler.ToStringAndClear();
    }

    private sealed class OperationBaseHarness : OperationBase
    {
        public static Task CleanupAsync(ITraceLogger logger, string targetPath) =>
            CleanupPartialDatabaseAsync(logger, dbContext: null, targetPath);
    }

    private sealed class TestDirectory : IDisposable
    {
        public TestDirectory()
        {
            Path = System.IO.Path.Combine(AppContext.BaseDirectory, "operation_base_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Path);
        }

        public string Path { get; }

        public string CreateFile(string fileName)
        {
            string filePath = System.IO.Path.Combine(Path, fileName);
            File.WriteAllText(filePath, "placeholder");

            return filePath;
        }

        public void Dispose()
        {
            if (Directory.Exists(Path)) { Directory.Delete(Path, recursive: true); }
        }
    }
}
