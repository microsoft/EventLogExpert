// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Runtime.FilterPane;
using EventLogExpert.Runtime.Settings;
using Fluxor;
using NSubstitute;

namespace EventLogExpert.Runtime.Tests.FilterPane;

public sealed class DateFilterTimeZoneSynchronizerTests
{
    [Fact]
    public void Dispose_WhenZoneChangesLater_DoesNotDispatch()
    {
        var previousZone = TimeZoneInfo.CreateCustomTimeZone("test+2", TimeSpan.FromHours(2), "test+2", "test+2");
        var newZone = TimeZoneInfo.CreateCustomTimeZone("test-5", TimeSpan.FromHours(-5), "test-5", "test-5");
        var settings = Substitute.For<ISettingsService>();
        settings.TimeZoneInfo.Returns(previousZone);
        var dispatcher = Substitute.For<IDispatcher>();
        var synchronizer = new DateFilterTimeZoneSynchronizer(settings, dispatcher);

        synchronizer.Dispose();
        settings.TimeZoneChanged?.Invoke(settings, newZone);

        dispatcher.DidNotReceive().Dispatch(Arg.Any<ReinterpretFilterDateRangeAction>());
    }

    [Fact]
    public void TimeZoneChanged_DispatchesReinterpretActionWithPreviousAndNewZones()
    {
        var previousZone = TimeZoneInfo.CreateCustomTimeZone("test+2", TimeSpan.FromHours(2), "test+2", "test+2");
        var newZone = TimeZoneInfo.CreateCustomTimeZone("test-5", TimeSpan.FromHours(-5), "test-5", "test-5");
        var settings = Substitute.For<ISettingsService>();
        settings.TimeZoneInfo.Returns(previousZone);
        var dispatcher = Substitute.For<IDispatcher>();
        using var synchronizer = new DateFilterTimeZoneSynchronizer(settings, dispatcher);

        settings.TimeZoneChanged?.Invoke(settings, newZone);

        dispatcher.Received(1).Dispatch(Arg.Is<ReinterpretFilterDateRangeAction>(action =>
            action != null && action.PreviousZone.Equals(previousZone) && action.NewZone.Equals(newZone)));
    }

    [Fact]
    public void TimeZoneChanged_WhenZoneIsUnchanged_DoesNotDispatch()
    {
        var zone = TimeZoneInfo.CreateCustomTimeZone("test+2", TimeSpan.FromHours(2), "test+2", "test+2");
        var settings = Substitute.For<ISettingsService>();
        settings.TimeZoneInfo.Returns(zone);
        var dispatcher = Substitute.For<IDispatcher>();
        using var synchronizer = new DateFilterTimeZoneSynchronizer(settings, dispatcher);

        settings.TimeZoneChanged?.Invoke(settings, zone);

        dispatcher.DidNotReceive().Dispatch(Arg.Any<ReinterpretFilterDateRangeAction>());
    }
}
