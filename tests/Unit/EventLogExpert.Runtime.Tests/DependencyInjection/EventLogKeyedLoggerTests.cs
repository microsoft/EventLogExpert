// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Logging.Abstractions;
using EventLogExpert.Logging.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace EventLogExpert.Runtime.Tests.DependencyInjection;

// Fluxor registers effect classes as plain container services and resolves them through the provider, so a
// [FromKeyedServices] logger parameter is honored by the container the same way an ordinary service is. This locks
// that the EventLog effects receive the Offline-style categorized logger rather than the default App logger.
public sealed class EventLogKeyedLoggerTests
{
    [Fact]
    public void KeyedEventLogLogger_IsInjected_IntoAFromKeyedServicesConstructorParameter()
    {
        var recording = new CategoryRecordingSink();
        var services = new ServiceCollection();
        services.AddSingleton<ILogSourceFactory>(new LogSourceFactory([recording]));
        services.AddKeyedSingleton<ITraceLogger>(LogCategories.EventLog, static (sp, _) =>
            sp.GetRequiredService<ILogSourceFactory>().ForCategory(LogCategories.EventLog));
        services.AddScoped<KeyedLoggerConsumer>();

        using ServiceProvider provider = services.BuildServiceProvider();
        var consumer = provider.GetRequiredService<KeyedLoggerConsumer>();
        consumer.Logger.Warning($"probe");

        Assert.Equal(LogCategories.EventLog, Assert.Single(recording.Received).Category);
    }

    private sealed class CategoryRecordingSink : ILogSink
    {
        public List<LogRecord> Received { get; } = [];

        public void Emit(LogRecord record) => Received.Add(record);

        public LogLevel MinimumLevelFor(string category) => LogLevel.Trace;
    }

    private sealed class KeyedLoggerConsumer([FromKeyedServices(LogCategories.EventLog)] ITraceLogger logger)
    {
        public ITraceLogger Logger { get; } = logger;
    }
}
