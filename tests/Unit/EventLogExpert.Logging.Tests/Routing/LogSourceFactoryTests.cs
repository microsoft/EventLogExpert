// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Logging.Abstractions;
using EventLogExpert.Logging.Routing;
using Microsoft.Extensions.Logging;

namespace EventLogExpert.Logging.Tests.Routing;

public sealed class LogSourceFactoryTests
{
    [Fact]
    public void Constructor_NullSinks_Throws() =>
        Assert.Throws<ArgumentNullException>(static () => new LogSourceFactory(null!));

    [Fact]
    public void DefaultCategory_IsApp() => Assert.Equal("App", LogSourceFactory.DefaultCategory);

    [Fact]
    public void ForCategory_DefaultsToInProcessOrigin()
    {
        var sink = new RecordingSink(_ => LogLevel.Trace);
        var factory = new LogSourceFactory([sink]);

        factory.ForCategory("App").Warning($"in process");

        var record = Assert.Single(sink.Received);
        Assert.Equal(ProcessOrigin.InProcess, record.ProcessOrigin);
    }

    [Fact]
    public void ForCategory_NullCategory_Throws()
    {
        var factory = new LogSourceFactory([new RecordingSink(_ => LogLevel.Trace)]);

        Assert.Throws<ArgumentNullException>(() => factory.ForCategory(null!));
    }

    [Fact]
    public void ForCategory_PropagatesProcessOrigin_FromTheFactory()
    {
        var sink = new RecordingSink(_ => LogLevel.Trace);
        var factory = new LogSourceFactory([sink], ProcessOrigin.ElevatedHelper);

        factory.ForCategory("App").Warning($"from helper");

        var record = Assert.Single(sink.Received);
        Assert.Equal(ProcessOrigin.ElevatedHelper, record.ProcessOrigin);
    }

    [Fact]
    public void ForCategory_SeparateCategories_ShareTheSameSinkSet()
    {
        var sink = new RecordingSink(_ => LogLevel.Trace);
        var factory = new LogSourceFactory([sink]);

        factory.ForCategory("Offline.Wim").Information($"a");
        factory.ForCategory("DatabaseTools.Merge").Information($"b");

        string[] origins = [.. sink.Received.Select(record => record.Origin)];

        Assert.Equal(["Offline.Wim", "DatabaseTools.Merge"], origins);
    }

    [Fact]
    public void ForCategory_StampsThatCategory_OnEmittedRecords()
    {
        var sink = new RecordingSink(_ => LogLevel.Trace);
        var factory = new LogSourceFactory([sink]);

        factory.ForCategory("DatabaseTools.Create").Information($"created");

        var record = Assert.Single(sink.Received);
        Assert.Equal("DatabaseTools.Create", record.Origin);
    }
}
