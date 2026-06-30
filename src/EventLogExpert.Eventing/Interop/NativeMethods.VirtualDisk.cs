// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using Microsoft.Win32.SafeHandles;
using System.Runtime.InteropServices;

// ReSharper disable InconsistentNaming
namespace EventLogExpert.Eventing.Interop;

internal static partial class NativeMethods
{
    internal const uint ATTACH_VIRTUAL_DISK_FLAG_NO_DRIVE_LETTER = 0x00000001;
    internal const uint ATTACH_VIRTUAL_DISK_FLAG_READ_ONLY = 0x00000002;
    internal const uint DRIVE_CDROM = 5;
    internal const uint DRIVE_FIXED = 3;
    internal const uint FILE_SHARE_READ_WRITE = 0x00000003;
    internal const uint IOCTL_STORAGE_GET_DEVICE_NUMBER = 0x002D1080;
    internal const uint OPEN_EXISTING = 3;
    internal const uint OPEN_VIRTUAL_DISK_FLAG_NONE = 0x00000000;
    internal const uint VIRTUAL_DISK_ACCESS_ATTACH_RO = 0x00010000;
    internal const uint VIRTUAL_DISK_ACCESS_DETACH = 0x00040000;
    internal const uint VIRTUAL_DISK_ACCESS_GET_INFO = 0x00080000;
    // The ISO storage type uses the Microsoft vendor GUID; the device id selects the .iso interpretation.
    internal const uint VIRTUAL_STORAGE_TYPE_DEVICE_ISO = 1;
    // DEVICE_UNKNOWN paired with the empty vendor GUID lets Windows auto-detect the format (.vhd vs .vhdx).
    internal const uint VIRTUAL_STORAGE_TYPE_DEVICE_UNKNOWN = 0;

    private const string VirtDiskApi = "virtdisk.dll";

    internal static readonly Guid VIRTUAL_STORAGE_TYPE_VENDOR_MICROSOFT = new("EC984AEC-A0F9-47e9-901F-71415A66345B");
    internal static readonly Guid VIRTUAL_STORAGE_TYPE_VENDOR_UNKNOWN = Guid.Empty;

    [LibraryImport(VirtDiskApi, EntryPoint = "AttachVirtualDisk", SetLastError = false)]
    internal static partial int AttachVirtualDisk(
        VirtualDiskSafeHandle virtualDiskHandle,
        IntPtr securityDescriptor,
        uint flags,
        uint providerSpecificFlags,
        IntPtr parameters,
        IntPtr overlapped);

    [LibraryImport(Kernel32Api,
        EntryPoint = "CreateFileW",
        StringMarshalling = StringMarshalling.Utf16,
        SetLastError = true)]
    internal static partial SafeFileHandle CreateFileW(
        string fileName,
        uint desiredAccess,
        uint shareMode,
        IntPtr securityAttributes,
        uint creationDisposition,
        uint flagsAndAttributes,
        IntPtr templateFile);

    [LibraryImport(Kernel32Api, EntryPoint = "DeviceIoControl", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool DeviceIoControl(
        SafeHandle device,
        uint ioControlCode,
        IntPtr inBuffer,
        uint inBufferSize,
        IntPtr outBuffer,
        uint outBufferSize,
        out uint bytesReturned,
        IntPtr overlapped);

    [LibraryImport(Kernel32Api,
        EntryPoint = "FindFirstVolumeW",
        StringMarshalling = StringMarshalling.Utf16,
        SetLastError = true)]
    internal static partial IntPtr FindFirstVolumeW([Out] char[] volumeName, uint bufferLength);

    [LibraryImport(Kernel32Api,
        EntryPoint = "FindNextVolumeW",
        StringMarshalling = StringMarshalling.Utf16,
        SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool FindNextVolumeW(IntPtr findVolume, [Out] char[] volumeName, uint bufferLength);

    [LibraryImport(Kernel32Api, EntryPoint = "FindVolumeClose", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool FindVolumeClose(IntPtr findVolume);

    [LibraryImport(Kernel32Api, EntryPoint = "GetDriveTypeW", StringMarshalling = StringMarshalling.Utf16)]
    internal static partial uint GetDriveTypeW(string rootPath);

    [LibraryImport(VirtDiskApi,
        EntryPoint = "GetVirtualDiskPhysicalPath",
        StringMarshalling = StringMarshalling.Utf16,
        SetLastError = false)]
    internal static partial int GetVirtualDiskPhysicalPath(
        VirtualDiskSafeHandle virtualDiskHandle,
        ref uint diskPathSizeInBytes,
        [Out] char[]? diskPath);

    [LibraryImport(VirtDiskApi,
        EntryPoint = "OpenVirtualDisk",
        StringMarshalling = StringMarshalling.Utf16,
        SetLastError = false)]
    internal static partial int OpenVirtualDisk(
        ref VIRTUAL_STORAGE_TYPE virtualStorageType,
        string path,
        uint virtualDiskAccessMask,
        uint flags,
        IntPtr parameters,
        out VirtualDiskSafeHandle handle);

    [StructLayout(LayoutKind.Sequential)]
    internal struct STORAGE_DEVICE_NUMBER
    {
        internal uint DeviceType;
        internal uint DeviceNumber;
        internal uint PartitionNumber;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct VIRTUAL_STORAGE_TYPE
    {
        internal uint DeviceId;
        internal Guid VendorId;
    }
}
