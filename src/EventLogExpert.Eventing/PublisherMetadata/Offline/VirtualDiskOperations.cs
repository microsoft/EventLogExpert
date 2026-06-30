// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Eventing.Interop;
using EventLogExpert.Logging.Abstractions;
using Microsoft.Win32.SafeHandles;
using System.Runtime.InteropServices;

namespace EventLogExpert.Eventing.PublisherMetadata.Offline;

/// <summary>
///     Real <see cref="IVirtualDiskOperations" />: opens an ISO read-only, attaches it with no drive letter, then
///     resolves the mounted volume by matching disk extents to the virtual disk's physical drive so the install image is
///     reached by volume GUID (no drive-letter dependence). The attach is non-permanent, so disposing the lease detaches
///     the ISO and a crash never leaves it mounted.
/// </summary>
internal sealed class VirtualDiskOperations : IVirtualDiskOperations
{
    internal static readonly VirtualDiskOperations Instance = new();

    // PhysicalDrive checked before CDROM so a VHD/VHDX (\\.\PhysicalDriveN) is parsed correctly; both are disk numbers.
    private static readonly string[] s_physicalPathMarkers = ["PHYSICALDRIVE", "CDROM"];

    private enum WindowsVolumeResolution { Found, None, Multiple }

    public IsoAttachResult Attach(string isoPath, ITraceLogger? logger)
    {
        NativeMethods.VIRTUAL_STORAGE_TYPE storageType = new()
        {
            DeviceId = NativeMethods.VIRTUAL_STORAGE_TYPE_DEVICE_ISO,
            VendorId = NativeMethods.VIRTUAL_STORAGE_TYPE_VENDOR_MICROSOFT
        };

        int openResult = NativeMethods.OpenVirtualDisk(
            ref storageType,
            isoPath,
            NativeMethods.VIRTUAL_DISK_ACCESS_ATTACH_RO | NativeMethods.VIRTUAL_DISK_ACCESS_GET_INFO | NativeMethods.VIRTUAL_DISK_ACCESS_DETACH,
            NativeMethods.OPEN_VIRTUAL_DISK_FLAG_NONE,
            IntPtr.Zero,
            out VirtualDiskSafeHandle handle);

        if (openResult != Win32ErrorCodes.ERROR_SUCCESS)
        {
            handle.Dispose();
            logger?.Debug($"{nameof(VirtualDiskOperations)}: OpenVirtualDisk failed for {isoPath} (error {openResult}).");

            // A corrupt/unrecognized image is NotAnIso; access-denied/compressed/encrypted host volumes are environment
            // failures the user can act on (run elevated, free the file), so keep those distinct.
            bool badImage = openResult is Win32ErrorCodes.ERROR_FILE_CORRUPT or Win32ErrorCodes.ERROR_INVALID_PARAMETER or Win32ErrorCodes.ERROR_NOT_SUPPORTED;

            return IsoAttachResult.Failed(badImage ? IsoAttachStatus.NotAnIso : IsoAttachStatus.Failed);
        }

        int attachResult = NativeMethods.AttachVirtualDisk(
            handle,
            IntPtr.Zero,
            NativeMethods.ATTACH_VIRTUAL_DISK_FLAG_READ_ONLY | NativeMethods.ATTACH_VIRTUAL_DISK_FLAG_NO_DRIVE_LETTER,
            0,
            IntPtr.Zero,
            IntPtr.Zero);

        if (attachResult != Win32ErrorCodes.ERROR_SUCCESS)
        {
            handle.Dispose();
            logger?.Debug($"{nameof(VirtualDiskOperations)}: AttachVirtualDisk failed for {isoPath} (error {attachResult}).");

            return IsoAttachResult.Failed(IsoAttachStatus.Failed);
        }

        // Disposing the handle (the lease) auto-detaches the ISO from here on; route every failure through it.
        var lease = new VirtualDiskLease(handle);

        try
        {
            string? volumeRoot = ResolveMountedVolume(handle, logger);

            if (volumeRoot is not null)
            {
                // Disk-only match: a valid ISO with no install image still resolves so OfflineIsoImage reports NoInstallImage.
                return new IsoAttachResult(IsoAttachStatus.Attached, volumeRoot, lease);
            }

            lease.Dispose();

            return IsoAttachResult.Failed(IsoAttachStatus.Failed);

        }
        catch (Exception ex) when (ex is not OutOfMemoryException and not StackOverflowException)
        {
            logger?.Debug($"{nameof(VirtualDiskOperations)}: resolving the mounted volume for {isoPath} failed: {ex.Message}");
            lease.Dispose();

            return IsoAttachResult.Failed(IsoAttachStatus.Failed);
        }
    }

    public VhdxAttachResult AttachVhdx(string vhdxPath, ITraceLogger? logger)
    {
        // DEVICE_UNKNOWN + the empty vendor GUID let Windows auto-detect .vhd vs .vhdx (no per-extension device id).
        NativeMethods.VIRTUAL_STORAGE_TYPE storageType = new()
        {
            DeviceId = NativeMethods.VIRTUAL_STORAGE_TYPE_DEVICE_UNKNOWN,
            VendorId = NativeMethods.VIRTUAL_STORAGE_TYPE_VENDOR_UNKNOWN
        };

        int openResult = NativeMethods.OpenVirtualDisk(
            ref storageType,
            vhdxPath,
            NativeMethods.VIRTUAL_DISK_ACCESS_ATTACH_RO | NativeMethods.VIRTUAL_DISK_ACCESS_GET_INFO | NativeMethods.VIRTUAL_DISK_ACCESS_DETACH,
            NativeMethods.OPEN_VIRTUAL_DISK_FLAG_NONE,
            IntPtr.Zero,
            out VirtualDiskSafeHandle handle);

        if (openResult != Win32ErrorCodes.ERROR_SUCCESS)
        {
            handle.Dispose();
            logger?.Debug($"{nameof(VirtualDiskOperations)}: OpenVirtualDisk failed for {vhdxPath} (error {openResult}).");

            // A corrupt/unrecognized file is NotAVhdx; everything else (e.g. the host file is in use) is an environment
            // failure surfaced as a generic mount failure.
            bool badImage = openResult is Win32ErrorCodes.ERROR_FILE_CORRUPT or Win32ErrorCodes.ERROR_INVALID_PARAMETER or Win32ErrorCodes.ERROR_NOT_SUPPORTED;

            return VhdxAttachResult.Failed(badImage ? VhdxAttachStatus.NotAVhdx : VhdxAttachStatus.Failed);
        }

        int attachResult = NativeMethods.AttachVirtualDisk(
            handle,
            IntPtr.Zero,
            NativeMethods.ATTACH_VIRTUAL_DISK_FLAG_READ_ONLY | NativeMethods.ATTACH_VIRTUAL_DISK_FLAG_NO_DRIVE_LETTER,
            0,
            IntPtr.Zero,
            IntPtr.Zero);

        if (attachResult != Win32ErrorCodes.ERROR_SUCCESS)
        {
            // Open succeeded but attach failed: close the handle so the opened-but-unattached disk is not leaked.
            handle.Dispose();
            logger?.Debug($"{nameof(VirtualDiskOperations)}: AttachVirtualDisk failed for {vhdxPath} (error {attachResult}).");

            return VhdxAttachResult.Failed(VhdxAttachStatus.Failed);
        }

        // Disposing the handle (the lease) auto-detaches the disk from here on; route every later failure through it so
        // a partially-resolved mount is never left attached.
        var lease = new VirtualDiskLease(handle);

        try
        {
            (WindowsVolumeResolution resolution, string? volumeRoot) = ResolveWindowsVolume(handle, logger);

            switch (resolution)
            {
                case WindowsVolumeResolution.Found:
                    return new VhdxAttachResult(VhdxAttachStatus.Attached, volumeRoot, lease);
                case WindowsVolumeResolution.Multiple:
                    lease.Dispose();

                    return VhdxAttachResult.Failed(VhdxAttachStatus.MultipleWindowsVolumes);
                default:
                    lease.Dispose();

                    return VhdxAttachResult.Failed(VhdxAttachStatus.NoWindowsVolume);
            }
        }
        catch (Exception ex) when (ex is not OutOfMemoryException and not StackOverflowException)
        {
            logger?.Debug($"{nameof(VirtualDiskOperations)}: resolving the Windows volume for {vhdxPath} failed: {ex.Message}");
            lease.Dispose();

            return VhdxAttachResult.Failed(VhdxAttachStatus.Failed);
        }
    }

    private static string? FindCdromVolume(uint deviceNumber, bool requireInstallImage)
    {
        char[] volumeName = new char[64];
        IntPtr find = NativeMethods.FindFirstVolumeW(volumeName, (uint)volumeName.Length);

        if (find == IntPtr.Zero || find == new IntPtr(-1)) { return null; }

        string? firstOnDevice = null;

        try
        {
            do
            {
                string volume = new string(volumeName).TrimEnd('\0');

                if (NativeMethods.GetDriveTypeW(volume) != NativeMethods.DRIVE_CDROM) { continue; }

                if (VolumeDeviceNumber(volume.TrimEnd('\\')) != deviceNumber) { continue; }

                firstOnDevice ??= volume;

                if (File.Exists(Path.Join(volume, "sources", "install.wim")) ||
                    File.Exists(Path.Join(volume, "sources", "install.esd")))
                {
                    return volume;
                }
            }
            while (NativeMethods.FindNextVolumeW(find, volumeName, (uint)volumeName.Length));

            return requireInstallImage ? null : firstOnDevice;
        }
        finally
        {
            NativeMethods.FindVolumeClose(find);
        }
    }

    private static List<string> FindWindowsVolumesOnDisk(uint diskNumber, ITraceLogger? logger)
    {
        var matches = new List<string>();
        char[] volumeName = new char[64];
        IntPtr find = NativeMethods.FindFirstVolumeW(volumeName, (uint)volumeName.Length);

        if (find == IntPtr.Zero || find == new IntPtr(-1)) { return matches; }

        try
        {
            do
            {
                string volume = new string(volumeName).TrimEnd('\0');

                // A VHD/VHDX partition mounts as a fixed disk; skip CD-ROM/removable/network volumes outright.
                if (NativeMethods.GetDriveTypeW(volume) != NativeMethods.DRIVE_FIXED) { continue; }

                if (VolumeDeviceNumber(volume.TrimEnd('\\')) != diskNumber) { continue; }

                // The volume GUID already ends with a single backslash, so Path.Join yields \\?\Volume{GUID}\Windows\System32
                // with no double separator. BitLocker-locked / raw / non-NTFS partitions throw here; swallow and skip so a
                // sibling readable Windows volume can still win.
                try
                {
                    if (Directory.Exists(Path.Join(volume, "Windows", "System32"))) { matches.Add(volume); }
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    logger?.Debug($"{nameof(VirtualDiskOperations)}: skipping unreadable volume {volume}: {ex.Message}");
                }
            }
            while (NativeMethods.FindNextVolumeW(find, volumeName, (uint)volumeName.Length));
        }
        finally
        {
            NativeMethods.FindVolumeClose(find);
        }

        return matches;
    }

    // GetVirtualDiskPhysicalPath returns \\.\CDROMn for an attached ISO and \\.\PhysicalDriveN for an attached VHD/VHDX;
    // both encode the disk number that STORAGE_DEVICE_NUMBER reports for the mounted volume. Unknown forms -> MaxValue.
    private static uint ParsePhysicalPathNumber(string physicalPath)
    {
        foreach (string marker in s_physicalPathMarkers)
        {
            int index = physicalPath.LastIndexOf(marker, StringComparison.OrdinalIgnoreCase);

            if (index >= 0 && uint.TryParse(physicalPath[(index + marker.Length)..], out uint number)) { return number; }
        }

        return uint.MaxValue;
    }

    private static uint ResolveDeviceNumber(VirtualDiskSafeHandle handle)
    {
        uint sizeBytes = 0;
        NativeMethods.GetVirtualDiskPhysicalPath(handle, ref sizeBytes, null);

        if (sizeBytes < 2) { return uint.MaxValue; }

        char[] buffer = new char[sizeBytes / 2];

        if (NativeMethods.GetVirtualDiskPhysicalPath(handle, ref sizeBytes, buffer) != Win32ErrorCodes.ERROR_SUCCESS) { return uint.MaxValue; }

        string path = new string(buffer).TrimEnd('\0');

        return ParsePhysicalPathNumber(path);
    }

    // A mounted ISO appears as a CD-ROM device (GetVirtualDiskPhysicalPath returns \\.\CDROMn). The mount can lag the
    // attach, so retry; match ONLY the volume whose storage device number equals THIS disk's, so a pre-existing mounted ISO
    // or inserted DVD is never picked. Prefer the install-bearing volume, else any volume on this disk (image-less ISO ->
    // NoInstallImage downstream).
    private static string? ResolveMountedVolume(VirtualDiskSafeHandle handle, ITraceLogger? logger)
    {
        for (int attempt = 0; attempt < 20; attempt++)
        {
            uint deviceNumber = ResolveDeviceNumber(handle);

            if (deviceNumber != uint.MaxValue)
            {
                if (FindCdromVolume(deviceNumber, requireInstallImage: true) is { } withImage) { return withImage; }

                if (FindCdromVolume(deviceNumber, requireInstallImage: false) is { } any) { return any; }
            }

            Thread.Sleep(100);
        }

        logger?.Debug($"{nameof(VirtualDiskOperations)}: mounted volume did not resolve in time.");

        return null;
    }

    // A mounted VHD/VHDX appears as a fixed disk (GetVirtualDiskPhysicalPath returns \\.\PhysicalDriveN), and the
    // filesystem can take longer to surface than an ISO, so retry the full enumerate->match->probe for ~10s. Match ONLY
    // volumes on THIS disk so a sibling fixed disk is never picked. More than one Windows volume is a deliberate ambiguity
    // error (never a silent guess); none after the window is reported distinctly so an encrypted / data-only disk gets an
    // actionable message. A SINGLE match must repeat on a consecutive pass before it wins, so a second Windows partition
    // whose filesystem surfaces a beat later still trips the ambiguity guard instead of being silently skipped.
    private static (WindowsVolumeResolution Resolution, string? VolumeRoot) ResolveWindowsVolume(VirtualDiskSafeHandle handle, ITraceLogger? logger)
    {
        string? pendingSingle = null;

        for (int attempt = 0; attempt < 50; attempt++)
        {
            uint diskNumber = ResolveDeviceNumber(handle);

            List<string> candidates = diskNumber != uint.MaxValue
                ? FindWindowsVolumesOnDisk(diskNumber, logger)
                : [];

            if (candidates.Count > 1)
            {
                logger?.Debug(
                    $"{nameof(VirtualDiskOperations)}: multiple Windows volumes on disk {diskNumber}: {string.Join(", ", candidates)}.");

                return (WindowsVolumeResolution.Multiple, null);
            }

            if (candidates.Count == 1)
            {
                // Accept only after the same single volume is seen on two consecutive passes; the intervening sleep gives
                // a sibling Windows partition time to surface (which would flip the result to Multiple above).
                if (pendingSingle == candidates[0]) { return (WindowsVolumeResolution.Found, candidates[0]); }

                pendingSingle = candidates[0];
            }
            else
            {
                pendingSingle = null;
            }

            Thread.Sleep(200);
        }

        // A single volume that only surfaced on the final pass never got a confirming pass; accept it rather than fail a
        // valid single-Windows disk (a sibling appearing later than the whole ~10s window is implausible).
        if (pendingSingle is not null) { return (WindowsVolumeResolution.Found, pendingSingle); }

        logger?.Debug($"{nameof(VirtualDiskOperations)}: no readable Windows volume resolved in time.");

        return (WindowsVolumeResolution.None, null);
    }

    private static uint VolumeDeviceNumber(string volumeDevice)
    {
        using SafeFileHandle device = NativeMethods.CreateFileW(
            volumeDevice, 0, NativeMethods.FILE_SHARE_READ_WRITE, IntPtr.Zero, NativeMethods.OPEN_EXISTING, 0, IntPtr.Zero);

        if (device.IsInvalid) { return uint.MaxValue; }

        int size = Marshal.SizeOf<NativeMethods.STORAGE_DEVICE_NUMBER>();
        IntPtr buffer = Marshal.AllocHGlobal(size);

        try
        {
            return NativeMethods.DeviceIoControl(device, NativeMethods.IOCTL_STORAGE_GET_DEVICE_NUMBER, IntPtr.Zero, 0, buffer, (uint)size, out _, IntPtr.Zero)
                ? Marshal.PtrToStructure<NativeMethods.STORAGE_DEVICE_NUMBER>(buffer).DeviceNumber
                : uint.MaxValue;
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
    }

    private sealed class VirtualDiskLease(VirtualDiskSafeHandle handle) : IDisposable
    {
        public void Dispose() => handle.Dispose();
    }
}
