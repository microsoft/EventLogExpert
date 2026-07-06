// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Eventing.Structured;

namespace EventLogExpert.Eventing.Tests.Structured;

public sealed class StructuredSchemaDiscoveryTests
{
    private const string Ns = "xmlns='http://schemas.microsoft.com/win/2004/08/events/event'";

    [Fact]
    public void DiscoverUserDataPaths_AttributeAndTextLeaves_AreEmitted()
    {
        string xml = $"<Event {Ns}><UserData><Root><Cert subjectName='x'/><Status>ok</Status></Root></UserData></Event>";

        var paths = StructuredSchemaDiscovery.DiscoverUserDataPaths([xml]);

        Assert.Contains("Event/UserData/Root/Cert/@subjectName", paths);
        Assert.Contains("Event/UserData/Root/Status", paths);
    }

    [Fact]
    public void DiscoverUserDataPaths_EventWithoutUserData_ReturnsEmpty()
    {
        string xml = $"<Event {Ns}><System><EventID>1</EventID></System><EventData><Data Name='A'>v</Data></EventData></Event>";

        var paths = StructuredSchemaDiscovery.DiscoverUserDataPaths([xml]);

        Assert.Empty(paths);
    }

    [Fact]
    public void DiscoverUserDataPaths_IgnoresXmlnsDeclarationAttributes()
    {
        string xml = $"<Event {Ns}><UserData><Root xmlns:e='urn:x'><Cert subjectName='x'/></Root></UserData></Event>";

        var paths = StructuredSchemaDiscovery.DiscoverUserDataPaths([xml]);

        Assert.DoesNotContain(paths, path => path.Contains("xmlns", StringComparison.Ordinal));
        Assert.Contains("Event/UserData/Root/Cert/@subjectName", paths);
    }

    [Fact]
    public void DiscoverUserDataPaths_MalformedXmlSample_IsSkipped()
    {
        string good = $"<Event {Ns}><UserData><Root><Cert subjectName='x'/></Root></UserData></Event>";
        const string malformed = "<Event><UserData><unclosed>";

        var paths = StructuredSchemaDiscovery.DiscoverUserDataPaths([malformed, good]);

        Assert.Contains("Event/UserData/Root/Cert/@subjectName", paths);
    }

    [Fact]
    public void DiscoverUserDataPaths_RepeatingElementInOneSample_EmitsWildcard()
    {
        string xml = $"<Event {Ns}><UserData><Root><Item value='a'/><Item value='b'/></Root></UserData></Event>";

        var paths = StructuredSchemaDiscovery.DiscoverUserDataPaths([xml]);

        Assert.Contains("Event/UserData/Root/Item[*]/@value", paths);
        Assert.DoesNotContain("Event/UserData/Root/Item/@value", paths);
    }

    [Fact]
    public void DiscoverUserDataPaths_RepeatingInAnySample_PromotesUnionToWildcard()
    {
        // Sample 1 shows a single Certificate; sample 2 shows two. Any sample with >=2 siblings promotes the
        // canonical path to [*], and the scalar form must NOT survive in the union.
        string single = $"<Event {Ns}><UserData><Root><Certificate subjectName='one'/></Root></UserData></Event>";
        string repeated = $"<Event {Ns}><UserData><Root><Certificate subjectName='two'/><Certificate subjectName='three'/></Root></UserData></Event>";

        var paths = StructuredSchemaDiscovery.DiscoverUserDataPaths([single, repeated]);

        Assert.Contains("Event/UserData/Root/Certificate[*]/@subjectName", paths);
        Assert.DoesNotContain("Event/UserData/Root/Certificate/@subjectName", paths);
    }

    [Fact]
    public void DiscoverUserDataPaths_ResultIsOrdered()
    {
        string xml = $"<Event {Ns}><UserData><Root><Zeta z='1'/><Alpha a='2'/></Root></UserData></Event>";

        var paths = StructuredSchemaDiscovery.DiscoverUserDataPaths([xml]);

        Assert.Equal(paths.OrderBy(path => path, StringComparer.Ordinal), paths);
    }

    [Fact]
    public void DiscoverUserDataPaths_UnionsDistinctLeavesAcrossSamples()
    {
        string first = $"<Event {Ns}><UserData><Root><A x='1'/></Root></UserData></Event>";
        string second = $"<Event {Ns}><UserData><Root><B y='2'/></Root></UserData></Event>";

        var paths = StructuredSchemaDiscovery.DiscoverUserDataPaths([first, second]);

        Assert.Contains("Event/UserData/Root/A/@x", paths);
        Assert.Contains("Event/UserData/Root/B/@y", paths);
    }

    [Fact]
    public void DiscoverUserDataPaths_WhitespaceOnlySamples_ReturnEmpty()
    {
        var paths = StructuredSchemaDiscovery.DiscoverUserDataPaths(["   ", "\n\t"]);

        Assert.Empty(paths);
    }
}
