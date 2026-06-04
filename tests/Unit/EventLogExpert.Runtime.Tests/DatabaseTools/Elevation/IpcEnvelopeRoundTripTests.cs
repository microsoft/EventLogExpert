// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.DatabaseTools.Common.Ipc;
using EventLogExpert.DatabaseTools.Common.Operations;
using Microsoft.Extensions.Logging;
using System.Text.Json;

namespace EventLogExpert.Runtime.Tests.DatabaseTools.Elevation;

public sealed class IpcEnvelopeRoundTripTests
{
    [Fact]
    public void CancelEnvelope_RoundTrips_WithCancelDiscriminator()
    {
        var roundTripped = SerializeDeserialize(new CancelEnvelope(), out var json);

        AssertDiscriminator(json, "cancel");
        Assert.IsType<CancelEnvelope>(roundTripped);
    }

    [Fact]
    public void FatalEnvelope_RoundTrips_PreservesExceptionTypeMessageAndStack()
    {
        var original = new FatalEnvelope(
            "System.InvalidOperationException",
            "boom",
            "   at HelperX.Run() in helper.cs:line 42");

        var roundTripped = SerializeDeserialize(original, out var json);

        AssertDiscriminator(json, "fatal");
        var fatal = Assert.IsType<FatalEnvelope>(roundTripped);
        Assert.Equal(original, fatal);
    }

    [Fact]
    public void HelloEnvelope_RoundTrips_PreservesPidAndProtocolVersion()
    {
        var original = new HelloEnvelope(HelperProcessId: 12345, ProtocolVersion: 1);

        var roundTripped = SerializeDeserialize(original, out var json);

        AssertDiscriminator(json, "hello");
        var hello = Assert.IsType<HelloEnvelope>(roundTripped);
        Assert.Equal(12345, hello.HelperProcessId);
        Assert.Equal(1, hello.ProtocolVersion);
    }

    [Fact]
    public void LogEnvelope_RoundTrips_PreservesTimestampLevelAndMessage()
    {
        var ts = new DateTime(2025, 6, 2, 14, 5, 0, DateTimeKind.Utc);
        var original = new LogEnvelope(ts, LogLevel.Warning, "provider X failed");

        var roundTripped = SerializeDeserialize(original, out var json);

        AssertDiscriminator(json, "log");
        var log = Assert.IsType<LogEnvelope>(roundTripped);
        Assert.Equal(ts, log.TimestampUtc);
        Assert.Equal(DateTimeKind.Utc, log.TimestampUtc.Kind);
        Assert.Equal(LogLevel.Warning, log.Level);
        Assert.Equal("provider X failed", log.Message);
    }

    [Fact]
    public void ProbeEnvelope_RoundTrips_PreservesAllFields()
    {
        var original = new ProbeEnvelope(
            ProcessPath: @"C:\app\helper.exe",
            IntegrityLevel: "high",
            PackageIdentityOk: true,
            PackageIdentityError: null,
            LocalProviderEnumerationOk: true,
            LocalProviderEnumerationError: null,
            LocalProviderCount: 1337);

        var roundTripped = SerializeDeserialize(original, out var json);

        AssertDiscriminator(json, "probe");
        var probe = Assert.IsType<ProbeEnvelope>(roundTripped);
        Assert.Equal(original, probe);
    }

    [Fact]
    public void ProbeEnvelope_RoundTrips_WithErrorFieldsPopulated()
    {
        var original = new ProbeEnvelope(
            ProcessPath: @"C:\app\helper.exe",
            IntegrityLevel: "high",
            PackageIdentityOk: false,
            PackageIdentityError: "InvalidOperationException: no package identity",
            LocalProviderEnumerationOk: false,
            LocalProviderEnumerationError: "EventLogException: access denied",
            LocalProviderCount: 0);

        var roundTripped = SerializeDeserialize(original, out _);

        var probe = Assert.IsType<ProbeEnvelope>(roundTripped);
        Assert.Equal(original, probe);
    }

    [Fact]
    public void ProgressEnvelope_RoundTrips_WithNullableTotalAndCurrentItem()
    {
        var original = new ProgressEnvelope(Processed: 7, Total: null, CurrentItem: null);

        var roundTripped = SerializeDeserialize(original, out var json);

        AssertDiscriminator(json, "progress");
        var prog = Assert.IsType<ProgressEnvelope>(roundTripped);
        Assert.Equal(7, prog.Processed);
        Assert.Null(prog.Total);
        Assert.Null(prog.CurrentItem);
    }

    [Fact]
    public void ProgressEnvelope_RoundTrips_WithPopulatedTotalAndCurrentItem()
    {
        var original = new ProgressEnvelope(Processed: 42, Total: 100, CurrentItem: "Microsoft-Windows-Kernel-General");

        var roundTripped = SerializeDeserialize(original, out _);

        var prog = Assert.IsType<ProgressEnvelope>(roundTripped);
        Assert.Equal(original, prog);
    }

    [Theory]
    [InlineData(DatabaseToolsOutcome.Succeeded, null)]
    [InlineData(DatabaseToolsOutcome.Cancelled, "user cancelled")]
    [InlineData(DatabaseToolsOutcome.Failed, "InvalidOperationException: boom")]
    public void ResultEnvelope_RoundTrips_PreservesOutcomeFailureSummaryAndDuration(
        DatabaseToolsOutcome outcome, string? failureSummary)
    {
        var original = new ResultEnvelope(outcome, failureSummary, DurationMs: 12345);

        var roundTripped = SerializeDeserialize(original, out var json);

        AssertDiscriminator(json, "result");
        var result = Assert.IsType<ResultEnvelope>(roundTripped);
        Assert.Equal(outcome, result.Outcome);
        Assert.Equal(failureSummary, result.FailureSummary);
        Assert.Equal(12345L, result.DurationMs);
    }

    private static void AssertDiscriminator(string json, string expected)
    {
        using var doc = JsonDocument.Parse(json);
        Assert.True(doc.RootElement.TryGetProperty("$type", out var typeProperty),
            $"Envelope JSON missing '$type' discriminator. JSON was: {json}");
        Assert.Equal(expected, typeProperty.GetString());
    }

    private static DatabaseToolsIpcEnvelope SerializeDeserialize(DatabaseToolsIpcEnvelope envelope, out string json)
    {
        json = JsonSerializer.Serialize(envelope, DatabaseToolsIpcSerializer.Options);

        var roundTripped = JsonSerializer.Deserialize<DatabaseToolsIpcEnvelope>(
            json, DatabaseToolsIpcSerializer.Options);

        Assert.NotNull(roundTripped);
        return roundTripped;
    }
}
