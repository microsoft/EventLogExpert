// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Eventing.Readers;
using EventLogExpert.Eventing.Structured;
using System.Runtime.CompilerServices;

namespace EventLogExpert.Eventing.Tests.Structured;

public sealed class RetainedBytesBudgetTests
{
    private const string Ns = "xmlns='http://schemas.microsoft.com/win/2004/08/events/event'";

    // The distinct-path cap actually drops paths (and flags the event incomplete) so the retained struct count can
    // never exceed the cap, no matter how many distinct paths the event carries.
    [Fact]
    public void ExtractedUserData_NeverRetainsMorePathStructsThanTheCap()
    {
        string leaves = string.Concat(Enumerable.Range(0, 10).Select(index => $"<F{index}>v</F{index}>"));
        string xml = $"<Event {Ns}><UserData><Root>{leaves}</Root></UserData></Event>";

        var (fields, incomplete) = UserDataValueExtractor.Extract(xml, maxDistinctPaths: 4);

        Assert.True(incomplete);
        Assert.True(fields.Length <= 4, $"retained {fields.Length} path structs above the cap of 4.");
    }

    // Dedup lever: repeats of a path collapse into one multi-value field, so the retained path-struct array is sized by
    // DISTINCT paths, not raw leaf count. A certificate chain with many repeated leaves must not retain one struct per
    // leaf.
    [Fact]
    public void ExtractedUserData_RetainedPathStructs_ScaleWithDistinctPathsNotRawLeaves()
    {
        const int repeats = 200;
        string leaves = string.Concat(
            Enumerable.Range(0, repeats).Select(index => $"<Cert><Thumbprint>{index:X4}</Thumbprint></Cert>"));
        string xml =
            $"<Event {Ns}><UserData><Chain>{leaves}</Chain><Result>0</Result><Issuer>CA</Issuer></UserData></Event>";

        var (fields, incomplete) = UserDataValueExtractor.Extract(xml);

        Assert.False(incomplete);

        // The 200 repeated <Thumbprint> leaves collapse to one path; two more distinct paths -> 3 retained structs, not 202.
        Assert.Equal(3, fields.Length);
        Assert.Equal(repeats, Assert.Single(fields, field => field.Path == "Chain/Cert/Thumbprint").Values.Length);

        long retainedPathStructBytes = (long)fields.Length * Unsafe.SizeOf<UserDataField>();
        long rawLeafStructBytes = (long)(repeats + 2) * Unsafe.SizeOf<UserDataField>();
        Assert.True(
            retainedPathStructBytes * 10 < rawLeafStructBytes,
            $"dedup did not bound retained path structs: {retainedPathStructBytes} B retained vs {rawLeafStructBytes} B raw.");
    }

    // Caps lever: a pathological event cannot retain unbounded UserData. The distinct-path cap bounds the path-struct
    // array, the per-field value cap bounds a field's values, and the per-value char cap bounds a single value. Locks
    // each ceiling so none regresses to effectively unbounded, and asserts the worst-case retained path-struct ceiling
    // they imply stays within budget.
    [Fact]
    public void RetentionCaps_BoundWorstCasePerEvent()
    {
        Assert.InRange(UserDataValueExtractor.MaxDistinctPathsPerEvent, 1, 4096);
        Assert.InRange(StructuredFieldPath.MaxWildcardValues, 1, 4096);
        Assert.InRange(UserDataValueExtractor.MaxValueChars, 1, 65536);

        long pathStructCeiling =
            (long)UserDataValueExtractor.MaxDistinctPathsPerEvent * Unsafe.SizeOf<UserDataField>();
        Assert.True(
            pathStructCeiling <= 64 * 1024,
            $"per-event path-struct retained ceiling {pathStructCeiling} B exceeds the 64 KB budget.");
    }

    // Per-field retained cost, multiplied across every retained event. EventData stores one EventProperty per field
    // (a 16-byte packed tagged scalar, no boxing); UserData stores one UserDataField per distinct path (a string ref +
    // an ImmutableArray ref + a bool = 24 bytes). Adding a field to either type is the most likely silent regression,
    // so both sizes are budgeted here.
    [Fact]
    public void StoreModel_PerFieldRetainedStructs_AreWithinBudget()
    {
        Assert.Equal(16, Unsafe.SizeOf<EventProperty>());
        Assert.True(
            Unsafe.SizeOf<UserDataField>() <= 24,
            $"UserDataField retained struct grew to {Unsafe.SizeOf<UserDataField>()} bytes (budget 24).");
    }
}
