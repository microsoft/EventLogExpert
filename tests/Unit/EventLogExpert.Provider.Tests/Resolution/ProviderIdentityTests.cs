// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Provider.Resolution;

namespace EventLogExpert.Provider.Tests.Resolution;

public sealed class ProviderIdentityTests
{
    [Fact]
    public void DefaultInstance_IsInert_AndDoesNotThrow()
    {
        // A default instance has null members; equality and hashing are null-safe, so it is inert (usable in a set
        // without throwing) even though it does not correspond to any real provider.
        var first = default(ProviderIdentity);
        var second = default(ProviderIdentity);

        Assert.Equal(first, second);
        Assert.Equal(first.GetHashCode(), second.GetHashCode());
        Assert.NotEqual(first, new ProviderIdentity(string.Empty, string.Empty));
        Assert.Contains(first, new HashSet<ProviderIdentity> { second });
    }

    [Fact]
    public void Equals_DistinctVersionKeys_AreNotEqual()
    {
        var first = new ProviderIdentity("Foo", "vk1");
        var second = new ProviderIdentity("Foo", "vk2");

        Assert.NotEqual(first, second);
    }

    [Fact]
    public void Equals_NameDiffersOnlyByCase_AreEqual()
    {
        var upper = new ProviderIdentity("Foo", "");
        var lower = new ProviderIdentity("foo", "");

        Assert.Equal(upper, lower);
        Assert.True(upper == lower);
        Assert.Equal(upper.GetHashCode(), lower.GetHashCode());
    }

    [Fact]
    public void Equals_VersionKeyDiffersByCase_AreNotEqual()
    {
        // VersionKey is compared ordinally (it is an opaque content hash), so case matters for the version
        // even though it does not for the name.
        var lower = new ProviderIdentity("Foo", "a");
        var upper = new ProviderIdentity("Foo", "A");

        Assert.NotEqual(lower, upper);
        Assert.True(lower != upper);
    }

    [Fact]
    public void HashSet_UsesBakedInCollation_WithoutAnExplicitComparer()
    {
        // The struct's overridden equality means a default HashSet dedups case-insensitively on name and
        // ordinally on version key, with no comparer threaded through the call site.
        var set = new HashSet<ProviderIdentity>
        {
            new("Foo", ""),
            new("foo", ""),
            new("Foo", "vk2")
        };

        Assert.Equal(2, set.Count);
        Assert.Contains(new ProviderIdentity("FOO", ""), set);
    }

    [Fact]
    public void Of_ExtractsNameAndVersionKey()
    {
        var provider = new ProviderDetails { ProviderName = "Foo", VersionKey = "vk1" };

        var identity = ProviderIdentity.Of(provider);

        Assert.Equal(new ProviderIdentity("Foo", "vk1"), identity);
    }
}
