// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Eventing.PublisherMetadata.Offline;
using Microsoft.Win32;
using System.Buffers.Binary;

namespace EventLogExpert.Eventing.Tests.PublisherMetadata.Offline;

/// <summary>
///     Exercises the managed <c>regf</c> parser (<see cref="OfflineHiveFile" />) directly: value-kind decoding and
///     boxed-type parity with the live registry, key navigation and physical leaf order, and - because the hive is treated
///     as hostile, attacker-controlled image content - that arbitrarily corrupted bytes degrade to <see langword="null" />
///     /empty rather than throwing or looping. Real hives are materialized through <see cref="OfflineTestImage" /> (which
///     loads and seeds a standalone hive via <c>RegLoadAppKey</c>); hostile cases mutate a real hive's bytes and re-open
///     them in memory.
/// </summary>
public sealed class OfflineHiveFileTests
{
    private const string TestKeyPath = @"Test\Values";

    [Fact]
    public void Enumeration_WithEveryNkClaimingHugeSubkeyCountAndWildListOffset_TerminatesWithoutThrowing()
    {
        // Drive the over-cap / out-of-bounds-offset guards directly: rewrite every nk record to claim int.MaxValue subkeys
        // pointing at a wild list offset. The parser must reject the absurd count and the unreachable offset and finish.
        byte[] bytes = SeedRealHiveBytes();

        for (int i = 0x1000; i + 0x20 < bytes.Length; i++)
        {
            if (bytes[i] == 0x6E && bytes[i + 1] == 0x6B) // "nk"
            {
                BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(i + 0x14), int.MaxValue);     // subkey count
                BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(i + 0x1C), 0x0FFF_FFF0u);    // subkey-list offset
            }
        }

        OfflineHiveFile? hive = OfflineHiveFile.TryOpen(bytes, logger: null);

        if (hive is null) { return; } // acceptable: the root nk corruption may have made the base block unreadable.

        using (hive)
        {
            try
            {
                _ = hive.GetSubKeyNames();
                _ = hive.OpenSubKey("Anything");
            }
            catch (Exception ex) when (ex is not OutOfMemoryException and not StackOverflowException)
            {
                Assert.Fail($"Corrupted nk records made the parser throw: {ex}");
            }
        }
    }

    [Fact]
    public void GetSubKeyNames_DecodesBothAsciiCompressedAndUnicodeKeyNames()
    {
        // The registry compresses all-ASCII names (Latin-1, flag bit set) but stores names with any non-ASCII character as
        // UTF-16; the parser must decode both code paths.
        using OfflineTestImage image = OfflineTestImage.Create(seedSoftware: software =>
        {
            using (software.CreateSubKey(@"Names\Alpha")) { }
            using (software.CreateSubKey("Names\\\u03A9mega")) { }
        });

        using OfflineHiveFile hive = OpenSoftware(image);

        IReadOnlyList<string> names = hive.OpenSubKey("Names")!.GetSubKeyNames();

        Assert.Contains("Alpha", names);
        Assert.Contains("\u03A9mega", names);
    }

    [Fact]
    public void GetSubKeyNames_MatchesLiveRegistryPhysicalLeafOrder()
    {
        // OfflineLegacyMessageFileResolver depends on "first channel wins", so the parser must surface subkeys in the same
        // physical leaf order the live registry enumerates them in - never re-sorted by the parser.
        using OfflineTestImage image = OfflineTestImage.Create(seedSoftware: software =>
        {
            using (software.CreateSubKey(@"Channels\Zebra")) { }
            using (software.CreateSubKey(@"Channels\Apple")) { }
            using (software.CreateSubKey(@"Channels\Mango")) { }
        });

        IReadOnlyList<string> live = OfflineTestImage.ReadSubKeyNamesViaLiveRegistry(image.ImageRoot.SoftwareHivePath, "Channels");
        using OfflineHiveFile hive = OpenSoftware(image);

        IReadOnlyList<string> offline = hive.OpenSubKey("Channels")!.GetSubKeyNames();

        Assert.Equal(live, offline);
    }

    [Fact]
    public void GetValue_DataLengthExceedingTheCap_ReturnsNullInsteadOfAllocating()
    {
        // A real registry refuses to store a >16 MB value, so craft one: take a valid hive, pad the file past 16 MB (cheap
        // zeros that are not part of any bin), and rewrite every vk record to CLAIM a ~16.5 MB non-resident length. The
        // claimed length is below the padded file length but above the parser's 16 MB cap, so GetValue must reject it (null)
        // rather than allocate ~16.5 MB and risk an OutOfMemoryException in the elevated helper.
        byte[] original = SeedRealHiveBytes();
        int originalLength = original.Length;
        byte[] padded = new byte[17 * 1024 * 1024];
        original.CopyTo(padded, 0);

        const uint claimedLength = 0x0108_0000; // ~16.5 MB; high (resident) bit clear; > MaxValueBytes (16 MB).

        for (int i = 0x1000; i + 0x10 < originalLength; i++)
        {
            if (padded[i] == 0x76 && padded[i + 1] == 0x6B) // "vk"
            {
                BinaryPrimitives.WriteUInt32LittleEndian(padded.AsSpan(i + 0x04), claimedLength);
            }
        }

        using OfflineHiveFile? hive = OfflineHiveFile.TryOpen(padded, logger: null);

        Assert.NotNull(hive);

        IOfflineRegistryKey? currentVersion = hive!.OpenSubKey(@"Microsoft\Windows NT\CurrentVersion");

        Assert.NotNull(currentVersion); // navigation still works; only the over-cap value read is rejected.
        Assert.Null(currentVersion!.GetValue("CurrentBuildNumber"));
    }

    [Fact]
    public void GetValue_DefaultValue_ReadByNullName()
    {
        using OfflineTestImage image = SeedSoftware(values => values.SetValue(null, "the-default"));

        using OfflineHiveFile hive = OpenSoftware(image);

        Assert.Equal("the-default", hive.OpenSubKey(TestKeyPath)!.GetValue(null));
    }

    [Fact]
    public void GetValue_LargeBinary_IsReassembledFromBigDataSegments()
    {
        // A value larger than a single 16344-byte cell is stored as a "db" big-data record pointing at segment cells; the
        // parser must stitch them back into the original byte sequence.
        byte[] payload = new byte[40_000];
        new Random(1234).NextBytes(payload);
        using OfflineTestImage image = SeedSoftware(values => values.SetValue("Blob", payload, RegistryValueKind.Binary));

        using OfflineHiveFile hive = OpenSoftware(image);

        object? offline = hive.OpenSubKey(TestKeyPath)!.GetValue("Blob");

        Assert.Equal(payload, Assert.IsType<byte[]>(offline));
    }

    [Fact]
    public void GetValue_MissingValue_ReturnsNull()
    {
        using OfflineTestImage image = SeedSoftware(values => values.SetValue("Present", "x"));

        using OfflineHiveFile hive = OpenSoftware(image);

        Assert.Null(hive.OpenSubKey(TestKeyPath)!.GetValue("Absent"));
    }

    [Fact]
    public void GetValue_RegBinary_ReturnsRawBytes()
    {
        byte[] payload = [0x00, 0x01, 0x7F, 0x80, 0xFF, 0x42];
        using OfflineTestImage image = SeedSoftware(values => values.SetValue("Bin", payload, RegistryValueKind.Binary));

        using OfflineHiveFile hive = OpenSoftware(image);

        object? offline = hive.OpenSubKey(TestKeyPath)!.GetValue("Bin");

        Assert.Equal(payload, Assert.IsType<byte[]>(offline));
    }

    [Fact]
    public void GetValue_RegDword_ReturnsBoxedInt32MatchingLiveRegistry()
    {
        // The boxed runtime type is load-bearing: OfflineLegacyMessageFileResolver and SourceOsProvenance test the result
        // with `is int`, so a boxed uint would silently break parity with the native-built database.
        using OfflineTestImage image = SeedSoftware(values => values.SetValue("Dw", unchecked((int)0x9ABCDEF0), RegistryValueKind.DWord));

        object? live = OfflineTestImage.ReadValueViaLiveRegistry(image.ImageRoot.SoftwareHivePath, TestKeyPath, "Dw");
        using OfflineHiveFile hive = OpenSoftware(image);

        object? offline = hive.OpenSubKey(TestKeyPath)!.GetValue("Dw");

        Assert.Equal(unchecked((int)0x9ABCDEF0), Assert.IsType<int>(offline));
        Assert.IsType<int>(live);
        Assert.Equal(live, offline);
    }

    [Fact]
    public void GetValue_RegExpandSz_IsReadLiterallyNotHostExpanded()
    {
        using OfflineTestImage image = SeedSoftware(values =>
            values.SetValue("Expand", @"%SystemRoot%\System32\x.dll", RegistryValueKind.ExpandString));

        object? live = OfflineTestImage.ReadValueViaLiveRegistry(image.ImageRoot.SoftwareHivePath, TestKeyPath, "Expand");
        using OfflineHiveFile hive = OpenSoftware(image);

        object? offline = hive.OpenSubKey(TestKeyPath)!.GetValue("Expand");

        // The %SystemRoot% token must survive verbatim; the parser never consults the host environment.
        Assert.Equal(@"%SystemRoot%\System32\x.dll", Assert.IsType<string>(offline));
        Assert.Equal(live, offline);
    }

    [Fact]
    public void GetValue_RegMultiSz_MatchesLiveRegistryExactly()
    {
        // Lock REG_MULTI_SZ decoding to Microsoft.Win32.RegistryKey.GetValue (the oracle): interior empties preserved, only
        // the final terminator dropped. Trailing-empty shapes like ["a",""] are the ones a whole-trailing-NUL-run trim breaks.
        string[][] shapes =
        [
            ["alpha", "beta"],
            ["first", "", "third"],
            ["a", ""],
            ["x", "", "", ""],
            ["solo"],
        ];

        foreach (string[] elements in shapes)
        {
            using OfflineTestImage image = SeedSoftware(values => values.SetValue("Multi", elements, RegistryValueKind.MultiString));

            object? live = OfflineTestImage.ReadValueViaLiveRegistry(image.ImageRoot.SoftwareHivePath, TestKeyPath, "Multi");
            using OfflineHiveFile hive = OpenSoftware(image);

            object? offline = hive.OpenSubKey(TestKeyPath)!.GetValue("Multi");

            Assert.Equal(Assert.IsType<string[]>(live), Assert.IsType<string[]>(offline));
        }
    }

    [Fact]
    public void GetValue_RegMultiSz_PreservesInteriorEmptyElementsAndDropsTerminator()
    {
        using OfflineTestImage image = SeedSoftware(values =>
            values.SetValue("Multi", new[] { "first", "", "third" }, RegistryValueKind.MultiString));

        using OfflineHiveFile hive = OpenSoftware(image);

        object? offline = hive.OpenSubKey(TestKeyPath)!.GetValue("Multi");

        Assert.Equal(new[] { "first", "", "third" }, Assert.IsType<string[]>(offline));
    }

    [Fact]
    public void GetValue_RegQword_ReturnsBoxedInt64MatchingLiveRegistry()
    {
        using OfflineTestImage image = SeedSoftware(values => values.SetValue("Qw", 0x1_2345_6789L, RegistryValueKind.QWord));

        object? live = OfflineTestImage.ReadValueViaLiveRegistry(image.ImageRoot.SoftwareHivePath, TestKeyPath, "Qw");
        using OfflineHiveFile hive = OpenSoftware(image);

        object? offline = hive.OpenSubKey(TestKeyPath)!.GetValue("Qw");

        Assert.Equal(0x1_2345_6789L, Assert.IsType<long>(offline));
        Assert.Equal(live, offline);
    }

    [Fact]
    public void GetValue_RegSz_ReturnsStringMatchingLiveRegistry()
    {
        using OfflineTestImage image = SeedSoftware(values => values.SetValue("Sz", "hello-world", RegistryValueKind.String));

        object? live = OfflineTestImage.ReadValueViaLiveRegistry(image.ImageRoot.SoftwareHivePath, TestKeyPath, "Sz");
        using OfflineHiveFile hive = OpenSoftware(image);

        object? offline = hive.OpenSubKey(TestKeyPath)!.GetValue("Sz");

        Assert.Equal("hello-world", Assert.IsType<string>(offline));
        Assert.Equal(live, offline);
    }

    [Fact]
    public void GetValue_ValueCountExceedingTheCap_ReturnsNullWithoutScanning()
    {
        // A crafted nk that declares a huge value count must be rejected before the per-name vk scan, so a lookup cannot be
        // forced to walk hundreds of thousands of vk offsets. Pad the file past 4 * MaxValuesPerNode so the per-node cap
        // (100k), not the file-size bound (_length/4), is the binding limit, then inflate every nk's value count.
        byte[] original = SeedRealHiveBytes();
        int originalLength = original.Length;
        byte[] padded = new byte[1024 * 1024]; // _length/4 = 256k > MaxValuesPerNode (100k).
        original.CopyTo(padded, 0);

        const int absurdValueCount = 150_000; // > MaxValuesPerNode (100k), < padded _length/4 (256k).

        for (int i = 0x1000; i + 0x28 < originalLength; i++)
        {
            if (padded[i] == 0x6E && padded[i + 1] == 0x6B) // "nk"
            {
                BinaryPrimitives.WriteInt32LittleEndian(padded.AsSpan(i + 0x24), absurdValueCount); // nk value count.
            }
        }

        using OfflineHiveFile? hive = OfflineHiveFile.TryOpen(padded, logger: null);

        Assert.NotNull(hive);

        IOfflineRegistryKey? currentVersion = hive!.OpenSubKey(@"Microsoft\Windows NT\CurrentVersion");

        Assert.NotNull(currentVersion); // navigation (subkey list) is untouched; only the value scan is capped.
        Assert.Null(currentVersion!.GetValue("CurrentBuildNumber"));
    }

    [Fact]
    public void OpenSubKey_MissingKey_ReturnsNull()
    {
        using OfflineTestImage image = SeedSoftware(values => values.SetValue("x", "y"));

        using OfflineHiveFile hive = OpenSoftware(image);

        Assert.Null(hive.OpenSubKey(@"No\Such\Key"));
    }

    [Fact]
    public void TryOpen_BadSignature_ReturnsNull()
    {
        byte[] bytes = SeedRealHiveBytes();
        bytes[0] = (byte)'X'; // corrupt the "regf" signature.

        Assert.Null(OfflineHiveFile.TryOpen(bytes, logger: null));
    }

    [Fact]
    public void TryOpen_BaseChecksumMismatch_ClassifiesDirtyButStillReads()
    {
        byte[] bytes = SeedRealHiveBytes();
        uint storedChecksum = BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(0x1FC));
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(0x1FC), storedChecksum ^ 0xFFFFFFFFu); // torn base, seq untouched.

        using OfflineHiveFile? hive = OfflineHiveFile.TryOpen(bytes, logger: null);

        Assert.NotNull(hive);
        Assert.True(hive!.IsDirty);

        // A torn base block is classified like a dirty hive: warn and read last-flushed, never reject.
        Assert.Equal("20348", hive.OpenSubKey(@"Microsoft\Windows NT\CurrentVersion")!.GetValue("CurrentBuildNumber"));
    }

    [Fact]
    public void TryOpen_BufferTooSmallToBeAHive_ReturnsNull() =>
        Assert.Null(OfflineHiveFile.TryOpen(new byte[256], logger: null));

    [Fact]
    public void TryOpen_DirtyHive_OpensAtLastFlushedStateWithoutRejecting()
    {
        byte[] bytes = SeedRealHiveBytes();
        uint primarySeq = BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(0x04));
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(0x04), primarySeq + 1); // primary != secondary => dirty.

        using OfflineHiveFile? hive = OfflineHiveFile.TryOpen(bytes, logger: null);

        Assert.NotNull(hive);
        Assert.True(hive!.IsDirty);

        // A dirty hive is still read (last-flushed state), never rejected.
        Assert.Equal("20348", hive.OpenSubKey(@"Microsoft\Windows NT\CurrentVersion")!.GetValue("CurrentBuildNumber"));
    }

    [Fact]
    public void TryOpen_NullBytes_Throws() =>
        Assert.Throws<ArgumentNullException>(() => OfflineHiveFile.TryOpen((byte[])null!, logger: null));

    [Fact]
    public void TryOpen_RandomlyCorruptedBins_NeverThrowsWhileWalkingTheHive()
    {
        byte[] original = SeedRealHiveBytes();
        var random = new Random(0x00C0FFEE);

        for (int iteration = 0; iteration < 600; iteration++)
        {
            byte[] corrupt = (byte[])original.Clone();
            int position = 0x1000 + random.Next(corrupt.Length - 0x1000); // corrupt only the hive bins, not the base block.
            corrupt[position] = (byte)random.Next(256);

            OfflineHiveFile? hive = OfflineHiveFile.TryOpen(corrupt, logger: null);

            if (hive is null) { continue; }

            using (hive)
            {
                try
                {
                    _ = hive.GetSubKeyNames();
                    IOfflineRegistryKey? currentVersion = hive.OpenSubKey(@"Microsoft\Windows NT\CurrentVersion");
                    _ = currentVersion?.GetSubKeyNames();
                    _ = currentVersion?.GetValue("CurrentBuildNumber");
                }
                catch (Exception ex) when (ex is not OutOfMemoryException and not StackOverflowException)
                {
                    Assert.Fail($"Corrupting byte {position} (value 0x{corrupt[position]:X2}) made the parser throw: {ex}");
                }
            }
        }
    }

    [Fact]
    public void TryOpen_RootCellOffsetOutOfRange_ReturnsNull()
    {
        byte[] bytes = SeedRealHiveBytes();
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(0x24), 0x7FFF_FFFFu); // root cell far past the file.

        Assert.Null(OfflineHiveFile.TryOpen(bytes, logger: null));
    }

    private static OfflineHiveFile OpenSoftware(OfflineTestImage image)
    {
        OfflineHiveFile? hive = OfflineHiveFile.TryOpen(image.ImageRoot.SoftwareHivePath, logger: null);
        Assert.NotNull(hive);

        return hive!;
    }

    // A real, structurally-valid SOFTWARE hive (with CurrentVersion content) as raw bytes, for byte-level corruption tests.
    private static byte[] SeedRealHiveBytes()
    {
        using OfflineTestImage image = OfflineTestImage.Create(seedSoftware: software =>
        {
            using RegistryKey currentVersion = software.CreateSubKey(@"Microsoft\Windows NT\CurrentVersion");
            currentVersion.SetValue("CurrentBuildNumber", "20348", RegistryValueKind.String);
            currentVersion.SetValue("UBR", 2700, RegistryValueKind.DWord);
        });

        return File.ReadAllBytes(image.ImageRoot.SoftwareHivePath);
    }

    private static OfflineTestImage SeedSoftware(Action<RegistryKey> seedValuesKey) =>
        OfflineTestImage.Create(seedSoftware: software =>
        {
            using RegistryKey values = software.CreateSubKey(TestKeyPath);
            seedValuesKey(values);
        });
}
