// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Eventing.OfflineImaging.Registry;
using Microsoft.Win32;
using System.Buffers.Binary;

namespace EventLogExpert.Eventing.OfflineImaging.Tests.Registry;

public sealed class OfflineHiveFileTests
{
    private const string TestKeyPath = @"Test\Values";

    [Fact]
    public void Enumeration_WithEveryNkClaimingHugeSubkeyCountAndWildListOffset_TerminatesWithoutThrowing()
    {
        // Crafted nk claims absurd subkeys and a wild list offset to hit cap/bounds guards.
        byte[] bytes = SeedRealHiveBytes();

        for (int i = 0x1000; i + 0x20 < bytes.Length; i++)
        {
            if (bytes[i] == 0x6E && bytes[i + 1] == 0x6B) // "nk"
            {
                BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(i + 0x14), int.MaxValue); // subkey count
                BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(i + 0x1C), 0x0FFF_FFF0u); // subkey-list offset
            }
        }

        OfflineHiveFile? hive = OfflineHiveFile.TryOpen(bytes, logger: null);

        if (hive is null) { return; }

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
        // regf stores ASCII-compressed names as Latin-1 and non-ASCII names as UTF-16.
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
        using OfflineTestImage image = OfflineTestImage.Create(seedSoftware: software =>
        {
            using (software.CreateSubKey(@"Channels\Zebra")) { }
            using (software.CreateSubKey(@"Channels\Apple")) { }
            using (software.CreateSubKey(@"Channels\Mango")) { }
        });

        // First channel wins: parser order must match live registry leaf order.
        IReadOnlyList<string> live = OfflineTestImage.ReadSubKeyNamesViaLiveRegistry(image.ImageRoot.SoftwareHivePath, "Channels");
        using OfflineHiveFile hive = OpenSoftware(image);

        IReadOnlyList<string> offline = hive.OpenSubKey("Channels")!.GetSubKeyNames();

        Assert.Equal(live, offline);
    }

    [Fact]
    public void GetValue_DataLengthExceedingTheCap_ReturnsNullInsteadOfAllocating()
    {
        // Crafted vk claims a >16 MB value so the cap rejects it before allocation.
        byte[] original = SeedRealHiveBytes();
        int originalLength = original.Length;
        byte[] padded = new byte[17 * 1024 * 1024];
        original.CopyTo(padded, 0);

        const uint claimedLength = 0x0108_0000; // ~16.5 MB; high resident bit clear; exceeds MaxValueBytes.

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

        Assert.NotNull(currentVersion);
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
        // "db" big-data values are reassembled from segment cells.
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
        // REG_DWORD must be boxed Int32; callers test with `is int`.
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

        // REG_EXPAND_SZ stays literal; the host environment is not consulted.
        Assert.Equal(@"%SystemRoot%\System32\x.dll", Assert.IsType<string>(offline));
        Assert.Equal(live, offline);
    }

    [Fact]
    public void GetValue_RegMultiSz_MatchesLiveRegistryExactly()
    {
        // REG_MULTI_SZ parity preserves interior empties and drops only the final terminator.
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
        byte[] original = SeedRealHiveBytes();
        int originalLength = original.Length;
        byte[] padded = new byte[1024 * 1024]; // _length/4 = 256k > MaxValuesPerNode.
        original.CopyTo(padded, 0);

        const int absurdValueCount = 150_000; // exceeds MaxValuesPerNode; below padded _length/4.

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

        Assert.NotNull(currentVersion);
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
        bytes[0] = (byte)'X';

        Assert.Null(OfflineHiveFile.TryOpen(bytes, logger: null));
    }

    [Fact]
    public void TryOpen_BaseChecksumMismatch_ClassifiesDirtyButStillReads()
    {
        byte[] bytes = SeedRealHiveBytes();
        uint storedChecksum = BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(0x1FC));
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(0x1FC), storedChecksum ^ 0xFFFFFFFFu); // torn base, sequence unchanged.

        using OfflineHiveFile? hive = OfflineHiveFile.TryOpen(bytes, logger: null);

        Assert.NotNull(hive);
        Assert.True(hive!.IsDirty);

        // Torn/dirty hives warn, then read last-flushed state.
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
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(0x04), primarySeq + 1); // primary != secondary marks dirty.

        using OfflineHiveFile? hive = OfflineHiveFile.TryOpen(bytes, logger: null);

        Assert.NotNull(hive);
        Assert.True(hive!.IsDirty);

        // Torn/dirty hives warn, then read last-flushed state.
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
            int position = 0x1000 + random.Next(corrupt.Length - 0x1000); // corrupt hive bins only; base block stays valid.
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
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(0x24), 0x7FFF_FFFFu);

        Assert.Null(OfflineHiveFile.TryOpen(bytes, logger: null));
    }

    private static OfflineHiveFile OpenSoftware(OfflineTestImage image)
    {
        OfflineHiveFile? hive = OfflineHiveFile.TryOpen(image.ImageRoot.SoftwareHivePath, logger: null);
        Assert.NotNull(hive);

        return hive!;
    }

    // Real SOFTWARE hive bytes keep hostile-input mutations structurally plausible.
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
