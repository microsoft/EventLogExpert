// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Eventing.Common.Channels;
using EventLogExpert.Eventing.Common.EventLogs;
using EventLogExpert.Filtering.TestUtils;
using EventLogExpert.Runtime.EventLog;
using EventLogExpert.Runtime.Tests.TestUtils.Constants;
using Fluxor;
using Fluxor.DependencyInjection;
using Microsoft.Extensions.DependencyInjection;
using System.Collections.Immutable;

namespace EventLogExpert.Runtime.Tests.EventLog;

public sealed class EventLogReducerRegistrationTests
{
    [Fact]
    public void RegisteredReducers_AddThenConsumeEvents_UpdateNewEventBuffer()
    {
        using ServiceProvider provider = new ServiceCollection()
            .AddFluxor(options => options.RegisterStateLibrary().WithLifetime(StoreLifetime.Singleton))
            .BuildServiceProvider();

        var feature = provider.GetRequiredService<IFeature<EventLogState>>();

        feature.RestoreState(feature.State with
        {
            ContinuouslyUpdate = false,
            OpenLogs = ImmutableDictionary<string, OpenLogInfo>.Empty
                .Add(Constants.LogNameTestLog, new OpenLogInfo(EventLogId.Create(), LogPathType.Channel))
        });

        var first = FilterEventBuilder.CreateTestEvent(100, owningLog: Constants.LogNameTestLog);
        var second = FilterEventBuilder.CreateTestEvent(200, owningLog: Constants.LogNameTestLog);

        feature.ReceiveDispatchNotificationFromStore(new AddEventAction(first));
        feature.ReceiveDispatchNotificationFromStore(new AddEventAction(second));

        Assert.Equal(2, feature.State.NewEventBuffer.Count);
        Assert.Same(second, feature.State.NewEventBuffer[0]);
        Assert.Same(first, feature.State.NewEventBuffer[1]);

        feature.ReceiveDispatchNotificationFromStore(new NewEventBufferConsumedAction([first, second]));

        Assert.Empty(feature.State.NewEventBuffer);
    }
}
