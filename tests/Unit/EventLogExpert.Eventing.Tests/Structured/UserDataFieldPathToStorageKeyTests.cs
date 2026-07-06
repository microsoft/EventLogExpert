// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Eventing.Structured;

namespace EventLogExpert.Eventing.Tests.Structured;

// Guards the canonical-path -> storage-key normalization the emitter and resolver both depend on: the exact two-element
// Event/UserData envelope drop, the [*] strip, the attribute suffix, and idempotency are all load-bearing.
public sealed class UserDataFieldPathToStorageKeyTests
{
    // Full store-key round-trip: a store key must survive TryNormalize (re-rooting) then ToStorageKey (envelope drop)
    // back to itself, so a picker-created filter looks up exactly the stored field - even a key starting "Event"/"UserData".
    [Theory]
    [InlineData("X509Objects/Certificate/SubjectName")]
    [InlineData("X509Objects/Certificate/@subjectName")]
    [InlineData("Operation")]
    [InlineData("Event/MyPayload")]
    [InlineData("UserData/Something")]
    public void StoreKey_RoundTripsThroughNormalizeAndBack(string storageKey)
    {
        Assert.True(UserDataFieldPath.TryNormalize(storageKey, out string? canonical, out string? error), error);
        Assert.Equal(storageKey, UserDataFieldPath.ToStorageKey(canonical!));
    }

    [Fact]
    public void ToStorageKey_AlreadyPlainPickerKey_PassesThroughUnchanged()
    {
        Assert.Equal(
            "X509Objects/Certificate/SubjectName",
            UserDataFieldPath.ToStorageKey("X509Objects/Certificate/SubjectName"));
    }

    [Fact]
    public void ToStorageKey_AttributeValue_KeepsAttributeSuffix()
    {
        Assert.Equal(
            "X509Objects/Certificate/@subjectName",
            UserDataFieldPath.ToStorageKey("Event/UserData/X509Objects/Certificate/@subjectName"));
    }

    [Fact]
    public void ToStorageKey_FullyRootedPath_DropsEventUserDataEnvelope()
    {
        Assert.Equal(
            "X509Objects/Certificate/SubjectName",
            UserDataFieldPath.ToStorageKey("Event/UserData/X509Objects/Certificate/SubjectName"));
    }

    [Fact]
    public void ToStorageKey_IsIdempotent()
    {
        string once = UserDataFieldPath.ToStorageKey("Event/UserData/X509Objects/Certificate/@subjectName");

        Assert.Equal(once, UserDataFieldPath.ToStorageKey(once));
    }

    [Fact]
    public void ToStorageKey_SingleElementUnderEnvelope_ReturnsThatElement()
    {
        Assert.Equal("Operation", UserDataFieldPath.ToStorageKey("Event/UserData/Operation"));
    }

    // The exact two-element match (not a per-element "strip Event") protects an unrooted payload whose first element is
    // literally "Event": a sequential strip would corrupt it to "MyPayload".
    [Fact]
    public void ToStorageKey_UnrootedPayloadNamedEvent_IsNotCorrupted()
    {
        Assert.Equal("Event/MyPayload", UserDataFieldPath.ToStorageKey("Event/MyPayload"));
    }

    [Fact]
    public void ToStorageKey_WildcardMarker_IsStripped()
    {
        Assert.Equal(
            "Certificate/SubjectName",
            UserDataFieldPath.ToStorageKey("Event/UserData/Certificate[*]/SubjectName"));
    }
}
