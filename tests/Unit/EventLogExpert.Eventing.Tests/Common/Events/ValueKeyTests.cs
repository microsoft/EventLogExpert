// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Eventing.Common.Channels;
using EventLogExpert.Eventing.Common.Events;

namespace EventLogExpert.Eventing.Tests.Common.Events;

public sealed class ValueKeyTests
{
    private static readonly DateTime s_sampleTime = new(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void Equality_DistinctRecordIds_AreNotEqual()
    {
        var first = new ValueKey(1, s_sampleTime, "live", "Security");
        var second = new ValueKey(2, s_sampleTime, "live", "Security");

        Assert.NotEqual(first, second);
    }

    [Fact]
    public void TryCreate_NullEvent_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => ValueKey.TryCreate(null!, out _));
    }

    [Fact]
    public void TryCreate_NullRecordId_ReturnsFalse()
    {
        var resolvedEvent = new ResolvedEvent("live", LogPathType.Channel) { RecordId = null, LogName = "Security" };

        Assert.False(ValueKey.TryCreate(resolvedEvent, out ValueKey key));
        Assert.Equal(default, key);
    }

    [Fact]
    public void TryCreate_WithRecordId_ReturnsKeyFromEventFields()
    {
        var resolvedEvent = new ResolvedEvent("live", LogPathType.Channel)
        {
            RecordId = 42,
            TimeCreated = s_sampleTime,
            LogName = "Security"
        };

        Assert.True(ValueKey.TryCreate(resolvedEvent, out ValueKey key));
        Assert.Equal(new ValueKey(42, s_sampleTime, "live", "Security"), key);
    }
}
