// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Runtime.StatusBar;
using EventLogExpert.UI.Common;
using EventLogExpert.UI.Tests.TestUtils;

namespace EventLogExpert.UI.Tests.StatusBar;

public sealed class ResolverStatusLocalizerTests
{
    private static readonly (ResolverStatusReason Reason, string LogDescription, string ExpectedKey)[] s_keyedReasonCases =
    [
        (ResolverStatusReason.FailedToOpen, "X", "StatusBar_Resolver_FailedToOpen"),
        (ResolverStatusReason.NoResolver, string.Empty, "StatusBar_Resolver_NoResolver"),
        (ResolverStatusReason.FailedToLoad, "Y", "StatusBar_Resolver_FailedToLoad")
    ];

    private readonly MarkerLocalizer _localizer = new();

    public static TheoryData<ResolverStatusReason, string, string> KeyedReasons()
    {
        TheoryData<ResolverStatusReason, string, string> data = new();

        foreach ((ResolverStatusReason reason, string logDescription, string expectedKey) in s_keyedReasonCases)
        {
            data.Add(reason, logDescription, expectedKey);
        }

        return data;
    }

    [Fact]
    public void DescribeCore_UnknownReason_Throws() =>
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            ResolverStatusLocalizer.DescribeCore(_localizer, (ResolverStatusReason)999, null));

    [Fact]
    public void Describe_None_ReturnsEmptyString() =>
        Assert.Equal(string.Empty, ResolverStatusLocalizer.Describe(_localizer, ResolverStatus.None));

    [Theory]
    [MemberData(nameof(KeyedReasons))]
    public void Describe_RoutesEveryKeyBearingReasonToItsOwnKey(
        ResolverStatusReason reason,
        string logDescription,
        string expectedKey)
    {
        ResolverStatus status = CreateResolverStatus(reason, logDescription);
        string expected = reason is ResolverStatusReason.NoResolver ?
            $"[[{expectedKey}]]" :
            $"[[{expectedKey}({logDescription})]]";

        Assert.Equal(expected, ResolverStatusLocalizer.Describe(_localizer, status));
    }

    [Fact]
    public void KeyedReasons_CoverEveryNonNoneReason()
    {
        ResolverStatusReason[] nonNoneReasons = Enum.GetValues<ResolverStatusReason>()
            .Where(reason => reason != ResolverStatusReason.None)
            .OrderBy(reason => reason)
            .ToArray();

        ResolverStatusReason[] keyedReasons = s_keyedReasonCases
            .Select(mapping => mapping.Reason)
            .OrderBy(reason => reason)
            .ToArray();

        Assert.Equal(nonNoneReasons, keyedReasons);
    }

    private static ResolverStatus CreateResolverStatus(ResolverStatusReason reason, string logDescription) =>
        reason switch
        {
            ResolverStatusReason.FailedToOpen => ResolverStatus.FailedToOpen(logDescription),
            ResolverStatusReason.NoResolver => ResolverStatus.NoResolver,
            ResolverStatusReason.FailedToLoad => ResolverStatus.FailedToLoad(logDescription),
            _ => throw new ArgumentOutOfRangeException(nameof(reason), reason, null)
        };
}
