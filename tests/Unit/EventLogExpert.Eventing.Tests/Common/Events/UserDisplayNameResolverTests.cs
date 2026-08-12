// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Eventing.Common.Events;
using EventLogExpert.Eventing.Readers;
using EventLogExpert.Eventing.Resolvers;
using System.Collections.Immutable;
using System.Security.Principal;

namespace EventLogExpert.Eventing.Tests.Common.Events;

public sealed class UserDisplayNameResolverTests
{
    private static readonly SecurityIdentifier s_userSid = new("S-1-5-21-1111111111-2222222222-3333333333-1105");

    [Fact]
    public void Resolve_EmptyUserId_OnlyTargetPair_ReturnsTarget()
    {
        // 4768/4769-shaped: only Target identity present.
        EventDataView data = EventData(
            ("TargetUserName", "bob"),
            ("TargetDomainName", "CONTOSO"));

        Assert.Equal(@"CONTOSO\bob", UserDisplayNameResolver.Resolve(null, data));
    }

    [Fact]
    public void Resolve_EmptyUserId_SubjectNoDomain_ReturnsNameOnly()
    {
        EventDataView data = EventData(("SubjectUserName", "alice"));

        Assert.Equal("alice", UserDisplayNameResolver.Resolve(null, data));
    }

    [Fact]
    public void Resolve_EmptyUserId_SubjectPair_ReturnsDomainUser()
    {
        EventDataView data = EventData(
            ("SubjectUserName", "alice"),
            ("SubjectDomainName", "CONTOSO"));

        Assert.Equal(@"CONTOSO\alice", UserDisplayNameResolver.Resolve(null, data));
    }

    [Fact]
    public void Resolve_InformativeSubject_PreferredOverTarget()
    {
        EventDataView data = EventData(
            ("SubjectUserName", "alice"),
            ("SubjectDomainName", "CONTOSO"),
            ("TargetUserName", "bob"),
            ("TargetDomainName", "FABRIKAM"));

        Assert.Equal(@"CONTOSO\alice", UserDisplayNameResolver.Resolve(null, data));
    }

    [Fact]
    public void Resolve_NoIdentityFields_ReturnsEmpty()
    {
        EventDataView data = EventData(("SomeOtherField", "x"), ("Param1", "y"));

        Assert.Equal(string.Empty, UserDisplayNameResolver.Resolve(null, data));
    }

    [Fact]
    public void Resolve_NonWellKnownUserId_NoEventData_ReturnsRawSid() =>
        Assert.Equal(s_userSid.Value, UserDisplayNameResolver.Resolve(s_userSid, EventDataView.Empty));

    [Fact]
    public void Resolve_NonWellKnownUserId_WithSubjectPair_PrefersDerivedName()
    {
        EventDataView data = EventData(
            ("SubjectUserSid", s_userSid.Value),
            ("SubjectUserName", "alice"),
            ("SubjectDomainName", "CONTOSO"));

        Assert.Equal(@"CONTOSO\alice", UserDisplayNameResolver.Resolve(s_userSid, data));
    }

    [Fact]
    public void Resolve_NullUserId_NoEventData_ReturnsEmpty() =>
        Assert.Equal(string.Empty, UserDisplayNameResolver.Resolve(null, EventDataView.Empty));

    [Fact]
    public void Resolve_PlaceholderDashSubject_SkippedForTarget()
    {
        EventDataView data = EventData(
            ("SubjectUserName", "-"),
            ("SubjectDomainName", "-"),
            ("TargetUserName", "bob"),
            ("TargetDomainName", "CONTOSO"));

        Assert.Equal(@"CONTOSO\bob", UserDisplayNameResolver.Resolve(null, data));
    }

    [Theory]
    [InlineData("SYSTEM", "NT AUTHORITY")]
    [InlineData("LOCAL SERVICE", "NT AUTHORITY")]
    [InlineData("DC01$", "CONTOSO")]
    public void Resolve_SubjectIsSystemOrMachine_FallsBackToTarget(string subjectName, string subjectDomain)
    {
        // The reporting SYSTEM / machine subject on a logon event should yield to the logged-on Target user.
        EventDataView data = EventData(
            ("SubjectUserName", subjectName),
            ("SubjectDomainName", subjectDomain),
            ("TargetUserName", "bob"),
            ("TargetDomainName", "CONTOSO"));

        Assert.Equal(@"CONTOSO\bob", UserDisplayNameResolver.Resolve(null, data));
    }

    [Fact]
    public void Resolve_SubjectIsSystem_NoTarget_ShowsSubject()
    {
        // A process genuinely running as SYSTEM (no Target) legitimately displays the Subject.
        EventDataView data = EventData(
            ("SubjectUserName", "SYSTEM"),
            ("SubjectDomainName", "NT AUTHORITY"));

        Assert.Equal(@"NT AUTHORITY\SYSTEM", UserDisplayNameResolver.Resolve(null, data));
    }

    [Theory]
    [InlineData("S-1-5-18", @"NT AUTHORITY\SYSTEM")]
    [InlineData("S-1-5-19", @"NT AUTHORITY\LOCAL SERVICE")]
    [InlineData("S-1-5-20", @"NT AUTHORITY\NETWORK SERVICE")]
    [InlineData("S-1-0-0", "NULL SID")]
    [InlineData("S-1-5-32-544", @"BUILTIN\Administrators")]
    public void Resolve_WellKnownUserId_ReturnsMappedName(string sid, string expected) =>
        Assert.Equal(expected, UserDisplayNameResolver.Resolve(new SecurityIdentifier(sid), EventDataView.Empty));

    private static EventDataView EventData(params (string Name, string Value)[] fields)
    {
        string template = "<template>" +
            string.Concat(fields.Select(field => $"<data name=\"{field.Name}\"/>")) +
            "</template>";

        TemplateFieldSchema schema = new TemplateAnalyzer().GetTemplateInfo(template).Schema;
        ImmutableArray<EventProperty> values = [.. fields.Select(field => (EventProperty)field.Value)];

        return new EventDataView(values, schema);
    }
}
