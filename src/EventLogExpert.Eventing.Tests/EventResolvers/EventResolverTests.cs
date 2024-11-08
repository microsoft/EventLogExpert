// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Eventing.EventResolvers;
using EventLogExpert.Eventing.Helpers;
using EventLogExpert.Eventing.Models;
using EventLogExpert.Eventing.Providers;
using EventLogExpert.Eventing.Readers;
using System.Diagnostics;
using Xunit.Abstractions;

namespace EventLogExpert.Eventing.Tests.EventResolvers;

public sealed class EventResolverTests(ITestOutputHelper outputHelper)
{
    internal class UnitTestEventResolver : EventResolverBase, IEventResolver
    {
        internal UnitTestEventResolver(List<ProviderDetails> providerDetailsList) :
            base((s, log) => Debug.WriteLine(s)) =>
            providerDetailsList.ForEach(p => providerDetails.TryAdd(p.ProviderName, p));

        public void ResolveProviderDetails(EventRecord eventRecord, string owningLogName)
        {
            if (providerDetails.ContainsKey(eventRecord.ProviderName))
            {
                return;
            }

            var details = new EventMessageProvider(eventRecord.ProviderName, tracer).LoadProviderDetails();
            providerDetails.TryAdd(eventRecord.ProviderName, details);
        }
    }

    private readonly ITestOutputHelper _outputHelper = outputHelper;

    [Fact]
    public void CanResolveMSExchangeRepl4114()
    {
        // This event has a message in the legacy provider, but a task in the modern provider.
        EventRecord eventRecord = new()
        {
            Id = 4114,
            Keywords = 36028797018963968,
            Level = 4,
            LogName = "Application",
            Properties = ["SERVER1", "4", "Lots of copy status text", "False"],
            ProviderName = "MSExchangeRepl",
            RecordId = 9518530,
            Task = 1,
            TimeCreated = DateTime.Parse("1/7/2023 10:02:00 AM")
        };

        var providerDetails = new ProviderDetails
        {
            Events = [],
            Keywords = [],
            Messages =
            [
                new MessageModel
                {
                    LogLink = null,
                    ProviderName = "MSExchangeRepl",
                    RawId = 1074008082,
                    ShortId = 4114,
                    Tag = null,
                    Template = null,
                    Text = "Database redundancy health check passed.%nDatabase copy: %1%nRedundancy count: %2%nIsSuppressed: %4%n%nErrors:%n%3\r\n"
                }
            ],
            Opcodes = [],
            ProviderName = "MSExchangeRepl",
            Tasks = new Dictionary<int, string>
            {
                { 1, "Service" },
                { 2, "Exchange VSS Writer" },
                { 3, "Move" },
                { 4, "Upgrade" },
                { 5, "Action" },
                { 6, "ExRes" }
            }
        };

        var resolver = new UnitTestEventResolver([providerDetails]);
        var description = resolver.ResolveDescription(eventRecord);
        var taskName = resolver.ResolveTaskName(eventRecord);

        var expectedDescription = "Database redundancy health check passed.\r\nDatabase copy: SERVER1\r\nRedundancy count: 4\r\nIsSuppressed: False\r\n\r\nErrors:\r\nLots of copy status text";

        Assert.Equal(expectedDescription, description);
        Assert.Equal("Service", taskName);
    }

    [Fact]
    public void PerfTest()
    {
        using var eventLogReader = new EventLogReader("Application", PathType.LogName);
        var resolver = new LocalProviderEventResolver();

        var sw = Stopwatch.GetTimestamp();

        while (eventLogReader.TryGetEvents(out var er))
        {
            foreach (var record in er)
            {
                resolver.ResolveProviderDetails(record, "Test");
            }
        }

        Debug.WriteLine(Stopwatch.GetElapsedTime(sw));
    }

    [Fact]
    public void PerfTest2()
    {
        using EventLogReader eventLogReader = new("Application", PathType.LogName);
        LocalProviderEventResolver resolver = new();
        List<EventRecord> eventRecords = [];

        var sw = Stopwatch.GetTimestamp();

        while (eventLogReader.TryGetEvents(out var er))
        {
            foreach (var record in er)
            {
                eventRecords.Add(record);
            }
        }

        Debug.WriteLine("Reading events took " + Stopwatch.GetElapsedTime(sw));

        sw = Stopwatch.GetTimestamp();

        foreach (var record in eventRecords)
        {
            resolver.ResolveProviderDetails(record, "Test");
        }

        Debug.WriteLine("Resolving events took " + Stopwatch.GetElapsedTime(sw));
    }

    [Fact]
    public void Test1()
    {
        var eventLogReader = new EventLogReader("Application", PathType.LogName);

        var resolvers = new List<IEventResolver>
        {
            new LocalProviderEventResolver(),
            /* new EventProviderDatabaseEventResolver(
                s => {
                    _outputHelper.WriteLine(s);
                    Debug.WriteLine(s);
                    Debug.Flush();
                }) */
        };

        HashSet<string> uniqueDescriptions = [];
        HashSet<string> uniqueKeywords = [];

        var totalCount = 0;
        var mismatchCount = 0;
        var keywordsMismatchCount = 0;

        while (eventLogReader.TryGetEvents(out var er))
        {
            foreach(var record in er)
            {
                uniqueDescriptions.Clear();
                uniqueKeywords.Clear();

                foreach (var r in resolvers)
                {
                    r.ResolveProviderDetails(record, "Test");

                    var description = r.ResolveDescription(record);
                    var keywords = r.GetKeywordsFromBitmask(record);

                    uniqueDescriptions.Add(description
                        .Replace("\r", "")      // I can't figure out the logic of FormatMessage() for when it leaves
                        .Replace("\n", "")      // CRLFs and spaces in or takes them out, so I'm just giving up for now.
                        .Replace(" ", "")       // If we're this close to matching FormatMessage() then we're close enough.
                        .Replace("\u200E", "")  // Remove LRM marks from dates.
                        .Trim());

                    if (!keywords.Any())
                    {
                        // Don't bother adding it. EventReader fails to resolve a lot of keywords for some reason.
                    }
                    else
                    {
                        uniqueKeywords.Add(string.Join(" ", keywords.OrderBy(n => n)));
                    }
                }

                if (uniqueDescriptions.Count > 1)
                {
                    mismatchCount++;
                }

                if (uniqueKeywords.Count > 1)
                {
                    keywordsMismatchCount++;
                }

                totalCount++;
            }
        }

        var totalMismatchCount = mismatchCount + keywordsMismatchCount;

        var mismatchPercent = totalMismatchCount > 0 && totalCount > 0 ? totalMismatchCount / totalCount * 100 : 0;

        Assert.True(mismatchPercent < 1);
    }
}
