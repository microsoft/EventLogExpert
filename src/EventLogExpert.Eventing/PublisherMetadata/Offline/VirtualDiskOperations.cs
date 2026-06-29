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
        var lease = new IsoAttachLease(handle);

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

    private static string? FindVolumeOnDisk(uint diskNumber, bool requireInstallImage)
    {
        char[] volumeName = new char[64];
        IntPtr find = NativeMethods.FindFirstVolumeW(volumeName, (uint)volumeName.Length);

        if (find == IntPtr.Zero || find == new IntPtr(-1)) { return null; }

        string? firstOnDisk = null;

        try
        {
            do
            {
                string volume = new string(volumeName).TrimEnd('\0');

                if (!VolumeIsOnDisk(volume.TrimEnd('\\'), diskNumber)) { continue; }

                firstOnDisk ??= volume;

                if (File.Exists(Path.Join(volume, "sources", "install.wim")) ||
                    File.Exists(Path.Join(volume, "sources", "install.esd")))
                {
                    return volume;
                }
            }
            while (NativeMethods.FindNextVolumeW(find, volumeName, (uint)volumeName.Length));

            return requireInstallImage ? null : firstOnDisk;
        }
        finally
        {
            NativeMethods.FindVolumeClose(find);
        }
    }

    // The volume that appears for a freshly attached ISO can lag the attach, so retry the whole resolve briefly. Prefer a
    // disk-matching volume that holds an install image; fall back to any volume on the disk so an image-less but valid ISO
    // still resolves and OfflineIsoImage can report NoInstallImage rather than a generic mount failure.
    private static string? ResolveMountedVolume(VirtualDiskSafeHandle handle, ITraceLogger? logger)
    {
        for (int attempt = 0; attempt < 20; attempt++)
        {
            uint diskNumber = ResolvePhysicalDiskNumber(handle);

            if (diskNumber != uint.MaxValue && FindVolumeOnDisk(diskNumber, requireInstallImage: true) is { } withImage) { return withImage; }

            Thread.Sleep(100);
        }

        // The window elapsed without an install image appearing; fall back to any volume on the disk so a valid but
        // image-less ISO resolves and OfflineIsoImage reports NoInstallImage rather than a generic mount failure.
        uint disk = ResolvePhysicalDiskNumber(handle);

        if (disk != uint.MaxValue && FindVolumeOnDisk(disk, requireInstallImage: false) is { } any) { return any; }

        logger?.Debug($"{nameof(VirtualDiskOperations)}: mounted volume did not resolve in time.");

        return null;
    }

    private static uint ResolvePhysicalDiskNumber(VirtualDiskSafeHandle handle)
    {
        uint sizeBytes = 0;
        NativeMethods.GetVirtualDiskPhysicalPath(handle, ref sizeBytes, null);

        if (sizeBytes < 2) { return uint.MaxValue; }

        char[] buffer = new char[sizeBytes / 2];

        if (NativeMethods.GetVirtualDiskPhysicalPath(handle, ref sizeBytes, buffer) != Win32ErrorCodes.ERROR_SUCCESS) { return uint.MaxValue; }

        string path = new string(buffer).TrimEnd('\0');
        int digit = path.LastIndexOf("PhysicalDrive", StringComparison.OrdinalIgnoreCase);

        return digit >= 0 && uint.TryParse(path[(digit + "PhysicalDrive".Length)..], out uint diskNumber) ? diskNumber : uint.MaxValue;
    }

    private static bool VolumeIsOnDisk(string volumeDevice, uint diskNumber)
    {
        using SafeFileHandle device = NativeMethods.CreateFileW(
            volumeDevice,
            NativeMethods.GENERIC_READ,
            NativeMethods.FILE_SHARE_READ_WRITE,
            IntPtr.Zero,
            NativeMethods.OPEN_EXISTING,
            0,
            IntPtr.Zero);

        if (device.IsInvalid) { return false; }

        int extentSize = Marshal.SizeOf<NativeMethods.DISK_EXTENT>();

        // VOLUME_DISK_EXTENTS is { uint NumberOfDiskExtents; DISK_EXTENT[] }; DISK_EXTENT's 8-byte-aligned long pushes the
        // array to offset 8 (4-byte count + 4 pad), not 4 - reading at 4 would parse DiskNumber from the padding.
        const int extentsOffset = 8;
        int bufferSize = extentsOffset + (extentSize * 16);
        IntPtr buffer = Marshal.AllocHGlobal(bufferSize);

        try
        {
            if (!NativeMethods.DeviceIoControl(device,
                NativeMethods.IOCTL_VOLUME_GET_VOLUME_DISK_EXTENTS,
                IntPtr.Zero,
                0,
                buffer,
                (uint)bufferSize,
                out _,
                IntPtr.Zero))
            {
                return false;
            }

            int count = Marshal.ReadInt32(buffer);

            for (int i = 0; i < count; i++)
            {
                var extent = Marshal.PtrToStructure<NativeMethods.DISK_EXTENT>(buffer + extentsOffset + (i * extentSize));

                if (extent.DiskNumber == diskNumber) { return true; }
            }

            return false;
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
    }

    private sealed class IsoAttachLease(VirtualDiskSafeHandle handle) : IDisposable
    {
        public void Dispose() => handle.Dispose();
    }
}
