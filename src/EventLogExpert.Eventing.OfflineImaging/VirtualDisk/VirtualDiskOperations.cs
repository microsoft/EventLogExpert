// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Eventing.OfflineImaging.Interop;
using EventLogExpert.Logging.Abstractions;
using Microsoft.Win32.SafeHandles;
using System.Runtime.InteropServices;

namespace EventLogExpert.Eventing.OfflineImaging.VirtualDisk;

internal sealed class VirtualDiskOperations : IVirtualDiskOperations
{
    internal static readonly VirtualDiskOperations Instance = new();

    // Check PhysicalDrive before CDROM so VHD/VHDX paths parse as disk numbers.
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

            // Keep corrupt/unrecognized images distinct from actionable environment failures.
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

        // The lease auto-detaches the ISO; route every later failure through it.
        var lease = new VirtualDiskLease(handle);

        try
        {
            string? volumeRoot = ResolveMountedVolume(handle, logger);

            if (volumeRoot is not null)
            {
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
        // Let Windows auto-detect .vhd versus .vhdx.
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

            // Keep corrupt/unrecognized files distinct from actionable environment failures.
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
            // Close opened-but-unattached disks so attach failures do not leak handles.
            handle.Dispose();
            logger?.Debug($"{nameof(VirtualDiskOperations)}: AttachVirtualDisk failed for {vhdxPath} (error {attachResult}).");

            return VhdxAttachResult.Failed(VhdxAttachStatus.Failed);
        }

        // The lease auto-detaches the disk; route every later failure through it.
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

                if (NativeMethods.GetDriveTypeW(volume) != NativeMethods.DRIVE_FIXED) { continue; }

                if (VolumeDeviceNumber(volume.TrimEnd('\\')) != diskNumber) { continue; }

                // Skip unreadable candidate volumes so a sibling readable Windows volume can still win.
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

    // ISO and VHD/VHDX physical paths both encode the disk number used by mounted volumes.
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

    // Retry mount discovery and match only this disk so pre-existing ISOs/DVDs are never selected.
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

    // Retry until the filesystem surfaces; require ambiguity instead of guessing among Windows volumes.
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
                // Require two consecutive single-volume passes so late-surfacing sibling partitions trip ambiguity.
                if (pendingSingle == candidates[0]) { return (WindowsVolumeResolution.Found, candidates[0]); }

                pendingSingle = candidates[0];
            }
            else
            {
                pendingSingle = null;
            }

            Thread.Sleep(200);
        }

        // Accept a final-pass single volume; a later sibling after the whole retry window is implausible.
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
