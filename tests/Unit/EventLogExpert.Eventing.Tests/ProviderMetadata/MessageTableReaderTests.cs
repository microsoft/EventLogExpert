// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Eventing.ProviderMetadata;
using EventLogExpert.Provider.Resolution;
using System.Runtime.InteropServices;
using System.Text;

namespace EventLogExpert.Eventing.Tests.ProviderMetadata;

public sealed class MessageTableReaderTests
{
    [Fact]
    public void AppendMatches_AppendsEveryEntryInWalkOrder_WhenFilterIsNegative()
    {
        byte[] table = BuildTable((5, [("five", false), ("six", true)]));

        WithTable(table, (memTable, size) =>
        {
            var into = new List<MessageModel>();
            MessageTableReader.AppendMatches(memTable, size, "P", -1, into);

            Assert.Equal(2, into.Count);
            Assert.Equal("five", into[0].Text);
            Assert.Equal(5, into[0].RawId);
            Assert.Equal("six", into[1].Text);
            Assert.Equal(6, into[1].RawId);
        });
    }

    [Fact]
    public void AppendMatches_ReturnsAllLow16Collisions_InBlockOrder()
    {
        // Two ids whose low 16 bits are both 5 (5 and 0x10005), in separate blocks - the qualifier/severity case the
        // eager store keys by (ushort)ShortId. GetByShortId(5) must return both, in walk order.
        byte[] table = BuildTable(
            (5, [("low", false)]),
            (0x10005, [("high", false)]));

        WithTable(table, (memTable, size) =>
        {
            var into = new List<MessageModel>();
            MessageTableReader.AppendMatches(memTable, size, "P", 5, into);

            Assert.Equal(["low", "high"], into.Select(m => m.Text));
            Assert.Equal([5L, 0x10005L], into.Select(m => m.RawId));
        });
    }

    [Fact]
    public void CountEntries_CountsEveryEntryAcrossBlocks()
    {
        byte[] table = BuildTable(
            (10, [("ten", false), ("eleven", false)]),
            (100, [("hundred", false)]));

        WithTable(table, (memTable, size) => Assert.Equal(3, MessageTableReader.CountEntries(memTable, size)));
    }

    [Fact]
    public void CountEntries_ReturnsNegative_WhenOffsetIsOutOfBounds()
    {
        byte[] table = BuildTable((1, [("one", false)]));

        // Corrupt the first block's OffsetToEntries (bytes 12-15) to point past the resource.
        BitConverter.GetBytes(0x7FFF_FFFF).CopyTo(table, 12);

        WithTable(table, (memTable, size) => Assert.Equal(-1, MessageTableReader.CountEntries(memTable, size)));
    }

    [Fact]
    public void FindFirstByRawId_ReturnsExactRawIdMatch()
    {
        byte[] table = BuildTable((5, [("five", false)]), (0x10005, [("high", false)]));

        WithTable(table, (memTable, size) =>
        {
            Assert.Equal("high", MessageTableReader.FindFirstByRawId(memTable, size, 0x10005, "P")?.Text);
            Assert.Null(MessageTableReader.FindFirstByRawId(memTable, size, 7, "P"));
        });
    }

    [Fact]
    public void LazySource_MaterializeAll_MatchesEagerLoadOnRealProviderDll()
    {
        string dll = Path.Combine(Environment.SystemDirectory, "netmsg.dll");
        Assert.True(File.Exists(dll));

        var eager = EventMessageProvider.LoadMessagesFromFiles([dll], "TestProvider");
        Assert.NotEmpty(eager);

        var lazy = new LegacyMessageFileSource([dll], "TestProvider", eager.Count, null);

        Assert.Equal(eager.Count, lazy.Count);

        var materialized = lazy.MaterializeAll();
        Assert.Equal(eager.Count, materialized.Count);
        for (int i = 0; i < eager.Count; i++)
        {
            Assert.Equal(eager[i].ShortId, materialized[i].ShortId);
            Assert.Equal(eager[i].RawId, materialized[i].RawId);
            Assert.Equal(eager[i].Text, materialized[i].Text);
        }

        // Per-id extraction must reproduce the eager store's by-ShortId and first-by-RawId lookups exactly.
        foreach (var shortId in eager.Select(m => (int)(ushort)m.ShortId).Distinct().Take(50))
        {
            var expected = eager.Where(m => (ushort)m.ShortId == shortId).Select(m => m.Text);
            Assert.Equal(expected, lazy.GetByShortId(shortId).Select(m => m.Text));
        }

        foreach (var rawId in eager.Select(m => m.RawId).Distinct().Take(50))
        {
            var expected = eager.First(m => m.RawId == rawId);
            Assert.Equal(expected.Text, lazy.GetByRawIdFirst(rawId)?.Text);
        }
    }

    [Fact]
    public void LegacyMessageFileSource_SkipsUnloadableFiles_AndConcatenatesFilesInOrder()
    {
        string dll = Path.Combine(Environment.SystemDirectory, "netmsg.dll");
        string missing = Path.Combine(Path.GetTempPath(), $"does-not-exist-{Guid.NewGuid():N}.dll");

        var single = EventMessageProvider.LoadMessagesFromFiles([dll], "P");
        Assert.NotEmpty(single);

        // An unloadable file is skipped, not fatal.
        var skipped = new LegacyMessageFileSource([missing, dll], "P", single.Count, null).MaterializeAll();
        Assert.Equal(single.Select(m => m.Text), skipped.Select(m => m.Text));

        // Multiple files concatenate in order: the same file twice yields its entries twice, file1 before file2.
        var doubled = new LegacyMessageFileSource([dll, dll], "P", single.Count * 2, null).MaterializeAll();
        Assert.Equal(single.Count * 2, doubled.Count);
        Assert.Equal(single[0].Text, doubled[0].Text);
        Assert.Equal(single[0].Text, doubled[single.Count].Text);
    }

    [Fact]
    public void TryOpen_ReturnsFalse_WhenFileIsNotAResourceModule()
    {
        string temp = Path.Combine(Path.GetTempPath(), $"not-a-dll-{Guid.NewGuid():N}.txt");
        File.WriteAllText(temp, "not a PE file");

        try
        {
            Assert.False(MessageTableReader.TryOpen(temp, null, out var handle, out _, out _));
            handle.Dispose();
        }
        finally { File.Delete(temp); }
    }

    // Builds a MESSAGE_RESOURCE_DATA blob: a block count, then one MESSAGE_RESOURCE_BLOCK per block (LowId, HighId,
    // OffsetToEntries), then the variable-length entries. Each block's ids run lowId..lowId+entries.Length-1.
    private static byte[] BuildTable(params (int lowId, (string text, bool unicode)[] entries)[] blocks)
    {
        int headerSize = 4 + (12 * blocks.Length);
        var entryBytes = new List<byte>();
        var offsets = new int[blocks.Length];

        for (int b = 0; b < blocks.Length; b++)
        {
            offsets[b] = headerSize + entryBytes.Count;

            foreach (var (text, unicode) in blocks[b].entries)
            {
                byte[] encoded = unicode
                    ? Encoding.Unicode.GetBytes(text + '\0')
                    : Encoding.ASCII.GetBytes(text + '\0');

                short length = (short)(4 + encoded.Length);
                entryBytes.AddRange(BitConverter.GetBytes(length));
                entryBytes.AddRange(BitConverter.GetBytes((short)(unicode ? 1 : 0)));
                entryBytes.AddRange(encoded);
            }
        }

        var result = new List<byte>();
        result.AddRange(BitConverter.GetBytes(blocks.Length));

        for (int b = 0; b < blocks.Length; b++)
        {
            result.AddRange(BitConverter.GetBytes(blocks[b].lowId));
            result.AddRange(BitConverter.GetBytes(blocks[b].lowId + blocks[b].entries.Length - 1));
            result.AddRange(BitConverter.GetBytes(offsets[b]));
        }

        result.AddRange(entryBytes);

        return [.. result];
    }

    private static void WithTable(byte[] table, Action<nint, uint> action)
    {
        var handle = GCHandle.Alloc(table, GCHandleType.Pinned);

        try { action(handle.AddrOfPinnedObject(), (uint)table.Length); }
        finally { handle.Free(); }
    }
}
