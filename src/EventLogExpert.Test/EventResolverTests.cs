// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Library.EventResolvers;
using EventLogExpert.Library.Models;
using EventLogExpert.Library.Providers;
using Moq;
using System.Diagnostics;
using System.Diagnostics.Eventing.Reader;
using System.Reflection;
using System.Security.Principal;
using Xunit.Abstractions;

namespace EventLogExpert.Test;

public class EventResolverTests
{
    internal class UnitTestEventResolver : EventResolverBase, IEventResolver
    {
        private readonly List<ProviderDetails> _providerDetailsList;

        internal UnitTestEventResolver(List<ProviderDetails> providerDetailsList) : base(s => Debug.WriteLine(s))
        {
            _providerDetailsList = providerDetailsList;
        }

        public DisplayEventModel Resolve(EventRecord eventRecord)
        {
            return ResolveFromProviderDetails(eventRecord, _providerDetailsList[0]);
        }
    }

    private readonly ITestOutputHelper _outputHelper;

    public EventResolverTests(ITestOutputHelper outputHelper)
    {
        _outputHelper = outputHelper;
    }

    [Fact]
    public void CanResolveMSExchangeRepl4114()
    {
        // This event has a message in the legacy provider, but a task in the modern provider.

        List<string> properties = new() { "SERVER1", "4", "Lots of copy status text", "False" };
        var propList = new List<EventProperty>();
        var constructors = typeof(EventProperty).GetConstructors(BindingFlags.NonPublic | BindingFlags.Instance);
        foreach (var p in properties)
        {
            var eventProperty = (EventProperty)constructors[0].Invoke(new[] { p });
            propList.Add(eventProperty);
        }

        var eventRecord = new Mock<EventRecord>();
        eventRecord.Setup(e => e.Id).Returns(4114);
        eventRecord.Setup(e => e.Keywords).Returns(36028797018963968);
        eventRecord.Setup(e => e.Level).Returns(4);
        eventRecord.Setup(e => e.LogName).Returns("Application");
        eventRecord.Setup(e => e.Opcode).Returns<short?>(null);
        eventRecord.Setup(e => e.Properties).Returns(propList);
        eventRecord.Setup(e => e.ProviderId).Returns<Guid?>(null);
        eventRecord.Setup(e => e.ProviderName).Returns("MSExchangeRepl");
        eventRecord.Setup(e => e.Qualifiers).Returns(16388);
        eventRecord.Setup(e => e.RecordId).Returns(9518530);
        eventRecord.Setup(e => e.RelatedActivityId).Returns<Guid?>(null);
        eventRecord.Setup(e => e.Task).Returns(1);
        eventRecord.Setup(e => e.TimeCreated).Returns(DateTime.Parse("1/7/2023 10:02:00 AM"));
        eventRecord.Setup(e => e.UserId).Returns<SecurityIdentifier>(null);
        eventRecord.Setup(e => e.Version).Returns<byte?>(null);

        var providerDetails = new ProviderDetails
        {
            Events = new List<EventModel>(),
            Keywords = new Dictionary<long, string>(),
            Messages = new List<MessageModel>
            {
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
            },
            Opcodes = new Dictionary<int, string>(),
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

        var resolver = new UnitTestEventResolver(new List<ProviderDetails> { providerDetails });
        var result = resolver.Resolve(eventRecord.Object);

        var expectedDescription = "Database redundancy health check passed.\r\nDatabase copy: SERVER1\r\nRedundancy count: 4\r\nIsSuppressed: False\r\n\r\nErrors:\r\nLots of copy status text";
        Assert.Equal(expectedDescription, result.Description);
        Assert.Equal("Service", result.TaskDisplayName);
    }

    public void Test1()
    {
        var eventLogReader = new EventLogReader("Application", PathType.LogName);

        var resolvers = new List<IEventResolver>()
        {
            new LocalProviderEventResolver(),
            new EventProviderDatabaseEventResolver(null, s => { _outputHelper.WriteLine(s); Debug.WriteLine(s); Debug.Flush(); })
        };

        EventRecord er;
        HashSet<string> uniqueDescriptions = new();

        var totalCount = 0;
        var mismatchCount = 0;
        var mismatches = new List<List<string>>();
        while (null != (er = eventLogReader.ReadEvent()))
        {
            uniqueDescriptions.Clear();

            foreach (var r in resolvers)
            {
                uniqueDescriptions.Add(r.Resolve(er).Description
                    .Replace("\r", "")  // I can't figure out the logic of FormatMessage() for when it leaves
                    .Replace("\n", "")  // CRLFs and spaces in or takes them out, so I'm just giving up for now.
                    .Replace(" ", "")   // If we're this close to matching FormatMessage() then we're close enough.
                    .Replace("\u200E", "") // Remove LRM marks from dates.
                    .Trim());
            }

            if (uniqueDescriptions.Count > 1)
            {
                mismatchCount++;
                mismatches.Add(uniqueDescriptions.ToList());
            }

            totalCount++;
        }

        foreach (var resolver in resolvers)
        {
            (resolver as IDisposable)?.Dispose();
        }

        Assert.Equal(0, mismatchCount);
    }
}
