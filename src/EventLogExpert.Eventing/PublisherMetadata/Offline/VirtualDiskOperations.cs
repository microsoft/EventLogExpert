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

    private static uint ResolveDeviceNumber(VirtualDiskSafeHandle handle)
    {
        uint sizeBytes = 0;
        NativeMethods.GetVirtualDiskPhysicalPath(handle, ref sizeBytes, null);

        if (sizeBytes < 2) { return uint.MaxValue; }

        char[] buffer = new char[sizeBytes / 2];

        if (NativeMethods.GetVirtualDiskPhysicalPath(handle, ref sizeBytes, buffer) != Win32ErrorCodes.ERROR_SUCCESS) { return uint.MaxValue; }

        string path = new string(buffer).TrimEnd('\0');
        int marker = path.LastIndexOf("CDROM", StringComparison.OrdinalIgnoreCase);

        return marker >= 0 && uint.TryParse(path[(marker + "CDROM".Length)..], out uint number) ? number : uint.MaxValue;
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

    private sealed class IsoAttachLease(VirtualDiskSafeHandle handle) : IDisposable
    {
        public void Dispose() => handle.Dispose();
    }
}
