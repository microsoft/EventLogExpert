// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Eventing.EventResolvers;
using EventLogExpert.Eventing.Helpers;
using EventLogExpert.Eventing.Models;
using EventLogExpert.Eventing.Providers;
using EventLogExpert.Eventing.Tests.TestUtils;
using EventLogExpert.Eventing.Tests.TestUtils.Constants;
using NSubstitute;
using System.Collections.Concurrent;

namespace EventLogExpert.Eventing.Tests.EventResolvers;

public sealed class EventResolverBaseTests
{
    [Fact]
    public void Constructor_WithCacheAndLogger_ShouldCreateInstance()
    {
        // Arrange
        var cache = new EventResolverCache();
        var logger = Substitute.For<ITraceLogger>();

        // Act
        var resolver = new TestEventResolver(cache, logger);

        // Assert
        Assert.NotNull(resolver);
    }

    [Fact]
    public void Constructor_WithNoParameters_ShouldCreateInstance()
    {
        // Act
        var resolver = new TestEventResolver();

        // Assert
        Assert.NotNull(resolver);
    }

    [Fact]
    public void ResolveEvent_ConcurrentCalls_ShouldHandleThreadSafely()
    {
        // Arrange
        var resolver = new TestEventResolver();
        var exceptions = new Exception?[50];

        // Act
        Parallel.For(0, 50, i =>
            {
                try
                {
                    var eventRecord = new EventRecord
                    {
                        ProviderName = $"Provider{i % 5}",
                        Id = (ushort)(1000 + i),
                        ComputerName = $"Computer{i}",
                        LogName = Constants.ApplicationLogName
                    };

                    resolver.ResolveProviderDetails(eventRecord);
                    var displayEvent = resolver.ResolveEvent(eventRecord);
                    Assert.NotNull(displayEvent);
                }
                catch (Exception ex)
                {
                    exceptions[i] = ex;
                }
            });

        // Assert
        Assert.All(exceptions, ex => Assert.Null(ex));
    }

    [Fact]
    public void ResolveEvent_MSExchangeReplEvent_ShouldResolveCorrectly()
    {
        // Arrange
        var providerDetails = EventUtils.CreateExchangeProviderDetails();
        var resolver = new TestEventResolver([providerDetails]);
        var eventRecord = EventUtils.CreateExchangeEventRecord();

        // Act
        var displayEvent = resolver.ResolveEvent(eventRecord);

        // Assert
        Assert.NotNull(displayEvent);
        Assert.Equal(Constants.ExchangeFormattedDescription, displayEvent.Description);
        Assert.Equal("Service", displayEvent.TaskCategory);
    }

    [Fact]
    public void ResolveEvent_WithBasicEventRecord_ShouldReturnDisplayEventModel()
    {
        // Arrange
        var resolver = new TestEventResolver();
        var eventRecord = EventUtils.CreateBasicEvent();

        // Act
        var displayEvent = resolver.ResolveEvent(eventRecord);

        // Assert
        Assert.NotNull(displayEvent);
        Assert.Equal(eventRecord.Id, displayEvent.Id);
        Assert.Equal(eventRecord.ComputerName, displayEvent.ComputerName);
        Assert.Equal(eventRecord.LogName, displayEvent.LogName);
        Assert.Equal(eventRecord.ProviderName, displayEvent.Source);
        Assert.Equal(eventRecord.TimeCreated, displayEvent.TimeCreated);
        Assert.Equal(eventRecord.RecordId, displayEvent.RecordId);
        Assert.Equal(eventRecord.ProcessId, displayEvent.ProcessId);
        Assert.Equal(eventRecord.ThreadId, displayEvent.ThreadId);
        Assert.Equal("Warning", displayEvent.Level);
    }

    [Fact]
    public void ResolveEvent_WithCache_ShouldUseCachedStrings()
    {
        // Arrange
        var cache = new EventResolverCache();
        var resolver = new TestEventResolver(cache);
        var eventRecord = EventUtils.CreateBasicEvent();

        // Act
        var event1 = resolver.ResolveEvent(eventRecord);
        var event2 = resolver.ResolveEvent(eventRecord);

        // Assert
        Assert.Same(event1.ComputerName, event2.ComputerName);
        Assert.Same(event1.LogName, event2.LogName);
        Assert.Same(event1.Source, event2.Source);
    }

    [Fact]
    public void ResolveEvent_WithFormattingCharacters_ShouldCleanupDescription()
    {
        // Arrange
        var providerDetails = new ProviderDetails
        {
            ProviderName = "TestProvider",
            Events = [],
            Messages =
            [
                new MessageModel
                {
                    ShortId = 1000,
                    Text = "Line 1%nLine 2%tTabbed"
                }
            ],
            Parameters = [],
            Keywords = new Dictionary<long, string>(),
            Tasks = new Dictionary<int, string>()
        };

        var resolver = new TestEventResolver([providerDetails]);

        var eventRecord = new EventRecord
        {
            ProviderName = "TestProvider",
            Id = 1000,
            Properties = []
        };

        // Act
        var displayEvent = resolver.ResolveEvent(eventRecord);

        // Assert
        Assert.NotNull(displayEvent);
        Assert.Contains("\r\n", displayEvent.Description);
        Assert.Contains("\t", displayEvent.Description);
    }

    [Fact]
    public void ResolveEvent_WithHexPropertyType_ShouldFormatAsHex()
    {
        // Arrange
        var providerDetails = new ProviderDetails
        {
            ProviderName = "TestProvider",
            Events =
            [
                new EventModel
                {
                    Id = 1000,
                    Version = 0,
                    LogName = Constants.ApplicationLogName,
                    Description = "Error code: %1",
                    Template = "<template><data name=\"ErrorCode\" outType=\"win:HexInt32\"/></template>"
                }
            ],
            Messages = [],
            Parameters = [],
            Keywords = new Dictionary<long, string>(),
            Tasks = new Dictionary<int, string>()
        };

        var resolver = new TestEventResolver([providerDetails]);

        var eventRecord = new EventRecord
        {
            ProviderName = "TestProvider",
            Id = 1000,
            Version = 0,
            LogName = Constants.ApplicationLogName,
            Properties = [255]
        };

        // Act
        var displayEvent = resolver.ResolveEvent(eventRecord);

        // Assert
        Assert.NotNull(displayEvent);
        Assert.Contains("0xFF", displayEvent.Description);
    }

    [Fact]
    public void ResolveEvent_WithLegacyProvider_ShouldResolveDescription()
    {
        // Arrange
        var providerDetails = new ProviderDetails
        {
            ProviderName = "TestProvider",
            Events = [],
            Messages =
            [
                new MessageModel
                {
                    ShortId = 1000,
                    Text = "Legacy event: %1 with %2",
                    ProviderName = "TestProvider"
                }
            ],
            Parameters = [],
            Keywords = new Dictionary<long, string>(),
            Tasks = new Dictionary<int, string>()
        };

        var resolver = new TestEventResolver([providerDetails]);

        var eventRecord = new EventRecord
        {
            ProviderName = "TestProvider",
            Id = 1000,
            Properties = ["value1", "value2"]
        };

        // Act
        var displayEvent = resolver.ResolveEvent(eventRecord);

        // Assert
        Assert.NotNull(displayEvent);
        Assert.Equal("Legacy event: value1 with value2", displayEvent.Description);
    }

    [Fact]
    public void ResolveEvent_WithModernEventTemplate_ShouldResolveDescription()
    {
        // Arrange
        var providerDetails = new ProviderDetails
        {
            ProviderName = "TestProvider",
            Events =
            [
                new EventModel
                {
                    Id = 1000,
                    Version = 0,
                    LogName = Constants.ApplicationLogName,
                    Description = "Test event with property: %1",
                    Template = "<template><data name=\"Prop1\" outType=\"win:UnicodeString\"/></template>"
                }
            ],
            Messages = [],
            Parameters = [],
            Keywords = new Dictionary<long, string>(),
            Tasks = new Dictionary<int, string>()
        };

        var resolver = new TestEventResolver([providerDetails]);

        var eventRecord = new EventRecord
        {
            ProviderName = "TestProvider",
            Id = 1000,
            Version = 0,
            LogName = Constants.ApplicationLogName,
            Properties = ["TestValue"]
        };

        // Act
        var displayEvent = resolver.ResolveEvent(eventRecord);

        // Assert
        Assert.NotNull(displayEvent);
        Assert.Equal("Test event with property: TestValue", displayEvent.Description);
    }

    [Fact]
    public void ResolveEvent_WithMultipleLegacyMessages_ShouldReturnDefaultDescription()
    {
        // Arrange
        var providerDetails = new ProviderDetails
        {
            ProviderName = "TestProvider",
            Events = [],
            Messages =
            [
                new MessageModel { ShortId = 1000, Text = "Message 1" },
                new MessageModel { ShortId = 1000, Text = "Message 2" }
            ],
            Parameters = [],
            Keywords = new Dictionary<long, string>(),
            Tasks = new Dictionary<int, string>()
        };

        var resolver = new TestEventResolver([providerDetails]);

        var eventRecord = new EventRecord
        {
            ProviderName = "TestProvider",
            Id = 1000,
            Properties = []
        };

        // Act
        var displayEvent = resolver.ResolveEvent(eventRecord);

        // Assert
        Assert.NotNull(displayEvent);
        Assert.Contains("No matching message", displayEvent.Description);
    }

    [Fact]
    public void ResolveEvent_WithMultipleProperties_ShouldFormatAllProperties()
    {
        // Arrange
        var providerDetails = new ProviderDetails
        {
            ProviderName = "TestProvider",
            Events = [],
            Messages =
            [
                new MessageModel
                {
                    ShortId = 1000,
                    Text = "Property1: %1, Property2: %2, Property3: %3"
                }
            ],
            Parameters = [],
            Keywords = new Dictionary<long, string>(),
            Tasks = new Dictionary<int, string>()
        };

        var resolver = new TestEventResolver([providerDetails]);

        var eventRecord = new EventRecord
        {
            ProviderName = "TestProvider",
            Id = 1000,
            Properties = ["StringValue", 42, true]
        };

        // Act
        var displayEvent = resolver.ResolveEvent(eventRecord);

        // Assert
        Assert.NotNull(displayEvent);
        Assert.Contains("StringValue", displayEvent.Description);
        Assert.Contains("42", displayEvent.Description);
        Assert.Contains("True", displayEvent.Description);
    }

    [Fact]
    public void ResolveEvent_WithNoProviderDetails_ShouldReturnDefaultDescription()
    {
        // Arrange
        var resolver = new TestEventResolver();

        var eventRecord = new EventRecord
        {
            ProviderName = "NonExistentProvider",
            Id = 1000,
            ComputerName = "TestComputer",
            LogName = Constants.ApplicationLogName
        };

        // Act
        resolver.ResolveProviderDetails(eventRecord);
        var displayEvent = resolver.ResolveEvent(eventRecord);

        // Assert
        Assert.NotNull(displayEvent);
        Assert.Contains("No matching provider", displayEvent.Description);
    }

    [Fact]
    public void ResolveEvent_WithNullKeywords_ShouldReturnEmptyKeywordList()
    {
        // Arrange
        var resolver = new TestEventResolver();

        var eventRecord = new EventRecord
        {
            ProviderName = "TestProvider",
            Id = 1000,
            Keywords = null
        };

        // Act
        resolver.ResolveProviderDetails(eventRecord);
        var displayEvent = resolver.ResolveEvent(eventRecord);

        // Assert
        Assert.NotNull(displayEvent);
        Assert.Empty(displayEvent.KeywordsDisplayNames);
    }

    [Fact]
    public void ResolveEvent_WithNullXml_ShouldReturnEmptyXml()
    {
        // Arrange
        var resolver = new TestEventResolver();

        var eventRecord = new EventRecord
        {
            ProviderName = "TestProvider",
            Id = 1000,
            Xml = null
        };

        // Act
        var displayEvent = resolver.ResolveEvent(eventRecord);

        // Assert
        Assert.NotNull(displayEvent);
        Assert.Equal(string.Empty, displayEvent.Xml);
    }

    [Fact]
    public void ResolveEvent_WithParameterSubstitution_ShouldResolveParameters()
    {
        // Arrange
        var providerDetails = new ProviderDetails
        {
            ProviderName = "TestProvider",
            Events = [],
            Messages =
            [
                new MessageModel
                {
                    ShortId = 1000,
                    Text = "Status: %%1001 for user %1"
                }
            ],
            Parameters =
            [
                new MessageModel
                {
                    RawId = 1001,
                    Text = "Success"
                }
            ],
            Keywords = new Dictionary<long, string>(),
            Tasks = new Dictionary<int, string>()
        };

        var resolver = new TestEventResolver([providerDetails]);

        var eventRecord = new EventRecord
        {
            ProviderName = "TestProvider",
            Id = 1000,
            Properties = ["JohnDoe"]
        };

        // Act
        var displayEvent = resolver.ResolveEvent(eventRecord);

        // Assert
        Assert.NotNull(displayEvent);
        Assert.Contains("Success", displayEvent.Description);
        Assert.Contains("JohnDoe", displayEvent.Description);
    }

    [Fact]
    public void ResolveEvent_WithProviderKeywords_ShouldResolveKeywords()
    {
        // Arrange
        var providerDetails = new ProviderDetails
        {
            ProviderName = "TestProvider",
            Events = [],
            Messages = [],
            Parameters = [],
            Keywords = new Dictionary<long, string>
            {
                { 0x1, "CustomKeyword1" },
                { 0x2, "CustomKeyword2" }
            },
            Tasks = new Dictionary<int, string>()
        };

        var resolver = new TestEventResolver([providerDetails]);

        var eventRecord = new EventRecord
        {
            ProviderName = "TestProvider",
            Id = 1000,
            Keywords = 0x3 // Both custom keywords
        };

        // Act
        var displayEvent = resolver.ResolveEvent(eventRecord);

        // Assert
        Assert.NotNull(displayEvent);
        Assert.Contains("CustomKeyword1", displayEvent.KeywordsDisplayNames);
        Assert.Contains("CustomKeyword2", displayEvent.KeywordsDisplayNames);
    }

    [Fact]
    public void ResolveEvent_WithSeverityLevel_ShouldResolveLevelString()
    {
        // Arrange
        var resolver = new TestEventResolver();

        var testCases = new[]
        {
            (Level: (byte)0, Expected: "Information"),
            (Level: (byte)2, Expected: "Error"),
            (Level: (byte)3, Expected: "Warning"),
            (Level: (byte)4, Expected: "Information"),
            (Level: (byte)5, Expected: "5")
        };

        foreach (var testCase in testCases)
        {
            var eventRecord = new EventRecord
            {
                ProviderName = "TestProvider",
                Id = 1000,
                Level = testCase.Level
            };

            // Act
            var displayEvent = resolver.ResolveEvent(eventRecord);

            // Assert
            Assert.Equal(testCase.Expected, displayEvent.Level);
        }
    }

    [Fact]
    public void ResolveEvent_WithSinglePropertyAndNoTemplate_ShouldUsePropertyAsDescription()
    {
        // Arrange
        var providerDetails = new ProviderDetails
        {
            ProviderName = "TestProvider",
            Events = [],
            Messages = [],
            Parameters = [],
            Keywords = new Dictionary<long, string>(),
            Tasks = new Dictionary<int, string>()
        };

        var resolver = new TestEventResolver([providerDetails]);

        var eventRecord = new EventRecord
        {
            ProviderName = "TestProvider",
            Id = 1000,
            Properties = ["This is the description from property"]
        };

        // Act
        var displayEvent = resolver.ResolveEvent(eventRecord);

        // Assert
        Assert.NotNull(displayEvent);
        Assert.Equal("This is the description from property", displayEvent.Description);
    }

    [Fact]
    public void ResolveEvent_WithStandardKeywords_ShouldResolveKeywords()
    {
        // Arrange
        var resolver = new TestEventResolver();

        var eventRecord = new EventRecord
        {
            ProviderName = "TestProvider",
            Id = 1000,
            Keywords = 0x80000000000000 // Classic keyword
        };

        // Act
        resolver.ResolveProviderDetails(eventRecord);
        var displayEvent = resolver.ResolveEvent(eventRecord);

        // Assert
        Assert.NotNull(displayEvent);
        Assert.Contains("Classic", displayEvent.KeywordsDisplayNames);
    }

    [Fact]
    public void ResolveEvent_WithTaskCategory_ShouldResolveTaskName()
    {
        // Arrange
        var providerDetails = new ProviderDetails
        {
            ProviderName = "TestProvider",
            Events =
            [
                new EventModel
                {
                    Id = 1000,
                    Version = 0,
                    LogName = Constants.ApplicationLogName,
                    Task = 5,
                    Description = "Test"
                }
            ],
            Messages = [],
            Parameters = [],
            Keywords = new Dictionary<long, string>(),
            Tasks = new Dictionary<int, string>
            {
                { 5, "CustomTask" }
            }
        };

        var resolver = new TestEventResolver([providerDetails]);

        var eventRecord = new EventRecord
        {
            ProviderName = "TestProvider",
            Id = 1000,
            Version = 0,
            LogName = Constants.ApplicationLogName,
            Task = 5
        };

        // Act
        var displayEvent = resolver.ResolveEvent(eventRecord);

        // Assert
        Assert.NotNull(displayEvent);
        Assert.Equal("CustomTask", displayEvent.TaskCategory);
    }

    [Fact]
    public void ResolveEvent_WithTaskFallback_ShouldUseTaskFromMessages()
    {
        // Arrange
        var providerDetails = new ProviderDetails
        {
            ProviderName = "TestProvider",
            Events = [],
            Messages =
            [
                new MessageModel
                {
                    ShortId = 5,
                    LogLink = Constants.ApplicationLogName,
                    Text = "MessageDerivedTask"
                }
            ],
            Parameters = [],
            Keywords = new Dictionary<long, string>(),
            Tasks = new Dictionary<int, string>()
        };

        var resolver = new TestEventResolver([providerDetails]);

        var eventRecord = new EventRecord
        {
            ProviderName = "TestProvider",
            Id = 1000,
            Task = 5,
            LogName = Constants.ApplicationLogName
        };

        // Act
        var displayEvent = resolver.ResolveEvent(eventRecord);

        // Assert
        Assert.NotNull(displayEvent);
        Assert.Equal("MessageDerivedTask", displayEvent.TaskCategory);
    }

    [Fact]
    public void ResolveEvent_WithXmlProperty_ShouldPreserveXml()
    {
        // Arrange
        var resolver = new TestEventResolver();
        var xmlContent = "<Event><System><EventID>1000</EventID></System></Event>";

        var eventRecord = new EventRecord
        {
            ProviderName = "TestProvider",
            Id = 1000,
            Xml = xmlContent
        };

        // Act
        var displayEvent = resolver.ResolveEvent(eventRecord);

        // Assert
        Assert.NotNull(displayEvent);
        Assert.Equal(xmlContent, displayEvent.Xml);
    }

    [Fact]
    public void ResolveEvent_WithZeroKeywords_ShouldReturnEmptyKeywordList()
    {
        // Arrange
        var resolver = new TestEventResolver();

        var eventRecord = new EventRecord
        {
            ProviderName = "TestProvider",
            Id = 1000,
            Keywords = 0
        };

        // Act
        resolver.ResolveProviderDetails(eventRecord);
        var displayEvent = resolver.ResolveEvent(eventRecord);

        // Assert
        Assert.NotNull(displayEvent);
        Assert.Empty(displayEvent.KeywordsDisplayNames);
    }

    private class TestEventResolver : EventResolverBase, IEventResolver
    {
        public TestEventResolver(IEventResolverCache? cache = null, ITraceLogger? logger = null)
            : base(cache, logger) { }

        public TestEventResolver(
            List<ProviderDetails> providerDetailsList,
            IEventResolverCache? cache = null,
            ITraceLogger? logger = null)
            : base(cache, logger)
        {
            providerDetailsList.ForEach(p => providerDetails.TryAdd(p.ProviderName, p));
        }

        public ConcurrentDictionary<string, ProviderDetails?> GetProviderDetails() => providerDetails;

        public void ResolveProviderDetails(EventRecord eventRecord)
        {
            if (providerDetails.ContainsKey(eventRecord.ProviderName))
            {
                return;
            }

            providerDetails.TryAdd(eventRecord.ProviderName, null);
        }
    }
}
