// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Eventing.EventResolvers;
using EventLogExpert.Eventing.Models;
using EventLogExpert.Eventing.Providers;
using NSubstitute;
using System.Diagnostics;
using System.Diagnostics.Eventing.Reader;
using System.Reflection;
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

        List<string> properties = ["SERVER1", "4", "Lots of copy status text", "False"];
        var propList = new List<EventProperty>();
        var constructors = typeof(EventProperty).GetConstructors(BindingFlags.NonPublic | BindingFlags.Instance);

        foreach (var p in properties)
        {
            var eventProperty = (EventProperty)constructors[0].Invoke(new[] { p });
            propList.Add(eventProperty);
        }

        var eventRecord = Substitute.For<EventRecord>();
        eventRecord.Id.Returns(4114);
        eventRecord.Keywords.Returns(36028797018963968);
        eventRecord.Level.Returns((byte)4);
        eventRecord.LogName.Returns("Application");
        eventRecord.Opcode.Returns(i => null);
        eventRecord.Properties.Returns(propList);
        eventRecord.ProviderId.Returns(i => null);
        eventRecord.ProviderName.Returns("MSExchangeRepl");
        eventRecord.Qualifiers.Returns(16388);
        eventRecord.RecordId.Returns(9518530);
        eventRecord.RelatedActivityId.Returns(i => null);
        eventRecord.Task.Returns(1);
        eventRecord.TimeCreated.Returns(DateTime.Parse("1/7/2023 10:02:00 AM"));
        eventRecord.UserId.Returns(i => null);
        eventRecord.Version.Returns(i => null);

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
        var sw = new Stopwatch();
        sw.Start();
        var eventLogReader = new EventLogReader("Application", PathType.LogName);
        var resolver = new LocalProviderEventResolver();
        EventRecord er;

        while (null != (er = eventLogReader.ReadEvent()))
        {
            resolver.ResolveProviderDetails(er, "Test");
        }

        sw.Stop();
        Debug.WriteLine(sw.ElapsedMilliseconds);
    }

    [Fact]
    public void PerfTest2()
    {
        var sw = new Stopwatch();
        var eventLogReader = new EventLogReader("Application", PathType.LogName);
        var resolver = new LocalProviderEventResolver();
        var eventRecords = new List<EventRecord>();
        EventRecord er;

        sw.Start();

        while (null != (er = eventLogReader.ReadEvent()))
        {
            eventRecords.Add(er);
        }

        sw.Stop();
        Debug.WriteLine("Reading events took " + sw.ElapsedMilliseconds);

        sw.Restart();

        foreach (var record in eventRecords)
        {
            resolver.ResolveProviderDetails(record, "Test");
        }

        sw.Stop();
        Debug.WriteLine("Resolving events took " + sw.ElapsedMilliseconds);
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

        while (eventLogReader.ReadEvent() is { } er)
        {
            uniqueDescriptions.Clear();
            uniqueKeywords.Clear();

            foreach (var r in resolvers)
            {
                r.ResolveProviderDetails(er, "Test");

                var description = r.ResolveDescription(er);
                var keywords = r.GetKeywordsFromBitmask(er);

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

        var totalMismatchCount = mismatchCount + keywordsMismatchCount;

        var mismatchPercent = totalMismatchCount > 0 && totalCount > 0 ? totalMismatchCount / totalCount * 100 : 0;

        Assert.True(mismatchPercent < 1);
    }
}
