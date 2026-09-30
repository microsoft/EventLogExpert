// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Eventing.Common.Channels;
using EventLogExpert.Eventing.Readers;
using EventLogExpert.Eventing.Resolvers;
using EventLogExpert.Eventing.Tests.TestUtils;
using EventLogExpert.Provider.Resolution;
using System.Collections.Concurrent;
using System.Globalization;

namespace EventLogExpert.Eventing.Tests.Resolvers;

[Collection(CultureSensitiveCollection.Name)]
public sealed class DescriptionFormatterCultureTests
{
    [Fact]
    public void ResolveEvent_DateTimeInsertStaysFixedInvariantAcrossCultures()
    {
        var details = new ProviderDetails
        {
            ProviderName = "ProviderA",
            Events =
            [
                new EventModel
                {
                    Id = 10,
                    Version = 0,
                    LogName = "Application",
                    Description = "Seen %1",
                    Keywords = [],
                    Template = "<template><data name=\"SeenAt\" inType=\"win:FILETIME\"/></template>"
                }
            ],
            Messages = [],
            Parameters = [],
            Keywords = new Dictionary<long, string>(),
            Opcodes = new Dictionary<int, string>(),
            Tasks = new Dictionary<int, string>()
        };
        var eventRecord = new EventRecord
        {
            ProviderName = "ProviderA",
            Id = 10,
            Version = 0,
            LogName = "Application",
            LogPathType = LogPathType.Channel,
            Properties = [new DateTime(2026, 8, 26, 17, 57, 5, 123, DateTimeKind.Utc).AddTicks(4567)]
        };
        var resolver = new TestEventResolver([details]);

        string english = RunUnderCulture(CultureInfo.GetCultureInfo("en-US"), () => resolver.ResolveEvent(eventRecord).Description);
        string arabic = RunUnderCulture(CultureInfo.GetCultureInfo("ar-SA"), () => resolver.ResolveEvent(eventRecord).Description);

        Assert.Equal(english, arabic);
        Assert.Contains("2026-08-26T17:57:05.123456700Z", english, StringComparison.Ordinal);
    }

    private static string RunUnderCulture(CultureInfo culture, Func<string> build)
    {
        CultureInfo priorCulture = CultureInfo.CurrentCulture;
        CultureInfo priorUiCulture = CultureInfo.CurrentUICulture;

        try
        {
            CultureInfo.CurrentCulture = culture;
            CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo("en");
            return build();
        }
        finally
        {
            CultureInfo.CurrentCulture = priorCulture;
            CultureInfo.CurrentUICulture = priorUiCulture;
        }
    }

    private sealed class TestEventResolver : EventResolverBase, IEventResolver
    {
        public TestEventResolver(List<ProviderDetails> providerDetailsList)
        {
            providerDetailsList.ForEach(providerDetails => ProviderDetails.TryAdd(providerDetails.ProviderName, providerDetails));
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
