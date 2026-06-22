// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Eventing.Interop;
using EventLogExpert.Eventing.Readers;
using EventLogExpert.Eventing.Resolvers;
using EventLogExpert.Eventing.TestUtils;
using EventLogExpert.Eventing.TestUtils.Constants;
using EventLogExpert.Logging.Abstractions;
using EventLogExpert.Provider.Resolution;
using NSubstitute;
using System.Collections.Concurrent;
using System.Security.Principal;

namespace EventLogExpert.Eventing.Tests.Resolvers;

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
    public void FormatDescription_WithPercentPercentParam_ShouldFallbackToFormatMessage()
    {
        // Arrange - %%1053 is Win32 error 1053 (service timeout)
        // When provider has no parameter files, should fallback to FormatMessage
        var (details, eventRecord) = EventUtils.CreateModernEvent(
            "Service failed: %%1053",
            """<template><data name="Service" inType="win:UnicodeString"/></template>""",
            ["TestService"]);

        // Clear parameters to simulate MTA provider without parameter files
        details.Parameters = [];

        var resolver = new TestEventResolver([details]);

        // Act
        var displayEvent = resolver.ResolveEvent(eventRecord);

        // Assert - %%1053 should be resolved to the actual error message
        Assert.NotNull(displayEvent);
        Assert.DoesNotContain("%%1053", displayEvent.Description);
        // The resolved message should not be a raw hex fallback
        Assert.DoesNotContain("0x00000", displayEvent.Description);
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

                    resolver.LoadProviderDetails(eventRecord);
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
    public void ResolveEvent_WithAmbiguousPrimaryAndSupplementalDescription_ShouldUseSupplementalParameters()
    {
        // Arrange - regression: when the description is selected from supplemental, %%n
        // parameter substitutions must resolve against supplemental's parameter table, not
        // the primary's. Primary has populated parameters that would otherwise short-circuit
        // the lookup and produce the wrong substitution.
        var primaryDetails = new ProviderDetails
        {
            ProviderName = Constants.TestProviderName,
            Events = [],
            Messages =
            [
                new MessageModel { ShortId = 800, RawId = 0x40000800, Text = Constants.PrimaryShortA, LogLink = null },
                new MessageModel { ShortId = 800, RawId = 0x40000800, Text = Constants.PrimaryShortB, LogLink = null }
            ],
            Parameters =
            [
                new MessageModel { ShortId = 1, RawId = 1, Text = Constants.PrimaryParameterValue, LogLink = null }
            ],
            Keywords = new Dictionary<long, string>(),
            Opcodes = new Dictionary<int, string>(),
            Tasks = new Dictionary<int, string>()
        };

        var supplementalDetails = new ProviderDetails
        {
            ProviderName = Constants.TestProviderName,
            Events =
            [
                new EventModel
                {
                    Id = 800,
                    Version = 0,
                    LogName = Constants.ApplicationLogName,
                    Description = Constants.ResolvedParameterTemplate,
                    Keywords = [],
                    Template = Constants.EmptyTemplate
                }
            ],
            Messages = [],
            Parameters =
            [
                new MessageModel { ShortId = 1, RawId = 1, Text = Constants.SupplementalParameterValue, LogLink = null }
            ],
            Keywords = new Dictionary<long, string>(),
            Opcodes = new Dictionary<int, string>(),
            Tasks = new Dictionary<int, string>()
        };

        var resolver = new SupplementalTestResolver([primaryDetails], supplementalDetails);

        var eventRecord = new EventRecord
        {
            ProviderName = Constants.TestProviderName,
            Id = 800,
            Version = 0,
            Level = 4,
            LogName = Constants.ApplicationLogName
        };

        // Act
        var displayEvent = resolver.ResolveEvent(eventRecord);

        // Assert
        Assert.NotNull(displayEvent);
        Assert.Contains(Constants.SupplementalParameterValue, displayEvent.Description);
        Assert.DoesNotContain(Constants.PrimaryParameterValue, displayEvent.Description);
    }

    [Fact]
    public void ResolveEvent_WithAmbiguousPrimaryAndSupplementalModernEventTask_ShouldUseModernEventTaskForLookup()
    {
        // Arrange - regression: when primary has ambiguous legacy and supplemental has a modern
        // event whose Task differs from EventRecord.Task, ResolveTaskName must look up the
        // supplemental Tasks table using the supplemental EventModel.Task (not just eventRecord.Task).
        var primaryDetails = new ProviderDetails
        {
            ProviderName = Constants.TestProviderName,
            Events = [],
            Messages =
            [
                new MessageModel { ShortId = 700, RawId = 0x40000700, Text = Constants.PrimaryShortA, LogLink = null },
                new MessageModel { ShortId = 700, RawId = 0x40000700, Text = Constants.PrimaryShortB, LogLink = null }
            ],
            Parameters = [],
            Keywords = new Dictionary<long, string>(),
            Opcodes = new Dictionary<int, string>(),
            Tasks = new Dictionary<int, string>()
        };

        var supplementalDetails = new ProviderDetails
        {
            ProviderName = Constants.TestProviderName,
            Events =
            [
                new EventModel
                {
                    Id = 700,
                    Version = 0,
                    Task = 42,
                    LogName = Constants.ApplicationLogName,
                    Description = "Supplemental modern description",
                    Keywords = [],
                    Template = Constants.EmptyTemplate
                }
            ],
            Messages = [],
            Parameters = [],
            Keywords = new Dictionary<long, string>(),
            Opcodes = new Dictionary<int, string>(),
            Tasks = new Dictionary<int, string> { { 42, Constants.ModernEventTask } }
        };

        var resolver = new SupplementalTestResolver([primaryDetails], supplementalDetails);

        var eventRecord = new EventRecord
        {
            ProviderName = Constants.TestProviderName,
            Id = 700,
            Version = 0,
            // EventRecord.Task is null - the only way TaskCategory can resolve is via the
            // supplemental modern event's Task = 42 mapping to Constants.ModernEventTask.
            Level = 4,
            LogName = Constants.ApplicationLogName
        };

        // Act
        var displayEvent = resolver.ResolveEvent(eventRecord);

        // Assert
        Assert.NotNull(displayEvent);
        Assert.Equal(Constants.ModernEventTask, displayEvent.TaskCategory);
    }

    [Fact]
    public void ResolveEvent_WithAmbiguousPrimaryLegacyAndSupplemental_ShouldAlsoUseSupplementalForKeywordsAndTask()
    {
        // Arrange - regression for the cross-issue case: when primary has ambiguous legacy
        // messages AND supplemental is loaded for the description fallback, supplemental's
        // task and keyword metadata must also be visible to the task / keyword resolution path.
        var primaryDetails = new ProviderDetails
        {
            ProviderName = Constants.TestProviderName,
            Events = [],
            Messages =
            [
                new MessageModel { ShortId = 600, RawId = 0x40000600, Text = Constants.PrimaryMessageA, LogLink = null },
                new MessageModel { ShortId = 600, RawId = 0x40000600, Text = Constants.PrimaryMessageB, LogLink = null }
            ],
            Parameters = [],
            Keywords = new Dictionary<long, string>(),
            Opcodes = new Dictionary<int, string>(),
            Tasks = new Dictionary<int, string>()
        };

        var supplementalDetails = new ProviderDetails
        {
            ProviderName = Constants.TestProviderName,
            Events =
            [
                new EventModel
                {
                    Id = 600,
                    Version = 0,
                    Task = 9,
                    LogName = Constants.ApplicationLogName,
                    Description = Constants.SupplementalDescription,
                    Keywords = [],
                    Template = Constants.EmptyTemplate
                }
            ],
            Messages = [],
            Parameters = [],
            Keywords = new Dictionary<long, string> { { 0x4, Constants.SupplementalKeyword } },
            Opcodes = new Dictionary<int, string>(),
            Tasks = new Dictionary<int, string> { { 9, Constants.SupplementalTask } }
        };

        var resolver = new SupplementalTestResolver([primaryDetails], supplementalDetails);

        var eventRecord = new EventRecord
        {
            ProviderName = Constants.TestProviderName,
            Id = 600,
            Version = 0,
            Task = 9,
            Level = 4,
            LogName = Constants.ApplicationLogName,
            Keywords = 0x4
        };

        // Act
        var displayEvent = resolver.ResolveEvent(eventRecord);

        // Assert
        Assert.NotNull(displayEvent);
        Assert.Contains(Constants.SupplementalDescription, displayEvent.Description);
        Assert.Equal(Constants.SupplementalTask, displayEvent.TaskCategory);
        Assert.Contains(Constants.SupplementalKeyword, displayEvent.Keywords);
    }

    [Fact]
    public void ResolveEvent_WithAmbiguousPrimaryLegacyAndSupplementalMatch_ShouldUseSupplementalDescription()
    {
        // Arrange - primary has 2 legacy messages for the same event ID with no LogLink
        // and identical severity (so disambiguation fails). Supplemental has a matching
        // modern event. Should fall back to supplemental's description.
        var primaryDetails = new ProviderDetails
        {
            ProviderName = Constants.TestProviderName,
            Events = [],
            Messages =
            [
                new MessageModel { ShortId = 500, RawId = 0x40000500, Text = Constants.PrimaryMessageA, LogLink = null },
                new MessageModel { ShortId = 500, RawId = 0x40000500, Text = Constants.PrimaryMessageB, LogLink = null }
            ],
            Parameters = [],
            Keywords = new Dictionary<long, string>(),
            Opcodes = new Dictionary<int, string>(),
            Tasks = new Dictionary<int, string>()
        };

        var supplementalDetails = new ProviderDetails
        {
            ProviderName = Constants.TestProviderName,
            Events =
            [
                new EventModel
                {
                    Id = 500,
                    Version = 0,
                    LogName = Constants.ApplicationLogName,
                    Description = "Supplemental modern: %1",
                    Keywords = [],
                    Template = "<template><data name=\"Val\" inType=\"win:UnicodeString\" outType=\"xs:string\"/></template>"
                }
            ],
            Messages = [],
            Parameters = [],
            Keywords = new Dictionary<long, string>(),
            Opcodes = new Dictionary<int, string>(),
            Tasks = new Dictionary<int, string>()
        };

        var resolver = new SupplementalTestResolver([primaryDetails], supplementalDetails);

        var eventRecord = new EventRecord
        {
            ProviderName = Constants.TestProviderName,
            Id = 500,
            Version = 0,
            Level = 4,
            LogName = Constants.ApplicationLogName,
            Properties = ["resolved"]
        };

        // Act
        var displayEvent = resolver.ResolveEvent(eventRecord);

        // Assert
        Assert.NotNull(displayEvent);
        Assert.Contains("Supplemental modern: resolved", displayEvent.Description);
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
    public void ResolveEvent_WithBitMap_ShouldDecodeFlagsToCommaJoinedNames()
    {
        // Arrange - Kernel-Boot VsmPolicy: 643 = 512 | 128 | 2 | 1 decodes to the joined flag names.
        var (details, eventRecord) = EventUtils.CreateModernEvent(
            "VSM policies: %1",
            """
                <template>
                  <data name="VsmPolicy" inType="win:UInt32" outType="xs:unsignedInt" map="VsmPolicyMap"/>
                </template>
                """,
            [643u]);

        details.Maps = new Dictionary<string, ValueMapDefinition>
        {
            ["VsmPolicyMap"] = new ValueMapDefinition(
                isBitMap: true,
                entries:
                [
                    new ValueMapEntry(1, "VBS Enabled"),
                    new ValueMapEntry(2, "VSM Required"),
                    new ValueMapEntry(128, "Hvci"),
                    new ValueMapEntry(512, "Boot Chain Signer Soft Enforced")
                ])
        };

        var resolver = new TestEventResolver([details]);

        // Act
        var displayEvent = resolver.ResolveEvent(eventRecord);

        // Assert
        Assert.NotNull(displayEvent);
        Assert.Contains(
            "VSM policies: VBS Enabled,VSM Required,Hvci,Boot Chain Signer Soft Enforced",
            displayEvent.Description);
    }

    [Fact]
    public void ResolveEvent_WithBoolean_ShouldFormatLowercase()
    {
        var (trueDetails, trueRecord) = EventUtils.CreateModernEvent(
            "Flag: %1",
            """<template><data name="Flag" inType="win:Boolean"/></template>""",
            [true]);

        var (falseDetails, falseRecord) = EventUtils.CreateModernEvent(
            "Flag: %1",
            """<template><data name="Flag" inType="win:Boolean"/></template>""",
            [false]);

        var trueEvent = new TestEventResolver([trueDetails]).ResolveEvent(trueRecord);
        var falseEvent = new TestEventResolver([falseDetails]).ResolveEvent(falseRecord);

        Assert.NotNull(trueEvent);
        Assert.NotNull(falseEvent);
        Assert.Contains("Flag: true", trueEvent.Description);
        Assert.Contains("Flag: false", falseEvent.Description);
    }

    [Fact]
    public void ResolveEvent_WithByteArrayAndHexOutType_ShouldStillFormatAsHexString()
    {
        // A byte[] is always rendered via Convert.ToHexString; a hex outType must not change that.
        var (details, eventRecord) = EventUtils.CreateModernEvent(
            "Data: %1",
            """<template><data name="Data" inType="win:Binary" outType="win:HexInt32"/></template>""",
            [new byte[] { 0xAB, 0xCD }]);

        var resolver = new TestEventResolver([details]);

        var displayEvent = resolver.ResolveEvent(eventRecord);

        Assert.NotNull(displayEvent);
        Assert.Contains("ABCD", displayEvent.Description);
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
    public void ResolveEvent_WithCaseInsensitiveProviderName_ShouldReuseCachedProvider()
    {
        // Arrange - provider added with one casing, event uses different casing
        var providerDetails = new ProviderDetails
        {
            ProviderName = "Microsoft-Windows-Test",
            Events = [],
            Messages =
            [
                new MessageModel
                {
                    ProviderName = "Microsoft-Windows-Test",
                    ShortId = 100,
                    Text = "Test message: %1"
                }
            ],
            Parameters = [],
            Keywords = new Dictionary<long, string>(),
            Tasks = new Dictionary<int, string>()
        };

        var resolver = new TestEventResolver([providerDetails]);

        // Event uses different casing than the cached provider
        var eventRecord = new EventRecord
        {
            ProviderName = "microsoft-windows-test",
            Id = 100,
            Properties = ["value1"]
        };

        // Act
        var displayEvent = resolver.ResolveEvent(eventRecord);

        // Assert - should find the cached provider despite case difference
        Assert.NotNull(displayEvent);
        Assert.Contains("Test message: value1", displayEvent.Description);
    }

    [Fact]
    public void ResolveEvent_WithClassicEventIdMatchingWin32ErrorCode_ShouldNotPrependSystemMessage()
    {
        // Arrange - regression guard: classic events whose low 16-bit EventId happens to
        // collide with a Win32 error code (e.g. EventId 2 == ERROR_FILE_NOT_FOUND) must
        // NOT have that error text injected as the description. The system-message
        // fallback is intentionally restricted to EventId 0 to avoid such false matches.
        var providerDetails = new ProviderDetails
        {
            ProviderName = Constants.TestProviderName,
            Events = [],
            Messages = [],
            Parameters = [],
            Keywords = new Dictionary<long, string>(),
            Tasks = new Dictionary<int, string>()
        };

        var resolver = new TestEventResolver([providerDetails]);

        var eventRecord = new EventRecord
        {
            ProviderName = Constants.TestProviderName,
            Id = 2,
            Keywords = unchecked((long)0x80000000000000UL),
            Properties = ["payload-a", "payload-b"]
        };

        // Act
        var displayEvent = resolver.ResolveEvent(eventRecord);

        // Assert
        Assert.NotNull(displayEvent);
        // Compare against the locale's system-message text for error 2 so the test
        // catches false matches on non-English Windows too.
        var win32ErrorTwoMessage = NativeMethods.FormatSystemMessage(2);
        Assert.NotNull(win32ErrorTwoMessage);
        Assert.False(string.IsNullOrWhiteSpace(win32ErrorTwoMessage), "FormatSystemMessage(2) must return text on Windows.");
        Assert.DoesNotContain(win32ErrorTwoMessage, displayEvent.Description);
        // Property tail is still rendered
        Assert.Contains("The following information was included with the event:", displayEvent.Description);
        Assert.Contains("payload-a", displayEvent.Description);
        Assert.Contains("payload-b", displayEvent.Description);
    }

    [Fact]
    public void ResolveEvent_WithClassicEventIdZeroAndNoProviderMetadata_ShouldPrependSystemMessage()
    {
        // Arrange - reproduces the AsusUpdateCheck scenario: classic-keyword event with
        // EventId 0 and no provider metadata at all. mmc shows the Win32 system message
        // for ERROR_SUCCESS ("The operation completed successfully.") followed by the
        // EventData payload. We replicate that.
        var providerDetails = new ProviderDetails
        {
            ProviderName = Constants.TestProviderName,
            Events = [],
            Messages = [],
            Parameters = [],
            Keywords = new Dictionary<long, string>(),
            Tasks = new Dictionary<int, string>()
        };

        var resolver = new TestEventResolver([providerDetails]);

        var eventRecord = new EventRecord
        {
            ProviderName = Constants.TestProviderName,
            Id = 0,
            Keywords = unchecked((long)0x80000000000000UL),
            Properties = ["AsusUpdateCheck", "CServiceControl in OnStop"]
        };

        // Act
        var displayEvent = resolver.ResolveEvent(eventRecord);

        // Assert
        Assert.NotNull(displayEvent);
        // Compare against the locale's system-message text rather than a hard-coded English
        // string so the test passes on non-English Windows.
        var expectedSystemMessage = NativeMethods.FormatSystemMessage(0);
        Assert.NotNull(expectedSystemMessage);
        Assert.False(string.IsNullOrWhiteSpace(expectedSystemMessage), "FormatSystemMessage(0) must return text on Windows.");
        Assert.StartsWith(expectedSystemMessage, displayEvent.Description);
        Assert.Contains("The following information was included with the event:", displayEvent.Description);
        Assert.Contains("AsusUpdateCheck", displayEvent.Description);
        Assert.Contains("CServiceControl in OnStop", displayEvent.Description);
        Assert.DoesNotContain("No matching provider", displayEvent.Description);
    }

    [Fact]
    public void ResolveEvent_WithDataElementsMissingOutType_ShouldResolveDescription()
    {
        // Arrange - template has 3 data elements, only first has outType
        var (details, eventRecord) = EventUtils.CreateModernEvent(
            "Status: %1, Joined: %2, Licensed: %3",
            """
                <template>
                  <data name="Status" inType="win:UnicodeString" outType="xs:string"/>
                  <data name="Joined" inType="win:Boolean"/>
                  <data name="Licensed" inType="win:UInt32"/>
                </template>
                """,
            ["Enabled", "True", "1"]);

        var resolver = new TestEventResolver([details]);

        // Act
        var displayEvent = resolver.ResolveEvent(eventRecord);

        // Assert
        Assert.NotNull(displayEvent);
        Assert.Contains("Enabled", displayEvent.Description);
        Assert.Contains("True", displayEvent.Description);
        Assert.Contains("1", displayEvent.Description);
        Assert.DoesNotContain("Failed to resolve", displayEvent.Description);
    }

    [Fact]
    public void ResolveEvent_WithDataSourceTag_ShouldNotMatchAsDataElement()
    {
        // Arrange - template has a <dataSource> tag that should not be counted as <data>
        var (details, eventRecord) = EventUtils.CreateModernEvent(
            "Value: %1",
            "<template><dataSource name=\"src\"/><data name=\"Value\" inType=\"win:UnicodeString\" outType=\"xs:string\"/></template>",
            ["TestValue"]);

        var resolver = new TestEventResolver([details]);

        // Act
        var displayEvent = resolver.ResolveEvent(eventRecord);

        // Assert
        Assert.NotNull(displayEvent);
        Assert.Contains("TestValue", displayEvent.Description);
        Assert.DoesNotContain("Failed to resolve", displayEvent.Description);
    }

    [Fact]
    public void ResolveEvent_WithEmptyPrimaryAndNonMatchingSupplemental_ShouldReturnNoMatchingMessage()
    {
        // Arrange - primary provider returned an empty ProviderDetails (provider not installed
        // on this machine) but a supplemental source has metadata for the provider, just no
        // match for THIS event ID. We should report "no matching message" since a provider
        // IS available, not "no matching provider".
        var primaryDetails = new ProviderDetails
        {
            ProviderName = Constants.TestProviderName,
            Events = [],
            Messages = [],
            Parameters = [],
            Keywords = new Dictionary<long, string>(),
            Opcodes = new Dictionary<int, string>(),
            Tasks = new Dictionary<int, string>()
        };

        var supplementalDetails = new ProviderDetails
        {
            ProviderName = Constants.TestProviderName,
            Events =
            [
                new EventModel
                {
                    Id = 100,
                    Version = 0,
                    LogName = Constants.ApplicationLogName,
                    Description = "Some unrelated event: %1",
                    Keywords = [],
                    Template = "<template><data name=\"Val\" inType=\"win:UnicodeString\" outType=\"xs:string\"/></template>"
                }
            ],
            Messages = [],
            Parameters = [],
            Keywords = new Dictionary<long, string>(),
            Opcodes = new Dictionary<int, string>(),
            Tasks = new Dictionary<int, string>()
        };

        var resolver = new SupplementalTestResolver([primaryDetails], supplementalDetails);

        var eventRecord = new EventRecord
        {
            ProviderName = Constants.TestProviderName,
            Id = 9999,
            Properties = ["a", "b", "c"]
        };

        // Act
        var displayEvent = resolver.ResolveEvent(eventRecord);

        // Assert
        Assert.NotNull(displayEvent);
        Assert.Contains("No matching message", displayEvent.Description);
        Assert.DoesNotContain("No matching provider", displayEvent.Description);
        Assert.DoesNotContain("Failed to resolve", displayEvent.Description);
    }

    [Fact]
    public void ResolveEvent_WithEmptyPrimaryAndNoSupplemental_ShouldReturnEventDataTail()
    {
        // Arrange - empty primary AND no supplemental should fall through to the
        // no-metadata fallback. With multiple properties present, that surfaces them
        // under the "included with the event" header (mmc-style payload dump).
        var primaryDetails = new ProviderDetails
        {
            ProviderName = Constants.TestProviderName,
            Events = [],
            Messages = [],
            Parameters = [],
            Keywords = new Dictionary<long, string>(),
            Opcodes = new Dictionary<int, string>(),
            Tasks = new Dictionary<int, string>()
        };

        var resolver = new SupplementalTestResolver([primaryDetails], null);

        var eventRecord = new EventRecord
        {
            ProviderName = Constants.TestProviderName,
            Id = 9999,
            Properties = ["a", "b", "c"]
        };

        // Act
        var displayEvent = resolver.ResolveEvent(eventRecord);

        // Assert
        Assert.NotNull(displayEvent);
        Assert.Contains("The following information was included with the event:", displayEvent.Description);
        Assert.Contains("a", displayEvent.Description);
        Assert.Contains("b", displayEvent.Description);
        Assert.Contains("c", displayEvent.Description);
        Assert.DoesNotContain("No matching message", displayEvent.Description);
    }

    [Fact]
    public void ResolveEvent_WithEmptyPrimaryAndSupplementalModernDescription_ShouldUsePrimaryParametersFallback()
    {
        // Arrange - regression: when descriptionDetails is promoted to supplemental
        // (count==0 path) and supplemental's description has %%n substitutions but supplemental
        // has NO parameters, the substitution must fall back to the primary parameter table.
        var primaryDetails = new ProviderDetails
        {
            ProviderName = Constants.TestProviderName,
            Events = [],
            Messages = [],
            Parameters =
            [
                new MessageModel { ShortId = 1, RawId = 1, Text = Constants.PrimaryFallbackParameter, LogLink = null }
            ],
            Keywords = new Dictionary<long, string>(),
            Opcodes = new Dictionary<int, string>(),
            Tasks = new Dictionary<int, string>()
        };

        var supplementalDetails = new ProviderDetails
        {
            ProviderName = Constants.TestProviderName,
            Events =
            [
                new EventModel
                {
                    Id = 900,
                    Version = 0,
                    LogName = Constants.ApplicationLogName,
                    Description = Constants.ResolvedParameterTemplate,
                    Keywords = [],
                    Template = Constants.EmptyTemplate
                }
            ],
            Messages = [],
            Parameters = [],
            Keywords = new Dictionary<long, string>(),
            Opcodes = new Dictionary<int, string>(),
            Tasks = new Dictionary<int, string>()
        };

        var resolver = new SupplementalTestResolver([primaryDetails], supplementalDetails);

        var eventRecord = new EventRecord
        {
            ProviderName = Constants.TestProviderName,
            Id = 900,
            Version = 0,
            Level = 4,
            LogName = Constants.ApplicationLogName
        };

        // Act
        var displayEvent = resolver.ResolveEvent(eventRecord);

        // Assert
        Assert.NotNull(displayEvent);
        Assert.Contains(Constants.PrimaryFallbackParameter, displayEvent.Description);
    }

    [Fact]
    public void ResolveEvent_WithEmptyPrimaryAndSupplementalTask_ShouldUseSupplementalTaskName()
    {
        // Arrange - primary is empty; supplemental has matching event AND task name.
        // Task category should come from supplemental.
        var primaryDetails = new ProviderDetails
        {
            ProviderName = Constants.TestProviderName,
            Events = [],
            Messages = [],
            Parameters = [],
            Keywords = new Dictionary<long, string>(),
            Opcodes = new Dictionary<int, string>(),
            Tasks = new Dictionary<int, string>()
        };

        var supplementalDetails = new ProviderDetails
        {
            ProviderName = Constants.TestProviderName,
            Events =
            [
                new EventModel
                {
                    Id = 100,
                    Version = 0,
                    Task = 7,
                    LogName = Constants.ApplicationLogName,
                    Description = "Event 100",
                    Keywords = [],
                    Template = Constants.EmptyTemplate
                }
            ],
            Messages = [],
            Parameters = [],
            Keywords = new Dictionary<long, string>(),
            Opcodes = new Dictionary<int, string>(),
            Tasks = new Dictionary<int, string> { { 7, Constants.SupplementalTask } }
        };

        var resolver = new SupplementalTestResolver([primaryDetails], supplementalDetails);

        var eventRecord = new EventRecord
        {
            ProviderName = Constants.TestProviderName,
            Id = 100,
            Task = 7,
            LogName = Constants.ApplicationLogName
        };

        // Act
        var displayEvent = resolver.ResolveEvent(eventRecord);

        // Assert
        Assert.NotNull(displayEvent);
        Assert.Equal(Constants.SupplementalTask, displayEvent.TaskCategory);
    }

    [Fact]
    public void ResolveEvent_WithEmptyProviderDetailsAndMultipleProperties_ShouldReturnEventDataTail()
    {
        // Arrange - reproduces the AppXDeploymentServer/Operational scenario where
        // EventMessageProvider returns an empty-but-non-null ProviderDetails because
        // no ETW publisher and no legacy registry source exist for the provider name.
        // The event has multiple properties so the no-metadata fallback should surface
        // the EventData payload using mmc's "included with the event" wording. EventId
        // 2562 is intentionally NOT a Win32 error code, so no system message is prepended.
        var providerDetails = new ProviderDetails
        {
            ProviderName = Constants.TestProviderName,
            Events = [],
            Messages = [],
            Parameters = [],
            Keywords = new Dictionary<long, string>(),
            Tasks = new Dictionary<int, string>()
        };

        var resolver = new TestEventResolver([providerDetails]);

        var eventRecord = new EventRecord
        {
            ProviderName = Constants.TestProviderName,
            Id = 2562,
            Keywords = unchecked((long)0x80000000000000UL),
            Properties = ["MSIXDeployment", "windows.applicationData", "DeleteMachineFolder", "Removed folder X"]
        };

        // Act
        var displayEvent = resolver.ResolveEvent(eventRecord);

        // Assert
        Assert.NotNull(displayEvent);
        Assert.Contains("The following information was included with the event:", displayEvent.Description);
        Assert.Contains("MSIXDeployment", displayEvent.Description);
        Assert.Contains("windows.applicationData", displayEvent.Description);
        Assert.Contains("DeleteMachineFolder", displayEvent.Description);
        Assert.Contains("Removed folder X", displayEvent.Description);
        Assert.DoesNotContain("No matching provider", displayEvent.Description);
        Assert.DoesNotContain("Failed to resolve", displayEvent.Description);
        // EventId 2562 must NOT false-match a Win32 error code via the system message table
        Assert.DoesNotContain("The operation completed successfully", displayEvent.Description);
    }

    [Fact]
    public void ResolveEvent_WithEmptyProviderDetailsAndNoProperties_ShouldReturnDefaultDescription()
    {
        // Arrange - no metadata, no EventData payload, no classic+Id0 system message hint
        // → fall back to the original "No matching provider" placeholder so the user knows
        // there is nothing further to display.
        var providerDetails = new ProviderDetails
        {
            ProviderName = Constants.TestProviderName,
            Events = [],
            Messages = [],
            Parameters = [],
            Keywords = new Dictionary<long, string>(),
            Tasks = new Dictionary<int, string>()
        };

        var resolver = new TestEventResolver([providerDetails]);

        var eventRecord = new EventRecord
        {
            ProviderName = Constants.TestProviderName,
            Id = 2,
            Keywords = unchecked((long)0x80000000000000UL),
            Properties = []
        };

        // Act
        var displayEvent = resolver.ResolveEvent(eventRecord);

        // Assert
        Assert.NotNull(displayEvent);
        Assert.Contains("No matching provider", displayEvent.Description);
    }

    [Fact]
    public void ResolveEvent_WithEmptyProviderDetailsAndSingleProperty_ShouldDumpProperty()
    {
        // Arrange - even when no provider metadata exists, a single-property event should still
        // surface that property as the description (some providers emit a self-contained string).
        var providerDetails = new ProviderDetails
        {
            ProviderName = Constants.TestProviderName,
            Events = [],
            Messages = [],
            Parameters = [],
            Keywords = new Dictionary<long, string>(),
            Tasks = new Dictionary<int, string>()
        };

        var resolver = new TestEventResolver([providerDetails]);

        var eventRecord = new EventRecord
        {
            ProviderName = Constants.TestProviderName,
            Id = 1000,
            Properties = ["The entire description is this single string."]
        };

        // Act
        var displayEvent = resolver.ResolveEvent(eventRecord);

        // Assert
        Assert.NotNull(displayEvent);
        Assert.Equal("The entire description is this single string.", displayEvent.Description);
    }

    [Fact]
    public void ResolveEvent_WithEvtHandleProperty_RendersViaToStringWithoutThrowing()
    {
        // EvtHandle is one of the ConvertVariant outputs; it is a reference rendered via .ToString().
        var (details, eventRecord) = EventUtils.CreateModernEvent(
            "Handle: %1",
            """<template><data name="Handle" inType="win:EvtHandle"/></template>""",
            [EventProperty.FromReference(new EvtHandle(IntPtr.Zero, ownsHandle: false))]);

        var resolver = new TestEventResolver([details]);

        var displayEvent = resolver.ResolveEvent(eventRecord);

        Assert.NotNull(displayEvent);
        Assert.DoesNotContain("Failed to resolve", displayEvent.Description);
        Assert.Contains(nameof(EvtHandle), displayEvent.Description);
    }

    [Fact]
    public void ResolveEvent_WithFormattingCharacters_ShouldCleanupDescription()
    {
        // Arrange
        var providerDetails = new ProviderDetails
        {
            ProviderName = Constants.TestProviderName,
            Events = [],
            Messages =
            [
                new MessageModel
                {
                    ProviderName = Constants.TestProviderName,
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
            ProviderName = Constants.TestProviderName,
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
    public void ResolveEvent_WithGuid_ShouldFormatWithBraces()
    {
        var (details, eventRecord) = EventUtils.CreateModernEvent(
            "Id: %1",
            """<template><data name="Id" inType="win:GUID"/></template>""",
            [new Guid("12345678-1234-1234-1234-123456789abc")]);

        var resolver = new TestEventResolver([details]);

        var displayEvent = resolver.ResolveEvent(eventRecord);

        Assert.NotNull(displayEvent);
        Assert.Contains("{12345678-1234-1234-1234-123456789abc}", displayEvent.Description);
    }

    [Fact]
    public void ResolveEvent_WithGuidProperty_ShouldRenderWithBraces()
    {
        // Arrange - Windows renders GUID insertions wrapped in braces.
        var volumeId = new Guid("4cff5b8e-e659-4f3a-8b2f-1a2b3c4d5e6f");
        var (details, eventRecord) = EventUtils.CreateModernEvent(
            "Volume Id: %1",
            """
                <template>
                  <data name="VolumeId" inType="win:GUID" outType="xs:GUID"/>
                </template>
                """,
            [volumeId]);

        var resolver = new TestEventResolver([details]);

        // Act
        var displayEvent = resolver.ResolveEvent(eventRecord);

        // Assert
        Assert.NotNull(displayEvent);
        Assert.Contains("Volume Id: {4cff5b8e-e659-4f3a-8b2f-1a2b3c4d5e6f}", displayEvent.Description);
    }

    [Fact]
    public void ResolveEvent_WithHexOutTypeAfterMissingOutType_ShouldFormatCorrectly()
    {
        // Arrange - first data element has no outType, second has HexInt32
        // Verifies outType alignment isn't shifted when earlier elements lack outType
        var (details, eventRecord) = EventUtils.CreateModernEvent(
            "Name: %1, Code: %2",
            """
                <template>
                  <data name="Name" inType="win:UnicodeString"/>
                  <data name="Code" inType="win:UInt32" outType="win:HexInt32"/>
                </template>
                """,
            ["TestApp", 255u]);

        var resolver = new TestEventResolver([details]);

        // Act
        var displayEvent = resolver.ResolveEvent(eventRecord);

        // Assert
        Assert.NotNull(displayEvent);
        Assert.Contains("TestApp", displayEvent.Description);
        Assert.Contains("0xFF", displayEvent.Description); // Should be hex-formatted
        Assert.DoesNotContain("Failed to resolve", displayEvent.Description);
    }

    [Fact]
    public void ResolveEvent_WithHexOutTypeOnStringProperty_ShouldNotAddHexPrefix()
    {
        // Arrange - Even if outType says hex, string properties should not get "0x" prefix
        var (details, eventRecord) = EventUtils.CreateModernEvent(
            "Value: %1",
            """<template><data name="Value" inType="win:UnicodeString" outType="win:HexInt32"/></template>""",
            ["SomeStringValue"]);

        var resolver = new TestEventResolver([details]);

        // Act
        var displayEvent = resolver.ResolveEvent(eventRecord);

        // Assert
        Assert.NotNull(displayEvent);
        Assert.Contains("SomeStringValue", displayEvent.Description);
        Assert.DoesNotContain("0xSomeStringValue", displayEvent.Description);
    }

    [Fact]
    public void ResolveEvent_WithHexPropertyType_ShouldFormatAsHex()
    {
        // Arrange
        var (details, eventRecord) = EventUtils.CreateModernEvent(
            "Error code: %1",
            "<template><data name=\"ErrorCode\" outType=\"win:HexInt32\"/></template>",
            [255]);

        var resolver = new TestEventResolver([details]);

        // Act
        var displayEvent = resolver.ResolveEvent(eventRecord);

        // Assert
        Assert.NotNull(displayEvent);
        Assert.Contains("0xFF", displayEvent.Description);
    }

    [Fact]
    public void ResolveEvent_WithHiddenLengthField_ShouldAlignOutTypesCorrectly()
    {
        // Arrange - template has a hidden length field followed by a hex-formatted property.
        // The outType for the property AFTER the hidden field must align correctly.
        var (details, _) = EventUtils.CreateModernEvent(
            "Name: %1, Data: %2, Code: %3",
            """
                <template>
                  <data name="Name" inType="win:UnicodeString" outType="xs:string"/>
                  <data name="__binLength" inType="win:UInt32" outType="xs:unsignedInt"/>
                  <data name="BinaryData" inType="win:Binary" outType="xs:hexBinary" length="__binLength"/>
                  <data name="ErrorCode" inType="win:UInt32" outType="win:HexInt32"/>
                </template>
                """,
            ["TestName", new byte[] { 0xAB, 0xCD }, 255u]);

        var resolver = new TestEventResolver([details]);

        // 3 visible properties: Name, BinaryData, ErrorCode (__binLength is hidden)
        var eventRecord = new EventRecord
        {
            ProviderName = Constants.TestProviderName,
            Id = 1000,
            Version = 0,
            LogName = Constants.ApplicationLogName,
            Properties = ["TestName", new byte[] { 0xAB, 0xCD }, 255u]
        };

        // Act
        var displayEvent = resolver.ResolveEvent(eventRecord);

        // Assert - ErrorCode (property[2]) should be formatted as hex (0xFF),
        // not as plain "255" which would happen if outType alignment was shifted
        Assert.NotNull(displayEvent);
        Assert.Contains("0xFF", displayEvent.Description);
        Assert.Contains("TestName", displayEvent.Description);
    }

    [Fact]
    public void ResolveEvent_WithHighBitUInt32_ShouldFormatAsUnsignedDecimal()
    {
        var (details, eventRecord) = EventUtils.CreateModernEvent(
            "Value: %1",
            """<template><data name="Value" inType="win:UInt32"/></template>""",
            [uint.MaxValue]);

        var resolver = new TestEventResolver([details]);

        var displayEvent = resolver.ResolveEvent(eventRecord);

        Assert.NotNull(displayEvent);
        Assert.Contains("4294967295", displayEvent.Description);
    }

    [Fact]
    public void ResolveEvent_WithHighBitUInt64_ShouldFormatAsUnsignedDecimal()
    {
        // Regression guard: ulong.MaxValue is packed as -1L in the property bit field; the no-outType
        // decimal path must render the unsigned value, never "-1".
        var (details, eventRecord) = EventUtils.CreateModernEvent(
            "Value: %1",
            """<template><data name="Value" inType="win:UInt64"/></template>""",
            [ulong.MaxValue]);

        var resolver = new TestEventResolver([details]);

        var displayEvent = resolver.ResolveEvent(eventRecord);

        Assert.NotNull(displayEvent);
        Assert.Contains("18446744073709551615", displayEvent.Description);
    }

    [Fact]
    public void ResolveEvent_WithHResultOutType_ShouldResolveDynamically()
    {
        // Arrange - win:HResult with a common error code should resolve dynamically
        var (details, eventRecord) = EventUtils.CreateModernEvent(
            "Error: %1",
            """<template><data name="Error" inType="win:Int32" outType="win:HResult"/></template>""",
            [unchecked((int)0x80070005)]);

        var resolver = new TestEventResolver([details]);

        // Act
        var displayEvent = resolver.ResolveEvent(eventRecord);

        // Assert - 0x80070005 is ACCESS_DENIED; resolved message should not contain the raw hex code
        Assert.NotNull(displayEvent);
        Assert.DoesNotContain("0x80070005", displayEvent.Description);
        Assert.DoesNotContain("0x00000005", displayEvent.Description);
    }

    [Fact]
    public void ResolveEvent_WithLargeTemplate_ShouldNotOverflowStack()
    {
        // Arrange - very large description template that would overflow stackalloc
        string largeDescription = "Event: " + new string('A', 5000) + " %1";
        string largeTemplate =
            "<template><data name=\"Val\" inType=\"win:UnicodeString\" outType=\"xs:string\"/></template>";

        var providerDetails = new ProviderDetails
        {
            ProviderName = Constants.TestProviderName,
            Events =
            [
                new EventModel
                {
                    Id = 1000,
                    Version = 0,
                    Keywords = [],
                    LogName = Constants.ApplicationLogName,
                    Description = largeDescription,
                    Template = largeTemplate
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
            ProviderName = Constants.TestProviderName,
            Id = 1000,
            Version = 0,
            LogName = Constants.ApplicationLogName,
            Properties = ["TestValue"]
        };

        // Act - should not throw StackOverflowException
        var displayEvent = resolver.ResolveEvent(eventRecord);

        // Assert
        Assert.NotNull(displayEvent);
        Assert.Contains("TestValue", displayEvent.Description);
    }

    [Fact]
    public void ResolveEvent_WithLegacyProvider_ShouldResolveDescription()
    {
        // Arrange
        var providerDetails = new ProviderDetails
        {
            ProviderName = Constants.TestProviderName,
            Events = [],
            Messages =
            [
                new MessageModel
                {
                    ShortId = 1000,
                    Text = "Legacy event: %1 with %2",
                    ProviderName = Constants.TestProviderName
                }
            ],
            Parameters = [],
            Keywords = new Dictionary<long, string>(),
            Tasks = new Dictionary<int, string>()
        };

        var resolver = new TestEventResolver([providerDetails]);

        var eventRecord = new EventRecord
        {
            ProviderName = Constants.TestProviderName,
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
    public void ResolveEvent_WithLegacyShortIdAbove32767_ShouldResolveLegacyMessage()
    {
        // Arrange - ShortId wraps negative for IDs > 32767 due to (short) cast
        // EventRecord.Id = 49156 (ushort), ShortId = -16380 (short)
        short shortId = unchecked((short)49156);

        var providerDetails = new ProviderDetails
        {
            ProviderName = Constants.TestProviderName,
            Events = [],
            Messages =
            [
                new MessageModel
                {
                    ProviderName = Constants.TestProviderName,
                    ShortId = shortId,
                    RawId = 49156,
                    Text = "High ID event: %1"
                }
            ],
            Parameters = [],
            Keywords = new Dictionary<long, string>(),
            Tasks = new Dictionary<int, string>()
        };

        var resolver = new TestEventResolver([providerDetails]);

        var eventRecord = new EventRecord
        {
            ProviderName = Constants.TestProviderName,
            Id = 49156,
            Properties = ["test_value"]
        };

        // Act
        var displayEvent = resolver.ResolveEvent(eventRecord);

        // Assert
        Assert.NotNull(displayEvent);
        Assert.Contains("High ID event: test_value", displayEvent.Description);
    }

    [Fact]
    public void ResolveEvent_WithLengthPrefixedBinaryData_ShouldMatchTemplate()
    {
        // Arrange - template has 4 data elements but __binLength is consumed internally
        // by Windows as a length provider, so the event surfaces 3 properties (param1,
        // param2, and the binary blob).
        var (details, _) = EventUtils.CreateModernEvent(
            "Param1: %1, Param2: %2",
            """
                <template xmlns="http://schemas.microsoft.com/win/2004/08/events">
                  <data name="param1" inType="win:UnicodeString" outType="xs:string"/>
                  <data name="param2" inType="win:UnicodeString" outType="xs:string"/>
                  <data name="__binLength" inType="win:UInt32" outType="xs:unsignedInt"/>
                  <data name="BinaryData" inType="win:Binary" outType="xs:hexBinary" length="__binLength"/>
                </template>
                """,
            ["Value1", "Value2", new byte[] { 0x01, 0x02 }]);

        var resolver = new TestEventResolver([details]);

        // Event has 3 properties: param1, param2, and the binary data blob
        var eventRecord = new EventRecord
        {
            ProviderName = Constants.TestProviderName,
            Id = 1000,
            Version = 0,
            LogName = Constants.ApplicationLogName,
            Properties = ["Value1", "Value2", new byte[] { 0x01, 0x02 }]
        };

        // Act
        var displayEvent = resolver.ResolveEvent(eventRecord);

        // Assert - should match despite template having 4 data nodes for 3 properties
        Assert.NotNull(displayEvent);
        Assert.Equal("Param1: Value1, Param2: Value2", displayEvent.Description);
    }

    [Fact]
    public void ResolveEvent_WithLengthProviderFieldsExcludedByEvtRender_ShouldAlignOutTypesCorrectly()
    {
        // Arrange - Same template but EvtRender excluded the length fields (4 properties)
        var (details, eventRecord) = EventUtils.CreateModernEvent(
            "The driver %3 failed.\r\nDevice: %1\r\nStatus: %2",
            """
                <template>
                  <data name="DriverNameLength" inType="win:UInt32"/>
                  <data name="DriverName" inType="win:UnicodeString" length="DriverNameLength"/>
                  <data name="Status" inType="win:UInt32" outType="win:HexInt32"/>
                  <data name="FailureNameLength" inType="win:UInt32"/>
                  <data name="FailureName" inType="win:UnicodeString" length="FailureNameLength"/>
                  <data name="Version" inType="win:UInt32"/>
                </template>
                """,
            ["ROOT\\DEVICE\\0000", 0xC0000365, "\\Driver\\WUDFRd", (uint)0]);

        var resolver = new TestEventResolver([details]);

        // Act
        var displayEvent = resolver.ResolveEvent(eventRecord);

        // Assert - visible outTypes align: DriverName=string, Status=hex, FailureName=string, Version=uint
        Assert.NotNull(displayEvent);
        Assert.Contains("0xC0000365", displayEvent.Description);
        Assert.DoesNotContain("0xROOT", displayEvent.Description);
    }

    [Fact]
    public void ResolveEvent_WithLengthProviderFieldsIncludedByEvtRender_ShouldAlignOutTypesCorrectly()
    {
        // Arrange - Template has length-provider fields (DriverNameLength, FailureNameLength)
        // but EvtRender includes ALL 6 properties, so outTypes must use the full array.
        var (details, eventRecord) = EventUtils.CreateModernEvent(
            "The driver %5 failed to load.\r\nDevice: %2\r\nStatus: %3",
            """
                <template>
                  <data name="DriverNameLength" inType="win:UInt32"/>
                  <data name="DriverName" inType="win:UnicodeString" length="DriverNameLength"/>
                  <data name="Status" inType="win:UInt32" outType="win:HexInt32"/>
                  <data name="FailureNameLength" inType="win:UInt32"/>
                  <data name="FailureName" inType="win:UnicodeString" length="FailureNameLength"/>
                  <data name="Version" inType="win:UInt32"/>
                </template>
                """,
            [(uint)40, "ROOT\\DEVICE\\0000", 0xC0000365, (uint)14, "\\Driver\\WUDFRd", (uint)0]);

        var resolver = new TestEventResolver([details]);

        // Act
        var displayEvent = resolver.ResolveEvent(eventRecord);

        // Assert - Status should be hex, device name should NOT have "0x" prefix
        Assert.NotNull(displayEvent);
        Assert.Contains("ROOT\\DEVICE\\0000", displayEvent.Description);
        Assert.DoesNotContain("0xROOT", displayEvent.Description);
        Assert.Contains("0xC0000365", displayEvent.Description);
    }

    [Fact]
    public void ResolveEvent_WithLogNameMismatch_ShouldFallbackToUniqueIdVersionMatch()
    {
        // Arrange - event logged to "Application" but manifest defines it under a diagnostic channel
        var providerDetails = new ProviderDetails
        {
            ProviderName = Constants.TestProviderName,
            Events =
            [
                new EventModel
                {
                    Id = 6001,
                    Version = 1,
                    LogName = "Microsoft-Windows-Winlogon/Diagnostic",
                    Description = "Winlogon started: %1",
                    Keywords = [],
                    Template = "<template><data name=\"Result\" inType=\"win:UInt32\" outType=\"xs:unsignedInt\"/></template>"
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
            ProviderName = Constants.TestProviderName,
            Id = 6001,
            Version = 1,
            LogName = "Application",
            Properties = [0u]
        };

        // Act
        var displayEvent = resolver.ResolveEvent(eventRecord);

        // Assert - should match via LogName-ignored fallback since only one candidate
        Assert.NotNull(displayEvent);
        Assert.Contains("Winlogon started:", displayEvent.Description);
    }

    [Fact]
    public void ResolveEvent_WithLogNameMismatch_ShouldNotMatchWhenAmbiguous()
    {
        // Arrange - two events with same Id+Version but different LogChannelNames, neither matching
        var providerDetails = new ProviderDetails
        {
            ProviderName = Constants.TestProviderName,
            Events =
            [
                new EventModel
                {
                    Id = 6001,
                    Version = 1,
                    LogName = "Channel-A",
                    Description = "From Channel A: %1",
                    Keywords = [],
                    Template = "<template><data name=\"Val\" inType=\"win:UInt32\" outType=\"xs:unsignedInt\"/></template>"
                },
                new EventModel
                {
                    Id = 6001,
                    Version = 1,
                    LogName = "Channel-B",
                    Description = "From Channel B: %1",
                    Keywords = [],
                    Template = "<template><data name=\"Val\" inType=\"win:UInt32\" outType=\"xs:unsignedInt\"/></template>"
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
            ProviderName = Constants.TestProviderName,
            Id = 6001,
            Version = 1,
            LogName = "Application",
            Properties = [0u]
        };

        // Act
        var displayEvent = resolver.ResolveEvent(eventRecord);

        // Assert - should NOT match because it's ambiguous (2 candidates)
        Assert.NotNull(displayEvent);
        Assert.DoesNotContain("Channel A", displayEvent.Description);
        Assert.DoesNotContain("Channel B", displayEvent.Description);
    }

    [Fact]
    public void ResolveEvent_WithMapAttributeButNoMapDefinition_ShouldFallBackToRawValue()
    {
        // Arrange - the template references a map, but no definition is loaded (DB/MTA provider path).
        var (details, eventRecord) = EventUtils.CreateModernEvent(
            "Bus Type: %1",
            """
                <template>
                  <data name="BusType" inType="win:UInt32" outType="xs:unsignedInt" map="BusTypeMap"/>
                </template>
                """,
            [10u]);

        var resolver = new TestEventResolver([details]);

        // Act
        var displayEvent = resolver.ResolveEvent(eventRecord);

        // Assert
        Assert.NotNull(displayEvent);
        Assert.Contains("Bus Type: 10", displayEvent.Description);
    }

    [Fact]
    public void ResolveEvent_WithMismatchedPropertyCount_ShouldNotMatchWrongTemplate()
    {
        // Arrange - template has 5 data elements but event only has 3 properties
        var (details, _) = EventUtils.CreateModernEvent(
            "A: %1, B: %2, C: %3, D: %4, E: %5",
            """
                <template>
                  <data name="A" inType="win:UnicodeString" outType="xs:string"/>
                  <data name="B" inType="win:UnicodeString" outType="xs:string"/>
                  <data name="C" inType="win:UnicodeString" outType="xs:string"/>
                  <data name="D" inType="win:UInt32"/>
                  <data name="E" inType="win:UInt32"/>
                </template>
                """,
            ["val1", "val2", "val3", "val4", "val5"]);

        var resolver = new TestEventResolver([details]);

        // Event has only 3 properties, but template expects 5
        var eventRecord = new EventRecord
        {
            ProviderName = Constants.TestProviderName,
            Id = 1000,
            Version = 0,
            LogName = Constants.ApplicationLogName,
            Properties = ["val1", "val2", "val3"]
        };

        // Act
        var displayEvent = resolver.ResolveEvent(eventRecord);

        // Assert - should NOT use the 5-property template's description
        Assert.NotNull(displayEvent);
        Assert.DoesNotContain("A: val1", displayEvent.Description);
    }

    [Fact]
    public void ResolveEvent_WithMismatchedPropertyCount_ShouldSkipOutTypeFormatting()
    {
        // Arrange - Template has 3 elements but event has 2 properties (version mismatch).
        // OutType formatting should be disabled to avoid misalignment.
        var (details, eventRecord) = EventUtils.CreateModernEvent(
            "Status: %1, Code: %2",
            """
                <template>
                  <data name="Name" inType="win:UnicodeString"/>
                  <data name="Status" inType="win:UInt32" outType="win:HexInt32"/>
                  <data name="Code" inType="win:UInt32"/>
                </template>
                """,
            [(uint)42, (uint)100]);

        var resolver = new TestEventResolver([details]);

        // Act
        var displayEvent = resolver.ResolveEvent(eventRecord);

        // Assert - No outType formatting applied (count mismatch), values shown as default
        Assert.NotNull(displayEvent);
        Assert.Contains("42", displayEvent.Description);
        Assert.DoesNotContain("0x2A", displayEvent.Description);
    }

    [Fact]
    public void ResolveEvent_WithModernEventEmptyTemplateAndNoProperties_ShouldUseModernDescription()
    {
        // Arrange - mimics Microsoft-Windows-WMI 5615 v2: manifest defines no <template>
        // (so EventModel.Template is the empty string) and the logged event has
        // <EventData></EventData> (zero properties). The static description must be
        // returned via the modern path.
        var providerDetails = new ProviderDetails
        {
            ProviderName = Constants.TestProviderName,
            Events =
            [
                new EventModel
                {
                    Id = 5615,
                    Version = 2,
                    LogName = Constants.ApplicationLogName,
                    Description = "WMI Service started successfully",
                    Keywords = [],
                    Template = string.Empty
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
            ProviderName = Constants.TestProviderName,
            Id = 5615,
            Version = 2,
            LogName = Constants.ApplicationLogName,
            Properties = []
        };

        // Act
        var displayEvent = resolver.ResolveEvent(eventRecord);

        // Assert
        Assert.NotNull(displayEvent);
        Assert.Equal("WMI Service started successfully", displayEvent.Description);
    }

    [Fact]
    public void ResolveEvent_WithModernEventEmptyTemplateAndProperties_ShouldNotUseModernDescription()
    {
        // Arrange - defensive: an empty template means the manifest defines no parameters.
        // If the event nonetheless has properties, the strict exact-match path must NOT
        // pretend the template matches; the resolver should fall through to its other
        // fallbacks (here, the single-property fallback).
        var providerDetails = new ProviderDetails
        {
            ProviderName = Constants.TestProviderName,
            Events =
            [
                new EventModel
                {
                    Id = 5615,
                    Version = 2,
                    LogName = Constants.ApplicationLogName,
                    Description = "Static manifest description",
                    Keywords = [],
                    Template = string.Empty
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
            ProviderName = Constants.TestProviderName,
            Id = 5615,
            Version = 2,
            LogName = Constants.ApplicationLogName,
            Properties = ["unexpected-payload"]
        };

        // Act
        var displayEvent = resolver.ResolveEvent(eventRecord);

        // Assert
        Assert.NotNull(displayEvent);
        Assert.Equal("unexpected-payload", displayEvent.Description);
    }

    [Fact]
    public void ResolveEvent_WithModernEventNullTemplateAndNoProperties_ShouldUseModernDescription()
    {
        // Arrange - defensive: EventModel.Template is declared as nullable, so a null
        // value should behave the same as the empty-string case above.
        var providerDetails = new ProviderDetails
        {
            ProviderName = Constants.TestProviderName,
            Events =
            [
                new EventModel
                {
                    Id = 5615,
                    Version = 2,
                    LogName = Constants.ApplicationLogName,
                    Description = "Modern static event description",
                    Keywords = [],
                    Template = null
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
            ProviderName = Constants.TestProviderName,
            Id = 5615,
            Version = 2,
            LogName = Constants.ApplicationLogName,
            Properties = []
        };

        // Act
        var displayEvent = resolver.ResolveEvent(eventRecord);

        // Assert
        Assert.NotNull(displayEvent);
        Assert.Equal("Modern static event description", displayEvent.Description);
    }

    [Fact]
    public void ResolveEvent_WithModernEventTemplate_ShouldResolveDescription()
    {
        // Arrange
        var (details, eventRecord) = EventUtils.CreateModernEvent(
            "Test event with property: %1",
            "<template><data name=\"Prop1\" outType=\"win:UnicodeString\"/></template>",
            ["TestValue"]);

        var resolver = new TestEventResolver([details]);

        // Act
        var displayEvent = resolver.ResolveEvent(eventRecord);

        // Assert
        Assert.NotNull(displayEvent);
        Assert.Equal("Test event with property: TestValue", displayEvent.Description);
    }

    [Fact]
    public void ResolveEvent_WithModernManifestFullRawIdAndConflictingLowIds_ShouldDisambiguateByQualifier()
    {
        // Arrange - two manifest entries share low-16 EventID 0x6001 (24577) but differ in
        // their high 16 bits. The broadened template gate now lets both pass the short-id loop,
        // which would yield ambiguous=null. The full-RawId narrowing must pick the entry that
        // matches the record's Qualifiers value.
        var providerDetails = new ProviderDetails
        {
            ProviderName = Constants.TestProviderName,
            Events =
            [
                new EventModel
                {
                    Id = 0x40016001,
                    Version = 0,
                    Keywords = [],
                    LogName = Constants.SystemLogName,
                    Description = "Qualifier 0x4001 message.",
                    Template = string.Empty
                },
                new EventModel
                {
                    Id = 0x80006001,
                    Version = 0,
                    Keywords = [],
                    LogName = Constants.SystemLogName,
                    Description = "Qualifier 0x8000 message.",
                    Template = string.Empty
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
            ProviderName = Constants.TestProviderName,
            Id = 24577,
            Qualifiers = 0x4001,
            Version = 0,
            LogName = Constants.SystemLogName,
            Properties = []
        };

        // Act
        var displayEvent = resolver.ResolveEvent(eventRecord);

        // Assert
        Assert.NotNull(displayEvent);
        Assert.Contains("Qualifier 0x4001 message", displayEvent.Description);
    }

    [Fact]
    public void ResolveEvent_WithModernManifestFullRawIdAndEmptyTemplate_ShouldResolveViaShortIdFallback()
    {
        // Arrange - reproduces the Microsoft-Windows-WPDClassInstaller EventID 24577 case.
        // The classic-emitted event has Qualifiers=0x4001 and EventID=24577 (=0x6001), but
        // the modern manifest stores it under the full 32-bit RawId (0x4001 << 16) | 0x6001
        // = 0x40016001 (1073831937). GetEventsById(24577) returns nothing, so the short-cast
        // fallback in the modern-event matcher is the only path that can match. The manifest entry has
        // an empty Template and the event carries no EventData properties — the fallback's
        // template gate must accept that combination.
        var providerDetails = new ProviderDetails
        {
            ProviderName = Constants.TestProviderName,
            Events =
            [
                new EventModel
                {
                    Id = 0x40016001,
                    Version = 0,
                    Keywords = [],
                    LogName = Constants.SystemLogName,
                    Description = "Compatibility layers registered.",
                    Template = string.Empty
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
            ProviderName = Constants.TestProviderName,
            Id = 24577,
            Qualifiers = 0x4001,
            Version = 0,
            LogName = Constants.SystemLogName,
            Properties = []
        };

        // Act
        var displayEvent = resolver.ResolveEvent(eventRecord);

        // Assert
        Assert.NotNull(displayEvent);
        Assert.Contains("Compatibility layers registered", displayEvent.Description);
    }

    [Fact]
    public void ResolveEvent_WithMultipleDataNodesOnOneLine_ShouldCountAllElements()
    {
        // Arrange - minified template with multiple data elements on one line
        var (details, eventRecord) = EventUtils.CreateModernEvent(
            "User: %1, Action: %2",
            "<template><data name=\"User\" inType=\"win:UnicodeString\" outType=\"xs:string\"/><data name=\"Action\" inType=\"win:UnicodeString\"/></template>",
            ["Admin", "Login"]);

        var resolver = new TestEventResolver([details]);

        // Act
        var displayEvent = resolver.ResolveEvent(eventRecord);

        // Assert
        Assert.NotNull(displayEvent);
        Assert.Contains("Admin", displayEvent.Description);
        Assert.Contains("Login", displayEvent.Description);
        Assert.DoesNotContain("Failed to resolve", displayEvent.Description);
    }

    [Fact]
    public void ResolveEvent_WithMultipleLegacyMessages_ShouldDisambiguateByLogLink()
    {
        // Arrange - two messages with same ShortId, but different LogLinks
        var providerDetails = new ProviderDetails
        {
            ProviderName = Constants.TestProviderName,
            Events = [],
            Messages =
            [
                new MessageModel
                {
                    ProviderName = Constants.TestProviderName,
                    ShortId = 1000,
                    Text = "Application Message",
                    LogLink = Constants.ApplicationLogName
                },
                new MessageModel
                {
                    ProviderName = Constants.TestProviderName,
                    ShortId = 1000,
                    Text = "System Message",
                    LogLink = Constants.SystemLogName
                }
            ],
            Parameters = [],
            Keywords = new Dictionary<long, string>(),
            Tasks = new Dictionary<int, string>()
        };

        var resolver = new TestEventResolver([providerDetails]);

        var eventRecord = new EventRecord
        {
            ProviderName = Constants.TestProviderName,
            Id = 1000,
            LogName = Constants.ApplicationLogName,
            Properties = ["prop1"]
        };

        // Act
        var displayEvent = resolver.ResolveEvent(eventRecord);

        // Assert
        Assert.NotNull(displayEvent);
        Assert.Contains("Application Message", displayEvent.Description);
        Assert.DoesNotContain("System Message", displayEvent.Description);
    }

    [Fact]
    public void ResolveEvent_WithMultipleLegacyMessages_ShouldDisambiguateBySeverity()
    {
        // Arrange - two messages with same ShortId but different RawId severity bits
        // RawId bits 31-30: 00=Success, 01=Informational, 10=Warning, 11=Error
        var providerDetails = new ProviderDetails
        {
            ProviderName = Constants.TestProviderName,
            Events = [],
            Messages =
            [
                new MessageModel
                {
                    ProviderName = Constants.TestProviderName,
                    ShortId = 2,
                    RawId = 2, // severity 00 = Success
                    Text = "Success: %1"
                },
                new MessageModel
                {
                    ProviderName = Constants.TestProviderName,
                    ShortId = 2,
                    RawId = unchecked(0xC0000002), // severity 11 = Error
                    Text = "Error: %1"
                }
            ],
            Parameters = [],
            Keywords = new Dictionary<long, string>(),
            Tasks = new Dictionary<int, string>()
        };

        var resolver = new TestEventResolver([providerDetails]);

        // Event with Level=2 (Error) should match the Error-severity message
        var eventRecord = new EventRecord
        {
            ProviderName = Constants.TestProviderName,
            Id = 2,
            Level = 2,
            LogName = Constants.ApplicationLogName,
            Properties = ["disk defrag"]
        };

        // Act
        var displayEvent = resolver.ResolveEvent(eventRecord);

        // Assert - should pick the error message
        Assert.NotNull(displayEvent);
        Assert.Contains("Error: disk defrag", displayEvent.Description);
    }

    [Fact]
    public void ResolveEvent_WithMultipleLegacyMessages_ShouldReturnDefaultDescription()
    {
        // Arrange - multiple messages with no LogLink, so disambiguation fails
        var providerDetails = new ProviderDetails
        {
            ProviderName = Constants.TestProviderName,
            Events = [],
            Messages =
            [
                new MessageModel { ProviderName = Constants.TestProviderName, ShortId = 1000, Text = "Message 1" },
                new MessageModel { ProviderName = Constants.TestProviderName, ShortId = 1000, Text = "Message 2" }
            ],
            Parameters = [],
            Keywords = new Dictionary<long, string>(),
            Tasks = new Dictionary<int, string>()
        };

        var resolver = new TestEventResolver([providerDetails]);

        var eventRecord = new EventRecord
        {
            ProviderName = Constants.TestProviderName,
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
    public void ResolveEvent_WithMultipleLegacyMessagesSameSeverity_ShouldDisambiguateByQualifier()
    {
        // Arrange - reproduces the Microsoft-Windows-Defrag EventID 258 case where two
        // messages share the same EventId and severity but differ in their high 16 bits
        // (Qualifier). Windows identifies the right entry by full message ID, so
        // RawId == (Qualifiers << 16) | EventId.
        var providerDetails = new ProviderDetails
        {
            ProviderName = Constants.TestProviderName,
            Events = [],
            Messages =
            [
                new MessageModel
                {
                    ProviderName = Constants.TestProviderName,
                    ShortId = 258,
                    RawId = 0x00000102, // Qualifier=0, severity 00=Success
                    Text = "The storage optimizer successfully completed %1 on %2"
                },
                new MessageModel
                {
                    ProviderName = Constants.TestProviderName,
                    ShortId = 258,
                    RawId = 0x09000102, // Qualifier=0x900, severity 00=Success
                    Text = "The retrim operation was skipped"
                }
            ],
            Parameters = [],
            Keywords = new Dictionary<long, string>(),
            Tasks = new Dictionary<int, string>()
        };

        var resolver = new TestEventResolver([providerDetails]);

        var eventRecord = new EventRecord
        {
            ProviderName = Constants.TestProviderName,
            Id = 258,
            Qualifiers = 0,
            Level = 0,
            LogName = Constants.ApplicationLogName,
            Properties = ["defragmentation", "Data (E:)"]
        };

        // Act
        var displayEvent = resolver.ResolveEvent(eventRecord);

        // Assert
        Assert.NotNull(displayEvent);
        Assert.Equal("The storage optimizer successfully completed defragmentation on Data (E:)", displayEvent.Description);
    }

    [Fact]
    public void ResolveEvent_WithMultipleLengthPrefixedBinaryPairs_ShouldMatchTemplate()
    {
        // Arrange - template has 6 data elements with 2 length-prefixed binary pairs;
        // each length provider is consumed internally, so 4 properties are surfaced.
        var (details, _) = EventUtils.CreateModernEvent(
            "Param1: %1, Param2: %2",
            """
                <template>
                  <data name="param1" inType="win:UnicodeString" outType="xs:string"/>
                  <data name="param2" inType="win:UnicodeString" outType="xs:string"/>
                  <data name="__bin1Length" inType="win:UInt32"/>
                  <data name="BinaryData1" inType="win:Binary" length="__bin1Length"/>
                  <data name="__bin2Length" inType="win:UInt32"/>
                  <data name="BinaryData2" inType="win:Binary" length="__bin2Length"/>
                </template>
                """,
            ["Value1", "Value2", new byte[] { 0x01 }, new byte[] { 0x02 }]);

        var resolver = new TestEventResolver([details]);

        // 4 visible properties: param1, param2, BinaryData1, BinaryData2
        var eventRecord = new EventRecord
        {
            ProviderName = Constants.TestProviderName,
            Id = 1000,
            Version = 0,
            LogName = Constants.ApplicationLogName,
            Properties = ["Value1", "Value2", new byte[] { 0x01 }, new byte[] { 0x02 }]
        };

        // Act
        var displayEvent = resolver.ResolveEvent(eventRecord);

        // Assert
        Assert.NotNull(displayEvent);
        Assert.Equal("Param1: Value1, Param2: Value2", displayEvent.Description);
    }

    [Fact]
    public void ResolveEvent_WithMultipleProperties_ShouldFormatAllProperties()
    {
        // Arrange
        var providerDetails = new ProviderDetails
        {
            ProviderName = Constants.TestProviderName,
            Events = [],
            Messages =
            [
                new MessageModel
                {
                    ProviderName = Constants.TestProviderName,
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
            ProviderName = Constants.TestProviderName,
            Id = 1000,
            Properties = ["StringValue", 42, true]
        };

        // Act
        var displayEvent = resolver.ResolveEvent(eventRecord);

        // Assert
        Assert.NotNull(displayEvent);
        Assert.Contains("StringValue", displayEvent.Description);
        Assert.Contains("42", displayEvent.Description);
        Assert.Contains("true", displayEvent.Description);
    }

    [Fact]
    public void ResolveEvent_WithNegativeInt32_ShouldFormatAsSignedDecimal()
    {
        var (details, eventRecord) = EventUtils.CreateModernEvent(
            "Value: %1",
            """<template><data name="Value" inType="win:Int32"/></template>""",
            [-1]);

        var resolver = new TestEventResolver([details]);

        var displayEvent = resolver.ResolveEvent(eventRecord);

        Assert.NotNull(displayEvent);
        Assert.Contains("Value: -1", displayEvent.Description);
    }

    [Fact]
    public void ResolveEvent_WithNegativeSByteAndNTStatusOutType_ShouldFallBackToSignedDecimal()
    {
        // sbyte is excluded from NTStatus resolution and must render as its signed decimal value.
        var (details, eventRecord) = EventUtils.CreateModernEvent(
            "Status: %1",
            """<template><data name="Status" inType="win:Int8" outType="win:NTStatus"/></template>""",
            [(sbyte)-1]);

        var resolver = new TestEventResolver([details]);

        var displayEvent = resolver.ResolveEvent(eventRecord);

        Assert.NotNull(displayEvent);
        Assert.Contains("Status: -1", displayEvent.Description);
    }

    [Fact]
    public void ResolveEvent_WithNewlineSeparatedDataAttributes_ShouldCountAllElements()
    {
        // Arrange - template uses newlines instead of spaces after <data
        var (details, eventRecord) = EventUtils.CreateModernEvent(
            "User: %1, Action: %2",
            "<template><data\nname=\"User\"\ninType=\"win:UnicodeString\"\noutType=\"xs:string\"/><data\nname=\"Action\"\ninType=\"win:UnicodeString\"/></template>",
            ["Admin", "Login"]);

        var resolver = new TestEventResolver([details]);

        // Act
        var displayEvent = resolver.ResolveEvent(eventRecord);

        // Assert
        Assert.NotNull(displayEvent);
        Assert.Contains("Admin", displayEvent.Description);
        Assert.Contains("Login", displayEvent.Description);
        Assert.DoesNotContain("Failed to resolve", displayEvent.Description);
    }

    [Fact]
    public void ResolveEvent_WithNonClassicEventIdZeroAndNoProviderMetadata_ShouldNotPrependSystemMessage()
    {
        // Arrange - regression guard: only classic-keyword events get the Win32
        // system-message prepend. Modern (manifest) events with EventId 0 should fall
        // through to the property tail without injecting the system message for ERROR_SUCCESS.
        var providerDetails = new ProviderDetails
        {
            ProviderName = Constants.TestProviderName,
            Events = [],
            Messages = [],
            Parameters = [],
            Keywords = new Dictionary<long, string>(),
            Tasks = new Dictionary<int, string>()
        };

        var resolver = new TestEventResolver([providerDetails]);

        var eventRecord = new EventRecord
        {
            ProviderName = Constants.TestProviderName,
            Id = 0,
            Keywords = null,
            Properties = ["payload-a", "payload-b"]
        };

        // Act
        var displayEvent = resolver.ResolveEvent(eventRecord);

        // Assert
        Assert.NotNull(displayEvent);
        // Compare against the locale's system-message text for ERROR_SUCCESS so the
        // negative assertion catches an incorrect prepend on non-English Windows too.
        var win32SuccessMessage = NativeMethods.FormatSystemMessage(0);
        if (!string.IsNullOrWhiteSpace(win32SuccessMessage))
        {
            Assert.DoesNotContain(win32SuccessMessage, displayEvent.Description);
        }

        Assert.Contains("The following information was included with the event:", displayEvent.Description);
        Assert.Contains("payload-a", displayEvent.Description);
    }

    [Fact]
    public void ResolveEvent_WithNoProviderDetails_ShouldReturnDefaultDescription()
    {
        // Arrange
        var resolver = new TestEventResolver();

        var eventRecord = new EventRecord
        {
            ProviderName = Constants.NonExistentProviderName,
            Id = 1000,
            ComputerName = Constants.TestComputer,
            LogName = Constants.ApplicationLogName
        };

        // Act
        resolver.LoadProviderDetails(eventRecord);
        var displayEvent = resolver.ResolveEvent(eventRecord);

        // Assert
        Assert.NotNull(displayEvent);
        Assert.Contains("No matching provider", displayEvent.Description);
    }

    [Fact]
    public void ResolveEvent_WithNormalMismatch_ShouldStillReject()
    {
        // Arrange - template has 4 data elements with NO length-prefixed binary pairs
        // and event has 2 properties — this should NOT match
        var (details, _) = EventUtils.CreateModernEvent(
            "A: %1, B: %2, C: %3, D: %4",
            """
                <template>
                  <data name="A" inType="win:UnicodeString" outType="xs:string"/>
                  <data name="B" inType="win:UnicodeString" outType="xs:string"/>
                  <data name="C" inType="win:UnicodeString" outType="xs:string"/>
                  <data name="D" inType="win:UInt32"/>
                </template>
                """,
            ["val1", "val2", "val3", "val4"]);

        var resolver = new TestEventResolver([details]);

        var eventRecord = new EventRecord
        {
            ProviderName = Constants.TestProviderName,
            Id = 1000,
            Version = 0,
            LogName = Constants.ApplicationLogName,
            Properties = ["val1", "val2"]
        };

        // Act
        var displayEvent = resolver.ResolveEvent(eventRecord);

        // Assert - should NOT match the 4-property template for a 2-property event
        Assert.NotNull(displayEvent);
        Assert.DoesNotContain("A: val1", displayEvent.Description);
    }

    [Fact]
    public void ResolveEvent_WithNtStatusOutType_ShouldFallbackToHexForUnknownCodes()
    {
        // Arrange - Unknown NTStatus should show hex
        const uint unknownCode = 0xDEADBEEF;

        var (details, eventRecord) = EventUtils.CreateModernEvent(
            "Status: %1",
            """<template><data name="Status" inType="win:UInt32" outType="win:NTStatus"/></template>""",
            [unknownCode]);

        var resolver = new TestEventResolver([details]);

        // Act
        var displayEvent = resolver.ResolveEvent(eventRecord);

        // Assert — if the OS resolves this code to a message, the description
        // won't contain the hex fallback. Only assert hex when the system has
        // no message for this code (avoids environment-dependent failures).
        Assert.NotNull(displayEvent);

        var ntStatusMessage = NativeMethods.FormatNtStatusMessage(unknownCode);
        var systemMessage = NativeMethods.FormatSystemMessage(unknownCode);

        if (ntStatusMessage is null && systemMessage is null)
        {
            Assert.Contains("0xDEADBEEF", displayEvent.Description);
        }
        else
        {
            Assert.NotNull(displayEvent.Description);
        }
    }

    [Fact]
    public void ResolveEvent_WithNtStatusOutType_ShouldResolveToStatusString()
    {
        // Arrange - win:NTStatus outType should resolve STATUS_SUCCESS
        var (details, eventRecord) = EventUtils.CreateModernEvent(
            "Status: %1",
            """<template><data name="Status" inType="win:UInt32" outType="win:NTStatus"/></template>""",
            [(uint)0x00000000]);

        var resolver = new TestEventResolver([details]);

        // Act
        var displayEvent = resolver.ResolveEvent(eventRecord);

        // Assert - should contain the resolved status string, not "0"
        Assert.NotNull(displayEvent);
        Assert.DoesNotContain("Status: 0", displayEvent.Description);
        Assert.False(string.IsNullOrEmpty(displayEvent.Description));
    }

    [Fact]
    public void ResolveEvent_WithNullKeywords_ShouldReturnEmptyKeywordList()
    {
        // Arrange
        var resolver = new TestEventResolver();

        var eventRecord = new EventRecord
        {
            ProviderName = Constants.TestProviderName,
            Id = 1000,
            Keywords = null
        };

        // Act
        resolver.LoadProviderDetails(eventRecord);
        var displayEvent = resolver.ResolveEvent(eventRecord);

        // Assert
        Assert.NotNull(displayEvent);
        Assert.Empty(displayEvent.Keywords);
    }

    [Fact]
    public void ResolveEvent_WithNullLogNameInEventModel_ShouldMatchEmptyLogNameInRecord()
    {
        // Arrange - EventModel.LogName is null, EventRecord.LogName defaults to ""
        var providerDetails = new ProviderDetails
        {
            ProviderName = Constants.TestProviderName,
            Events =
            [
                new EventModel
                {
                    Id = 1000,
                    Version = 1,
                    Keywords = [],
                    LogName = null, // null in database/provider
                    Description = "Matched with null LogName: %1",
                    Template = "<template><data name=\"Val\" inType=\"win:UnicodeString\" outType=\"xs:string\"/></template>"
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
            ProviderName = Constants.TestProviderName,
            Id = 1000,
            Version = 1,
            LogName = "", // defaults to empty string
            Properties = ["hello"]
        };

        // Act
        var displayEvent = resolver.ResolveEvent(eventRecord);

        // Assert - null LogName in EventModel should match "" in EventRecord
        Assert.NotNull(displayEvent);
        Assert.Contains("Matched with null LogName: hello", displayEvent.Description);
    }

    [Fact]
    public void ResolveEvent_WithNullQualifiersAndMultipleLegacyMessages_ShouldFallThroughToSeverity()
    {
        // Arrange - Qualifier-based filter must be skipped when the event has no Qualifiers,
        // preserving the existing severity-based disambiguation behavior.
        var providerDetails = new ProviderDetails
        {
            ProviderName = Constants.TestProviderName,
            Events = [],
            Messages =
            [
                new MessageModel
                {
                    ProviderName = Constants.TestProviderName,
                    ShortId = 50,
                    RawId = 0x00000032, // severity 00=Success
                    Text = "Success: %1"
                },
                new MessageModel
                {
                    ProviderName = Constants.TestProviderName,
                    ShortId = 50,
                    RawId = unchecked(0xC0000032), // severity 11=Error
                    Text = "Error: %1"
                }
            ],
            Parameters = [],
            Keywords = new Dictionary<long, string>(),
            Tasks = new Dictionary<int, string>()
        };

        var resolver = new TestEventResolver([providerDetails]);

        var eventRecord = new EventRecord
        {
            ProviderName = Constants.TestProviderName,
            Id = 50,
            Qualifiers = null,
            Level = 2,
            LogName = Constants.ApplicationLogName,
            Properties = ["payload"]
        };

        // Act
        var displayEvent = resolver.ResolveEvent(eventRecord);

        // Assert
        Assert.NotNull(displayEvent);
        Assert.Contains("Error: payload", displayEvent.Description);
    }

    [Fact]
    public void ResolveEvent_WithNullXml_ShouldReturnEmptyXml()
    {
        // Arrange
        var resolver = new TestEventResolver();

        var eventRecord = new EventRecord
        {
            ProviderName = Constants.TestProviderName,
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
    public void ResolveEvent_WithOutOfRangePropertyIndex_ShouldKeepPlaceholderAndResolveRest()
    {
        // Arrange - legacy message references %7 but only 6 properties exist.
        // This is modeled after ESENT events where the manifest template can reference
        // more properties than the event actually supplies (version mismatch).
        var providerDetails = new ProviderDetails
        {
            ProviderName = Constants.TestProviderName,
            Events = [],
            Messages =
            [
                new MessageModel
                {
                    ProviderName = Constants.TestProviderName,
                    ShortId = 1000,
                    Text = "%1 (%2) %3The database engine started a new instance (%4). (Time=%5 seconds)\r\n\r\nAdditional Data:\r\n%7\r\n\r\nInternal Timing Sequence: %6"
                }
            ],
            Parameters = [],
            Keywords = new Dictionary<long, string>(),
            Tasks = new Dictionary<int, string>()
        };

        var resolver = new TestEventResolver([providerDetails]);

        var eventRecord = new EventRecord
        {
            ProviderName = Constants.TestProviderName,
            Id = 1000,
            Properties = ["SQLEXPRESS", "MSSQL", "\r\n", "1", "3", "[1] 0.000"]
        };

        // Act
        var displayEvent = resolver.ResolveEvent(eventRecord);

        // Assert - all 6 available properties substituted, missing %7 replaced with empty
        Assert.NotNull(displayEvent);
        Assert.Contains("SQLEXPRESS", displayEvent.Description);
        Assert.Contains("MSSQL", displayEvent.Description);
        Assert.Contains("Time=3 seconds", displayEvent.Description);
        Assert.Contains("[1] 0.000", displayEvent.Description);
        Assert.DoesNotContain("%7", displayEvent.Description);
        Assert.DoesNotContain("Failed to resolve", displayEvent.Description);
    }

    [Fact]
    public void ResolveEvent_WithParameterEndingInZero_ShouldNotCorruptText()
    {
        // Arrange - parameter text ends with "0" or "%" characters that should be preserved
        var providerDetails = new ProviderDetails
        {
            ProviderName = Constants.TestProviderName,
            Events = [],
            Messages =
            [
                new MessageModel
                {
                    ProviderName = Constants.TestProviderName,
                    ShortId = 1000,
                    Text = "Status: %%2000"
                }
            ],
            Parameters =
            [
                new MessageModel
                {
                    ProviderName = Constants.TestProviderName,
                    RawId = 2000,
                    Text = "Version 1.0"
                }
            ],
            Keywords = new Dictionary<long, string>(),
            Tasks = new Dictionary<int, string>()
        };

        var resolver = new TestEventResolver([providerDetails]);

        var eventRecord = new EventRecord
        {
            ProviderName = Constants.TestProviderName,
            Id = 1000,
            Properties = []
        };

        // Act
        var displayEvent = resolver.ResolveEvent(eventRecord);

        // Assert - "Version 1.0" should NOT be truncated to "Version 1."
        Assert.NotNull(displayEvent);
        Assert.Contains("Version 1.0", displayEvent.Description);
    }

    [Fact]
    public void ResolveEvent_WithParameterHavingPercentZeroTerminator_ShouldRemoveOnlyTerminator()
    {
        // Arrange - parameter text has actual %0 terminator that should be removed
        var providerDetails = new ProviderDetails
        {
            ProviderName = Constants.TestProviderName,
            Events = [],
            Messages =
            [
                new MessageModel
                {
                    ProviderName = Constants.TestProviderName,
                    ShortId = 1000,
                    Text = "Result: %%3000"
                }
            ],
            Parameters =
            [
                new MessageModel
                {
                    ProviderName = Constants.TestProviderName,
                    RawId = 3000,
                    Text = "Success%0"
                }
            ],
            Keywords = new Dictionary<long, string>(),
            Tasks = new Dictionary<int, string>()
        };

        var resolver = new TestEventResolver([providerDetails]);

        var eventRecord = new EventRecord
        {
            ProviderName = Constants.TestProviderName,
            Id = 1000,
            Properties = []
        };

        // Act
        var displayEvent = resolver.ResolveEvent(eventRecord);

        // Assert - "%0" terminator removed, but the rest preserved
        Assert.NotNull(displayEvent);
        Assert.Contains("Success", displayEvent.Description);
        Assert.DoesNotContain("%0", displayEvent.Description);
    }

    [Fact]
    public void ResolveEvent_WithParameterSubstitution_ShouldResolveParameters()
    {
        // Arrange
        var providerDetails = new ProviderDetails
        {
            ProviderName = Constants.TestProviderName,
            Events = [],
            Messages =
            [
                new MessageModel
                {
                    ProviderName = Constants.TestProviderName,
                    ShortId = 1000,
                    Text = "Status: %%1001 for user %1"
                }
            ],
            Parameters =
            [
                new MessageModel
                {
                    ProviderName = Constants.TestProviderName,
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
            ProviderName = Constants.TestProviderName,
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
    public void ResolveEvent_WithPercentZeroTerminator_ShouldSkipTerminatorAndResolveDescription()
    {
        // Arrange - %0 is a Windows Event Log message terminator that should be stripped
        var providerDetails = new ProviderDetails
        {
            ProviderName = Constants.TestProviderName,
            Events = [],
            Messages =
            [
                new MessageModel
                {
                    ProviderName = Constants.TestProviderName,
                    ShortId = 1000,
                    Text = "Windows Search Service has created default configuration for new user '%1' .%n%0"
                }
            ],
            Parameters = [],
            Keywords = new Dictionary<long, string>(),
            Tasks = new Dictionary<int, string>()
        };

        var resolver = new TestEventResolver([providerDetails]);

        var eventRecord = new EventRecord
        {
            ProviderName = Constants.TestProviderName,
            Id = 1000,
            Properties = ["TestUser", "ExtraProperty"]
        };

        // Act - should not throw and should produce valid description
        var displayEvent = resolver.ResolveEvent(eventRecord);

        // Assert
        Assert.NotNull(displayEvent);
        Assert.Contains("TestUser", displayEvent.Description);
        Assert.DoesNotContain("%0", displayEvent.Description);
        Assert.DoesNotContain("Failed to resolve", displayEvent.Description);
    }

    [Fact]
    public void ResolveEvent_WithPopulatedProviderButUnknownEventAndMultipleProperties_ShouldReturnNoMatchingMessage()
    {
        // Arrange - provider has metadata for some other event, but not this one. We have
        // multiple properties, so the single-property dump does not apply. The result should
        // be "no matching message" (we know the provider, just not this specific event).
        var providerDetails = new ProviderDetails
        {
            ProviderName = Constants.TestProviderName,
            Events = [],
            Messages =
            [
                new MessageModel { ProviderName = Constants.TestProviderName, ShortId = 1234, Text = "Some other event" }
            ],
            Parameters = [],
            Keywords = new Dictionary<long, string>(),
            Tasks = new Dictionary<int, string>()
        };

        var resolver = new TestEventResolver([providerDetails]);

        var eventRecord = new EventRecord
        {
            ProviderName = Constants.TestProviderName,
            Id = 9999,
            Properties = ["a", "b", "c"]
        };

        // Act
        var displayEvent = resolver.ResolveEvent(eventRecord);

        // Assert
        Assert.NotNull(displayEvent);
        Assert.Contains("No matching message", displayEvent.Description);
        Assert.DoesNotContain("No matching provider", displayEvent.Description);
        Assert.DoesNotContain("Failed to resolve", displayEvent.Description);
    }

    [Fact]
    public void ResolveEvent_WithPrimaryAndSupplementalDetailsButUnmappedEventId_ShouldReturnNoMatchingMessage()
    {
        // Arrange — supplemental loads (primary has no match for Id) and is forwarded into
        // CreateEventModel; a task seeded only in supplemental proves the forwarding.
        const string SupplementalOnlyTaskName = "SupplementalOnlyTask";
        const int SharedTaskId = 42;

        var primaryDetails = new ProviderDetails
        {
            ProviderName = Constants.TestProviderName,
            Events = [],
            Messages =
            [
                new MessageModel { ProviderName = Constants.TestProviderName, ShortId = 100, Text = "Primary event 100" }
            ],
            Parameters = [],
            Keywords = new Dictionary<long, string>(),
            Opcodes = new Dictionary<int, string>(),
            Tasks = new Dictionary<int, string>()
        };

        var supplementalDetails = new ProviderDetails
        {
            ProviderName = Constants.TestProviderName,
            Events = [],
            Messages =
            [
                new MessageModel { ProviderName = Constants.TestProviderName, ShortId = 200, Text = "Supplemental event 200" }
            ],
            Parameters = [],
            Keywords = new Dictionary<long, string>(),
            Opcodes = new Dictionary<int, string>(),
            Tasks = new Dictionary<int, string> { { SharedTaskId, SupplementalOnlyTaskName } }
        };

        var resolver = new SupplementalTestResolver([primaryDetails], supplementalDetails);

        var eventRecord = new EventRecord
        {
            ProviderName = Constants.TestProviderName,
            Id = 9999,
            Task = SharedTaskId,
            Properties = []
        };

        // Act
        var displayEvent = resolver.ResolveEvent(eventRecord);

        // Assert
        Assert.NotNull(displayEvent);
        Assert.Contains("No matching message", displayEvent.Description);
        Assert.DoesNotContain("No matching provider", displayEvent.Description);
        Assert.DoesNotContain("Failed to resolve", displayEvent.Description);
        Assert.DoesNotContain("Primary event 100", displayEvent.Description);
        Assert.DoesNotContain("Supplemental event 200", displayEvent.Description);
        Assert.Equal(SupplementalOnlyTaskName, displayEvent.TaskCategory);
    }

    [Fact]
    public void ResolveEvent_WithPrimaryAndSupplementalTask_ShouldPreferPrimaryTaskName()
    {
        // Arrange - both primary and supplemental define the task. Primary wins.
        var primaryDetails = new ProviderDetails
        {
            ProviderName = Constants.TestProviderName,
            Events =
            [
                new EventModel
                {
                    Id = 100,
                    Version = 0,
                    Task = 7,
                    LogName = Constants.ApplicationLogName,
                    Description = "Primary event",
                    Keywords = [],
                    Template = Constants.EmptyTemplate
                }
            ],
            Messages = [],
            Parameters = [],
            Keywords = new Dictionary<long, string>(),
            Opcodes = new Dictionary<int, string>(),
            Tasks = new Dictionary<int, string> { { 7, Constants.PrimaryTask } }
        };

        var supplementalDetails = new ProviderDetails
        {
            ProviderName = Constants.TestProviderName,
            Events = [],
            Messages = [],
            Parameters = [],
            Keywords = new Dictionary<long, string>(),
            Opcodes = new Dictionary<int, string>(),
            Tasks = new Dictionary<int, string> { { 7, Constants.SupplementalTask } }
        };

        var resolver = new SupplementalTestResolver([primaryDetails], supplementalDetails);

        var eventRecord = new EventRecord
        {
            ProviderName = Constants.TestProviderName,
            Id = 100,
            Task = 7,
            LogName = Constants.ApplicationLogName
        };

        // Act
        var displayEvent = resolver.ResolveEvent(eventRecord);

        // Assert
        Assert.NotNull(displayEvent);
        Assert.Equal(Constants.PrimaryTask, displayEvent.TaskCategory);
    }

    [Fact]
    public void ResolveEvent_WithPropertyIndexOutOfRange_ShouldResolveAvailableProperties()
    {
        // Arrange - template references %3 but only 2 properties exist
        var providerDetails = new ProviderDetails
        {
            ProviderName = Constants.TestProviderName,
            Events = [],
            Messages =
            [
                new MessageModel
                {
                    ProviderName = Constants.TestProviderName,
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
            ProviderName = Constants.TestProviderName,
            Id = 1000,
            Properties = ["Value1", "Value2"] // Only 2 properties, but template expects 3
        };

        // Act
        var displayEvent = resolver.ResolveEvent(eventRecord);

        // Assert - available properties should be substituted, missing %3 replaced with empty
        Assert.NotNull(displayEvent);
        Assert.Contains("Property1: Value1", displayEvent.Description);
        Assert.Contains("Property2: Value2", displayEvent.Description);
        Assert.Contains("Property3: ", displayEvent.Description);
        Assert.DoesNotContain("%3", displayEvent.Description);
    }

    [Fact]
    public void ResolveEvent_WithProviderHavingOnlyKeywordsTasksOpcodes_ShouldReturnNoMatchingMessage()
    {
        // Arrange - modern providers can register Keywords/Tasks/Opcodes without any
        // Events or Messages. The provider exists, so we should report "no matching message"
        // rather than "no matching provider".
        var providerDetails = new ProviderDetails
        {
            ProviderName = Constants.TestProviderName,
            Events = [],
            Messages = [],
            Parameters = [],
            Keywords = new Dictionary<long, string> { { unchecked((long)0x8000000000000000UL), "ClassicKeyword" } },
            Opcodes = new Dictionary<int, string> { { 1, "Start" } },
            Tasks = new Dictionary<int, string> { { 1, "InitTask" } }
        };

        var resolver = new TestEventResolver([providerDetails]);

        var eventRecord = new EventRecord
        {
            ProviderName = Constants.TestProviderName,
            Id = 9999,
            Properties = ["a", "b", "c"]
        };

        // Act
        var displayEvent = resolver.ResolveEvent(eventRecord);

        // Assert
        Assert.NotNull(displayEvent);
        Assert.Contains("No matching message", displayEvent.Description);
        Assert.DoesNotContain("No matching provider", displayEvent.Description);
        Assert.DoesNotContain("Failed to resolve", displayEvent.Description);
    }

    [Fact]
    public void ResolveEvent_WithProviderKeywords_ShouldResolveKeywords()
    {
        // Arrange
        var providerDetails = new ProviderDetails
        {
            ProviderName = Constants.TestProviderName,
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
            ProviderName = Constants.TestProviderName,
            Id = 1000,
            Keywords = 0x3 // Both custom keywords
        };

        // Act
        var displayEvent = resolver.ResolveEvent(eventRecord);

        // Assert
        Assert.NotNull(displayEvent);
        Assert.Contains("CustomKeyword1", displayEvent.Keywords);
        Assert.Contains("CustomKeyword2", displayEvent.Keywords);
    }

    [Fact]
    public void ResolveEvent_WithProviderKeywordsInBits32To47_ShouldResolveKeywords()
    {
        // Arrange - keyword in bit 32 (0x100000000), which was previously masked out
        var providerDetails = new ProviderDetails
        {
            ProviderName = Constants.TestProviderName,
            Events = [],
            Messages = [],
            Parameters = [],
            Keywords = new Dictionary<long, string>
            {
                { 0x100000000L, "HighBitKeyword" },
                { 0x1, "LowBitKeyword" }
            },
            Tasks = new Dictionary<int, string>()
        };

        var resolver = new TestEventResolver([providerDetails]);

        var eventRecord = new EventRecord
        {
            ProviderName = Constants.TestProviderName,
            Id = 1000,
            Keywords = 0x100000001L // Both high and low provider keywords
        };

        // Act
        var displayEvent = resolver.ResolveEvent(eventRecord);

        // Assert
        Assert.NotNull(displayEvent);
        Assert.Contains("HighBitKeyword", displayEvent.Keywords);
        Assert.Contains("LowBitKeyword", displayEvent.Keywords);
    }

    [Fact]
    public void ResolveEvent_WithQualifierMatchingMultipleMessages_ShouldNarrowBeforeLogLink()
    {
        // Arrange - two messages share the matching qualifier; a third message has a
        // different qualifier but a LogLink that would otherwise win. Qualifier filter
        // must narrow the set first so LogLink only considers matching-qualifier candidates.
        var providerDetails = new ProviderDetails
        {
            ProviderName = Constants.TestProviderName,
            Events = [],
            Messages =
            [
                new MessageModel
                {
                    ProviderName = Constants.TestProviderName,
                    ShortId = 258,
                    RawId = 0x09000102,
                    LogLink = "Application",
                    Text = "Should not match: wrong LogLink"
                },
                new MessageModel
                {
                    ProviderName = Constants.TestProviderName,
                    ShortId = 258,
                    RawId = 0x09000102,
                    LogLink = "System",
                    Text = "Correct: %1"
                },
                new MessageModel
                {
                    ProviderName = Constants.TestProviderName,
                    ShortId = 258,
                    RawId = 0x4A000102,
                    LogLink = "System",
                    Text = "Should not match: wrong qualifier"
                }
            ],
            Parameters = [],
            Keywords = new Dictionary<long, string>(),
            Tasks = new Dictionary<int, string>()
        };

        var resolver = new TestEventResolver([providerDetails]);

        var eventRecord = new EventRecord
        {
            ProviderName = Constants.TestProviderName,
            Id = 258,
            Qualifiers = 0x0900,
            Level = 0,
            LogName = "System",
            Properties = ["payload"]
        };

        // Act
        var displayEvent = resolver.ResolveEvent(eventRecord);

        // Assert
        Assert.NotNull(displayEvent);
        Assert.Equal("Correct: payload", displayEvent.Description);
    }

    [Fact]
    public void ResolveEvent_WithQualifierMismatch_ShouldFallThroughToSeverity()
    {
        // Arrange - event has a Qualifier value that no message matches; disambiguation
        // must fall through to the existing severity-based check rather than failing.
        var providerDetails = new ProviderDetails
        {
            ProviderName = Constants.TestProviderName,
            Events = [],
            Messages =
            [
                new MessageModel
                {
                    ProviderName = Constants.TestProviderName,
                    ShortId = 100,
                    RawId = 0x00000064, // Qualifier=0, severity 00=Success
                    Text = "Success: %1"
                },
                new MessageModel
                {
                    ProviderName = Constants.TestProviderName,
                    ShortId = 100,
                    RawId = unchecked(0xC0000064), // Qualifier=0xC000, severity 11=Error
                    Text = "Error: %1"
                }
            ],
            Parameters = [],
            Keywords = new Dictionary<long, string>(),
            Tasks = new Dictionary<int, string>()
        };

        var resolver = new TestEventResolver([providerDetails]);

        // Qualifier 0x1234 matches no message in the table.
        var eventRecord = new EventRecord
        {
            ProviderName = Constants.TestProviderName,
            Id = 100,
            Qualifiers = 0x1234,
            Level = 2,
            LogName = Constants.ApplicationLogName,
            Properties = ["payload"]
        };

        // Act
        var displayEvent = resolver.ResolveEvent(eventRecord);

        // Assert - Level=2 maps to severity 11=Error, so fall-through picks the Error message.
        Assert.NotNull(displayEvent);
        Assert.Contains("Error: payload", displayEvent.Description);
    }

    [Fact]
    public void ResolveEvent_WithQualifiersPresentAndShortOnlyManifest_ShouldFallBackToShortMatch()
    {
        // Arrange - record carries Qualifiers=0x4001 but the manifest only has the short-id
        // entry (no full-RawId encoded variant). Full-RawId narrowing finds nothing, so the
        // low-16 fallback must still match the short-only entry.
        var providerDetails = new ProviderDetails
        {
            ProviderName = Constants.TestProviderName,
            Events =
            [
                new EventModel
                {
                    Id = 24577,
                    Version = 0,
                    Keywords = [],
                    LogName = Constants.SystemLogName,
                    Description = "Short-only manifest entry.",
                    Template = string.Empty
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
            ProviderName = Constants.TestProviderName,
            Id = 24577,
            Qualifiers = 0x4001,
            Version = 0,
            LogName = Constants.ApplicationLogName,
            Properties = []
        };

        // Act
        var displayEvent = resolver.ResolveEvent(eventRecord);

        // Assert
        Assert.NotNull(displayEvent);
        Assert.Contains("Short-only manifest entry", displayEvent.Description);
    }

    [Fact]
    public void ResolveEvent_WithReferenceArrayShapes_RenderViaToString()
    {
        // Array / Handle / Xml / AnsiString variants all share the reference default branch (-> ToString),
        // matching the pre-unboxing behavior. uint[]/int[]/ushort[] reach EventProperty via FromReference
        // (the path RenderEventProperties uses for them); string[] has a typed implicit operator.
        var (uintArrayDetails, uintArrayRecord) = EventUtils.CreateModernEvent(
            "Value: %1",
            """<template><data name="Value" inType="win:UInt32" outType="win:HexInt32Array"/></template>""",
            [EventProperty.FromReference(new uint[] { 1, 2, 3 })]);

        var (stringArrayDetails, stringArrayRecord) = EventUtils.CreateModernEvent(
            "Value: %1",
            """<template><data name="Value" inType="win:UnicodeString"/></template>""",
            [(string[])["a", "b"]]);

        var uintArrayEvent = new TestEventResolver([uintArrayDetails]).ResolveEvent(uintArrayRecord);
        var stringArrayEvent = new TestEventResolver([stringArrayDetails]).ResolveEvent(stringArrayRecord);

        Assert.NotNull(uintArrayEvent);
        Assert.NotNull(stringArrayEvent);
        Assert.Contains("System.UInt32[]", uintArrayEvent.Description);
        Assert.Contains("System.String[]", stringArrayEvent.Description);
    }

    [Fact]
    public void ResolveEvent_WithSByteAndNTStatusOutType_ShouldFallBackToDecimal()
    {
        // NTStatus resolution excludes sbyte; an sbyte property must render as its decimal value.
        var (details, eventRecord) = EventUtils.CreateModernEvent(
            "Status: %1",
            """<template><data name="Status" inType="win:Int8" outType="win:NTStatus"/></template>""",
            [(sbyte)5]);

        var resolver = new TestEventResolver([details]);

        var displayEvent = resolver.ResolveEvent(eventRecord);

        Assert.NotNull(displayEvent);
        Assert.Contains("Status: 5", displayEvent.Description);
    }

    [Fact]
    public void ResolveEvent_WithSeverityLevel_ShouldResolveLevelString()
    {
        // Arrange
        var resolver = new TestEventResolver();

        // ETW level 0 is "LogAlways" but Windows Event Viewer renders it as "Information"
        var testCases = new[]
        {
            (Level: (byte)0, Expected: "Information"),
            (Level: (byte)1, Expected: "Critical"),
            (Level: (byte)2, Expected: "Error"),
            (Level: (byte)3, Expected: "Warning"),
            (Level: (byte)4, Expected: "Information"),
            (Level: (byte)5, Expected: "Verbose")
        };

        foreach (var testCase in testCases)
        {
            var eventRecord = new EventRecord
            {
                ProviderName = Constants.TestProviderName,
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
    public void ResolveEvent_WithSharedKeywordsAndSid_ShouldInternAcrossEvents()
    {
        // Arrange
        var cache = new EventResolverCache();
        var resolver = new TestEventResolver(cache, Substitute.For<ITraceLogger>());

        const long auditSuccessKeyword = 0x20000000000000L;

        EventRecord CreateRecord() => new()
        {
            ProviderName = "Provider",
            Id = 1000,
            LogName = Constants.ApplicationLogName,
            ComputerName = "Computer",
            Keywords = auditSuccessKeyword,
            UserId = new SecurityIdentifier("S-1-5-18")
        };

        // Act
        var first = resolver.ResolveEvent(CreateRecord());
        var second = resolver.ResolveEvent(CreateRecord());

        // Assert
        Assert.Equal(new[] { "Audit Success" }, first.Keywords);
        Assert.Same(first.Keywords, second.Keywords);
        Assert.NotNull(first.UserId);
        Assert.Same(first.UserId, second.UserId);
    }

    [Fact]
    public void ResolveEvent_WithShortCastFallbackAndHighEventId_ShouldMatchUsingUnsignedComparison()
    {
        // Arrange - record has no Qualifiers and an EventID > 0x7FFF (40000 = 0x9C40). The
        // manifest entry's Id has the same low 16 bits but encodes high bits as well. A signed
        // (short) cast would sign-extend 0x9C40 to a negative number and never match the
        // unsigned record Id, so the fallback must use an unsigned comparison.
        var providerDetails = new ProviderDetails
        {
            ProviderName = Constants.TestProviderName,
            Events =
            [
                new EventModel
                {
                    Id = 0x40019C40,
                    Version = 0,
                    Keywords = [],
                    LogName = Constants.SystemLogName,
                    Description = "High EventID match.",
                    Template = string.Empty
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
            ProviderName = Constants.TestProviderName,
            Id = 40000,
            Qualifiers = null,
            Version = 0,
            LogName = Constants.ApplicationLogName,
            Properties = []
        };

        // Act
        var displayEvent = resolver.ResolveEvent(eventRecord);

        // Assert
        Assert.NotNull(displayEvent);
        Assert.Contains("High EventID match", displayEvent.Description);
    }

    [Fact]
    public void ResolveEvent_WithSid_ShouldFormatAsSddl()
    {
        var (details, eventRecord) = EventUtils.CreateModernEvent(
            "User: %1",
            """<template><data name="User" inType="win:SID"/></template>""",
            [new SecurityIdentifier("S-1-5-18")]);

        var resolver = new TestEventResolver([details]);

        var displayEvent = resolver.ResolveEvent(eventRecord);

        Assert.NotNull(displayEvent);
        Assert.Contains("S-1-5-18", displayEvent.Description);
    }

    [Fact]
    public void ResolveEvent_WithSingleAndDouble_ShouldRoundTripThroughBitField()
    {
        // Regression guard: Single/Double are stored via BitConverter reinterpret, not a numeric cast.
        var (singleDetails, singleRecord) = EventUtils.CreateModernEvent(
            "Value: %1",
            """<template><data name="Value" inType="win:Float"/></template>""",
            [3.5f]);

        var (doubleDetails, doubleRecord) = EventUtils.CreateModernEvent(
            "Value: %1",
            """<template><data name="Value" inType="win:Double"/></template>""",
            [2.5d]);

        var singleEvent = new TestEventResolver([singleDetails]).ResolveEvent(singleRecord);
        var doubleEvent = new TestEventResolver([doubleDetails]).ResolveEvent(doubleRecord);

        Assert.NotNull(singleEvent);
        Assert.NotNull(doubleEvent);
        Assert.Contains("3.5", singleEvent.Description);
        Assert.Contains("2.5", doubleEvent.Description);
    }

    [Fact]
    public void ResolveEvent_WithSinglePropertyAndNoTemplate_ShouldUsePropertyAsDescription()
    {
        // Arrange
        var providerDetails = new ProviderDetails
        {
            ProviderName = Constants.TestProviderName,
            Events = [],
            Messages = [],
            Parameters = [],
            Keywords = new Dictionary<long, string>(),
            Tasks = new Dictionary<int, string>()
        };

        var resolver = new TestEventResolver([providerDetails]);

        var eventRecord = new EventRecord
        {
            ProviderName = Constants.TestProviderName,
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
    public void ResolveEvent_WithSizeT_ShouldFormatAsDecimal()
    {
        var (details, eventRecord) = EventUtils.CreateModernEvent(
            "Value: %1",
            """<template><data name="Value" inType="win:Pointer"/></template>""",
            [(nuint)4096]);

        var resolver = new TestEventResolver([details]);

        var displayEvent = resolver.ResolveEvent(eventRecord);

        Assert.NotNull(displayEvent);
        Assert.Contains("4096", displayEvent.Description);
    }

    [Fact]
    public void ResolveEvent_WithSplitKeywordsBetweenPrimaryAndSupplemental_ShouldMergeWithPrimaryWins()
    {
        // Arrange - event has keyword bits 0x1 and 0x2 set. Primary defines name for 0x1,
        // supplemental defines a different name for 0x1 AND a name for 0x2. Result should
        // include primary's name for 0x1 and supplemental's name for 0x2.
        var primaryDetails = new ProviderDetails
        {
            ProviderName = Constants.TestProviderName,
            Events = [],
            Messages = [],
            Parameters = [],
            Keywords = new Dictionary<long, string> { { 0x1, Constants.PrimaryKeyword } },
            Opcodes = new Dictionary<int, string>(),
            Tasks = new Dictionary<int, string>()
        };

        var supplementalDetails = new ProviderDetails
        {
            ProviderName = Constants.TestProviderName,
            Events =
            [
                new EventModel
                {
                    Id = 200,
                    Version = 0,
                    LogName = Constants.ApplicationLogName,
                    Description = "Supplemental event",
                    Keywords = [],
                    Template = Constants.EmptyTemplate
                }
            ],
            Messages = [],
            Parameters = [],
            Keywords = new Dictionary<long, string>
            {
                { 0x1, Constants.SupplementalKeywordShouldLose },
                { 0x2, Constants.SupplementalKeyword2 }
            },
            Opcodes = new Dictionary<int, string>(),
            Tasks = new Dictionary<int, string>()
        };

        var resolver = new SupplementalTestResolver([primaryDetails], supplementalDetails);

        var eventRecord = new EventRecord
        {
            ProviderName = Constants.TestProviderName,
            Id = 200,
            Keywords = 0x3
        };

        // Act
        var displayEvent = resolver.ResolveEvent(eventRecord);

        // Assert
        Assert.NotNull(displayEvent);
        Assert.Contains(Constants.PrimaryKeyword, displayEvent.Keywords);
        Assert.Contains(Constants.SupplementalKeyword2, displayEvent.Keywords);
        Assert.DoesNotContain(Constants.SupplementalKeywordShouldLose, displayEvent.Keywords);
    }

    [Fact]
    public void ResolveEvent_WithStandardKeywords_ShouldResolveKeywords()
    {
        // Arrange
        var resolver = new TestEventResolver();

        var eventRecord = new EventRecord
        {
            ProviderName = Constants.TestProviderName,
            Id = 1000,
            Keywords = 0x80000000000000 // Classic keyword
        };

        // Act
        resolver.LoadProviderDetails(eventRecord);
        var displayEvent = resolver.ResolveEvent(eventRecord);

        // Assert
        Assert.NotNull(displayEvent);
        Assert.Contains("Classic", displayEvent.Keywords);
    }

    [Fact]
    public void ResolveEvent_WithSupplementalProvider_ShouldFallbackToSupplemental()
    {
        // Arrange - primary provider has some events but not the one we need
        var primaryDetails = new ProviderDetails
        {
            ProviderName = Constants.TestProviderName,
            Events =
            [
                new EventModel
                {
                    Id = 100,
                    Version = 0,
                    LogName = Constants.ApplicationLogName,
                    Description = "Primary event 100: %1",
                    Keywords = [],
                    Template = "<template><data name=\"Val\" inType=\"win:UnicodeString\" outType=\"xs:string\"/></template>"
                }
            ],
            Messages = [],
            Parameters = [],
            Keywords = new Dictionary<long, string>(),
            Tasks = new Dictionary<int, string>()
        };

        // Supplemental has the event we need
        var supplementalDetails = new ProviderDetails
        {
            ProviderName = Constants.TestProviderName,
            Events =
            [
                new EventModel
                {
                    Id = 200,
                    Version = 0,
                    LogName = Constants.ApplicationLogName,
                    Description = "Supplemental event 200: %1",
                    Keywords = [],
                    Template = "<template><data name=\"Val\" inType=\"win:UnicodeString\" outType=\"xs:string\"/></template>"
                }
            ],
            Messages = [],
            Parameters = [],
            Keywords = new Dictionary<long, string>(),
            Tasks = new Dictionary<int, string>()
        };

        var resolver = new SupplementalTestResolver([primaryDetails], supplementalDetails);

        var eventRecord = new EventRecord
        {
            ProviderName = Constants.TestProviderName,
            Id = 200,
            Version = 0,
            LogName = Constants.ApplicationLogName,
            Properties = ["test_value"]
        };

        // Act
        var displayEvent = resolver.ResolveEvent(eventRecord);

        // Assert - should resolve from supplemental
        Assert.NotNull(displayEvent);
        Assert.Contains("Supplemental event 200: test_value", displayEvent.Description);
    }

    [Fact]
    public void ResolveEvent_WithTabSeparatedDataAttributes_ShouldCountAllElements()
    {
        // Arrange - template uses tabs instead of spaces after <data
        var (details, eventRecord) = EventUtils.CreateModernEvent(
            "Code: %1",
            "<template><data\tname=\"ErrorCode\"\toutType=\"win:HexInt32\"/></template>",
            [255]);

        var resolver = new TestEventResolver([details]);

        // Act
        var displayEvent = resolver.ResolveEvent(eventRecord);

        // Assert
        Assert.NotNull(displayEvent);
        Assert.Contains("0xFF", displayEvent.Description);
    }

    [Fact]
    public void ResolveEvent_WithTaskCategory_ShouldResolveTaskName()
    {
        // Arrange
        var providerDetails = new ProviderDetails
        {
            ProviderName = Constants.TestProviderName,
            Events =
            [
                new EventModel
                {
                    Id = 1000,
                    Version = 0,
                    Keywords = [],
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
            ProviderName = Constants.TestProviderName,
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
            ProviderName = Constants.TestProviderName,
            Events = [],
            Messages =
            [
                new MessageModel
                {
                    ProviderName = Constants.TestProviderName,
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
            ProviderName = Constants.TestProviderName,
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
    public void ResolveEvent_WithTemplateCountDiffOfOne_ShouldMatchExactIdVersionLogName()
    {
        // Arrange - template has 3 data nodes, event has 2 properties (diff = 1)
        // This mimics WER 1001 where manifest added an optional field
        var providerDetails = new ProviderDetails
        {
            ProviderName = Constants.TestProviderName,
            Events =
            [
                new EventModel
                {
                    Id = 1001,
                    Version = 0,
                    LogName = Constants.ApplicationLogName,
                    Description = "Fault: %1, Module: %2, Extra: %3",
                    Keywords = [],
                    Template = """
                        <template>
                          <data name="AppName" inType="win:UnicodeString" outType="xs:string"/>
                          <data name="ModName" inType="win:UnicodeString" outType="xs:string"/>
                          <data name="Extra" inType="win:UnicodeString" outType="xs:string"/>
                        </template>
                        """
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
            ProviderName = Constants.TestProviderName,
            Id = 1001,
            Version = 0,
            LogName = Constants.ApplicationLogName,
            Properties = ["MyApp.exe", "ntdll.dll"]
        };

        // Act
        var displayEvent = resolver.ResolveEvent(eventRecord);

        // Assert - should use the template even though event has 1 fewer property
        Assert.NotNull(displayEvent);
        Assert.Contains("Fault: MyApp.exe", displayEvent.Description);
        Assert.Contains("Module: ntdll.dll", displayEvent.Description);
    }

    [Fact]
    public void ResolveEvent_WithTrailingPercentN_ShouldNotThrowIndexOutOfRange()
    {
        // Arrange
        var providerDetails = new ProviderDetails
        {
            ProviderName = Constants.TestProviderName,
            Events = [],
            Messages =
            [
                new MessageModel
                {
                    ProviderName = Constants.TestProviderName,
                    ShortId = 1001,
                    Text = "Message ends with newline%n"
                }
            ],
            Parameters = [],
            Keywords = new Dictionary<long, string>(),
            Tasks = new Dictionary<int, string>()
        };

        var resolver = new TestEventResolver([providerDetails]);

        var eventRecord = new EventRecord
        {
            ProviderName = Constants.TestProviderName,
            Id = 1001,
            Properties = []
        };

        // Act - should not throw IndexOutOfRangeException
        var displayEvent = resolver.ResolveEvent(eventRecord);

        // Assert
        Assert.NotNull(displayEvent);
        Assert.EndsWith("\r\n", displayEvent.Description);
    }

    [Fact]
    public void ResolveEvent_WithUtcDateTime_ShouldFormatRoundTrip()
    {
        var (details, eventRecord) = EventUtils.CreateModernEvent(
            "Time: %1",
            """<template><data name="Time" inType="win:FILETIME"/></template>""",
            [new DateTime(2024, 1, 1, 12, 0, 0, DateTimeKind.Utc)]);

        var resolver = new TestEventResolver([details]);

        var displayEvent = resolver.ResolveEvent(eventRecord);

        Assert.NotNull(displayEvent);
        Assert.Contains("2024-01-01T12:00:00", displayEvent.Description);
    }

    [Fact]
    public void ResolveEvent_WithValueMap_ShouldDecodeEnumValueToName()
    {
        // Arrange - Ntfs BusType: the raw value 10 decodes to "SAS" via a valueMap.
        var (details, eventRecord) = EventUtils.CreateModernEvent(
            "Bus Type: %1",
            """
                <template>
                  <data name="BusType" inType="win:UInt32" outType="xs:unsignedInt" map="BusTypeMap"/>
                </template>
                """,
            [10u]);

        details.Maps = new Dictionary<string, ValueMapDefinition>
        {
            ["BusTypeMap"] = new ValueMapDefinition(
                isBitMap: false,
                entries: [new ValueMapEntry(10, "SAS")])
        };

        var resolver = new TestEventResolver([details]);

        // Act
        var displayEvent = resolver.ResolveEvent(eventRecord);

        // Assert
        Assert.NotNull(displayEvent);
        Assert.Contains("Bus Type: SAS", displayEvent.Description);
    }

    [Fact]
    public void ResolveEvent_WithXmlProperty_ShouldPreserveXml()
    {
        // Arrange
        var resolver = new TestEventResolver();
        var xmlContent = "<Event><System><EventID>1000</EventID></System></Event>";

        var eventRecord = new EventRecord
        {
            ProviderName = Constants.TestProviderName,
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
            ProviderName = Constants.TestProviderName,
            Id = 1000,
            Keywords = 0
        };

        // Act
        resolver.LoadProviderDetails(eventRecord);
        var displayEvent = resolver.ResolveEvent(eventRecord);

        // Assert
        Assert.NotNull(displayEvent);
        Assert.Empty(displayEvent.Keywords);
    }

    private class SupplementalTestResolver : EventResolverBase, IEventResolver
    {
        private readonly ProviderDetails? _supplemental;

        public SupplementalTestResolver(
            List<ProviderDetails> providerDetailsList,
            ProviderDetails? supplemental,
            IEventResolverCache? cache = null,
            ITraceLogger? logger = null)
            : base(cache, logger)
        {
            _supplemental = supplemental;
            providerDetailsList.ForEach(p => ProviderDetails.TryAdd(p.ProviderName, p));
        }

        public void LoadProviderDetails(EventRecord eventRecord)
        {
            if (ProviderDetails.ContainsKey(eventRecord.ProviderName))
            {
                return;
            }

            ProviderDetails.TryAdd(eventRecord.ProviderName, null);
        }

        public void SetMetadataPaths(IReadOnlyList<string> metadataPaths) => throw new NotImplementedException();

        protected override ProviderDetails? TryGetSupplementalDetails(EventRecord eventRecord) => _supplemental;
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
            providerDetailsList.ForEach(p => ProviderDetails.TryAdd(p.ProviderName, p));
        }

        public ConcurrentDictionary<string, ProviderDetails?> GetProviderDetails() => ProviderDetails;

        public void LoadProviderDetails(EventRecord eventRecord)
        {
            if (ProviderDetails.ContainsKey(eventRecord.ProviderName))
            {
                return;
            }

            ProviderDetails.TryAdd(eventRecord.ProviderName, null);
        }

        public void SetMetadataPaths(IReadOnlyList<string> metadataPaths) => throw new NotImplementedException();
    }
}
