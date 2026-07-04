// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.DatabaseTools.Common.Ipc;
using EventLogExpert.DatabaseTools.Common.Operations;
using EventLogExpert.Eventing.OfflineImaging.Wim;
using EventLogExpert.Logging.Abstractions;
using Microsoft.Extensions.Logging;
using System.Text.Json;

namespace EventLogExpert.DatabaseTools.Tests.Common.Ipc;

public sealed class IpcMessageRoundTripTests
{
    [Fact]
    public void CancelMessage_RoundTrips_WithCancelDiscriminator()
    {
        var roundTripped = SerializeDeserialize(new CancelMessage(), out var json);

        AssertDiscriminator(json, "cancel");
        Assert.IsType<CancelMessage>(roundTripped);
    }

    [Fact]
    public void FatalMessage_RoundTrips_PreservesExceptionTypeMessageAndStack()
    {
        var original = new FatalMessage(
            "System.InvalidOperationException",
            "boom",
            "   at HelperX.Run() in helper.cs:line 42");

        var roundTripped = SerializeDeserialize(original, out var json);

        AssertDiscriminator(json, "fatal");
        var fatal = Assert.IsType<FatalMessage>(roundTripped);
        Assert.Equal(original, fatal);
    }

    [Fact]
    public void HelloMessage_RoundTrips_PreservesPidAndProtocolVersion()
    {
        var original = new HelloMessage(HelperProcessId: 12345, ProtocolVersion: 1);

        var roundTripped = SerializeDeserialize(original, out var json);

        AssertDiscriminator(json, "hello");
        var hello = Assert.IsType<HelloMessage>(roundTripped);
        Assert.Equal(12345, hello.HelperProcessId);
        Assert.Equal(1, hello.ProtocolVersion);
    }

    [Fact]
    public void ImageEditionsMessage_RoundTrips_PreservesStatusAndImages()
    {
        var original = new ImageEditionsMessage(
            WimImageListStatus.Ok,
            [
                new WimImageEntry(Index: 1, Name: "Windows 11 Pro", Edition: "Professional", TotalBytes: 15_000_000_000),
                new WimImageEntry(Index: 2, Name: "Windows 11 Home", Edition: "Core", TotalBytes: null)
            ]);

        var roundTripped = SerializeDeserialize(original, out var json);

        AssertDiscriminator(json, "image-editions");
        var editions = Assert.IsType<ImageEditionsMessage>(roundTripped);
        Assert.Equal(WimImageListStatus.Ok, editions.Status);
        Assert.Equal(2, editions.Images.Count);
        Assert.Equal(new WimImageEntry(1, "Windows 11 Pro", "Professional", 15_000_000_000), editions.Images[0]);
        Assert.Equal(new WimImageEntry(2, "Windows 11 Home", "Core", null), editions.Images[1]);
    }

    [Fact]
    public void ImageEditionsMessage_RoundTrips_WithEmptyImageList()
    {
        var original = new ImageEditionsMessage(WimImageListStatus.NotAWim, []);

        var roundTripped = SerializeDeserialize(original, out _);

        var editions = Assert.IsType<ImageEditionsMessage>(roundTripped);
        Assert.Equal(WimImageListStatus.NotAWim, editions.Status);
        Assert.Empty(editions.Images);
    }

    [Fact]
    public void LogMessage_RoundTrips_PreservesCategoryAndProcessOrigin()
    {
        var ts = new DateTime(2025, 6, 2, 14, 5, 0, DateTimeKind.Utc);
        var original = new LogMessage(ts, LogLevel.Warning, "provider X failed", "Offline.Wim");

        var roundTripped = SerializeDeserialize(original, out var json);

        AssertDiscriminator(json, "log");
        var log = Assert.IsType<LogMessage>(roundTripped);
        Assert.Equal("Offline.Wim", log.Category);
        Assert.Equal(ProcessOrigin.ElevatedHelper, log.ProcessOrigin);
        Assert.DoesNotContain("ElevatedHelper", json);
    }

    [Fact]
    public void LogMessage_RoundTrips_PreservesTimestampLevelAndMessage()
    {
        var ts = new DateTime(2025, 6, 2, 14, 5, 0, DateTimeKind.Utc);
        var original = new LogMessage(ts, LogLevel.Warning, "provider X failed");

        var roundTripped = SerializeDeserialize(original, out var json);

        AssertDiscriminator(json, "log");
        var log = Assert.IsType<LogMessage>(roundTripped);
        Assert.Equal(ts, log.TimestampUtc);
        Assert.Equal(DateTimeKind.Utc, log.TimestampUtc.Kind);
        Assert.Equal(LogLevel.Warning, log.Level);
        Assert.Equal("provider X failed", log.Message);
    }

    [Fact]
    public void ProbeMessage_RoundTrips_PreservesAllFields()
    {
        var original = new ProbeMessage(
            ProcessPath: @"C:\app\helper.exe",
            IntegrityLevel: "high",
            PackageIdentityOk: true,
            PackageIdentityError: null,
            LocalProviderEnumerationOk: true,
            LocalProviderEnumerationError: null,
            LocalProviderCount: 1337);

        var roundTripped = SerializeDeserialize(original, out var json);

        AssertDiscriminator(json, "probe");
        var probe = Assert.IsType<ProbeMessage>(roundTripped);
        Assert.Equal(original, probe);
    }

    [Fact]
    public void ProbeMessage_RoundTrips_WithErrorFieldsPopulated()
    {
        var original = new ProbeMessage(
            ProcessPath: @"C:\app\helper.exe",
            IntegrityLevel: "high",
            PackageIdentityOk: false,
            PackageIdentityError: "InvalidOperationException: no package identity",
            LocalProviderEnumerationOk: false,
            LocalProviderEnumerationError: "EventLogException: access denied",
            LocalProviderCount: 0);

        var roundTripped = SerializeDeserialize(original, out _);

        var probe = Assert.IsType<ProbeMessage>(roundTripped);
        Assert.Equal(original, probe);
    }

    [Fact]
    public void ProgressMessage_RoundTrips_WithNullableTotalAndCurrentItem()
    {
        var original = new ProgressMessage(Processed: 7, Total: null, CurrentItem: null);

        var roundTripped = SerializeDeserialize(original, out var json);

        AssertDiscriminator(json, "progress");
        var prog = Assert.IsType<ProgressMessage>(roundTripped);
        Assert.Equal(7, prog.Processed);
        Assert.Null(prog.Total);
        Assert.Null(prog.CurrentItem);
    }

    [Fact]
    public void ProgressMessage_RoundTrips_WithPopulatedTotalAndCurrentItem()
    {
        var original = new ProgressMessage(Processed: 42, Total: 100, CurrentItem: "Microsoft-Windows-Kernel-General");

        var roundTripped = SerializeDeserialize(original, out _);

        var prog = Assert.IsType<ProgressMessage>(roundTripped);
        Assert.Equal(original, prog);
    }

    [Theory]
    [InlineData(DatabaseToolsOutcome.Succeeded, null)]
    [InlineData(DatabaseToolsOutcome.Cancelled, "user cancelled")]
    [InlineData(DatabaseToolsOutcome.Failed, "InvalidOperationException: boom")]
    public void ResultMessage_RoundTrips_PreservesOutcomeFailureSummaryAndDuration(
        DatabaseToolsOutcome outcome, string? failureSummary)
    {
        var original = new ResultMessage(outcome, failureSummary, DurationMs: 12345);

        var roundTripped = SerializeDeserialize(original, out var json);

        AssertDiscriminator(json, "result");
        var result = Assert.IsType<ResultMessage>(roundTripped);
        Assert.Equal(outcome, result.Outcome);
        Assert.Equal(failureSummary, result.FailureSummary);
        Assert.Equal(12345L, result.DurationMs);
    }

    private static void AssertDiscriminator(string json, string expected)
    {
        using var doc = JsonDocument.Parse(json);
        Assert.True(doc.RootElement.TryGetProperty("$type", out var typeProperty),
            $"Message JSON missing '$type' discriminator. JSON was: {json}");
        Assert.Equal(expected, typeProperty.GetString());
    }

    private static DatabaseToolsIpcMessage SerializeDeserialize(DatabaseToolsIpcMessage message, out string json)
    {
        json = JsonSerializer.Serialize(message, DatabaseToolsIpcSerializer.Options);

        var roundTripped = JsonSerializer.Deserialize<DatabaseToolsIpcMessage>(
            json, DatabaseToolsIpcSerializer.Options);

        Assert.NotNull(roundTripped);
        return roundTripped;
    }
}
