// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Eventing.Helpers;
using EventLogExpert.Eventing.Models;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace EventLogExpert.Eventing.Tests.Helpers;

public sealed class EventMethodsTests
{
    [Fact]
    public void ConvertVariant_WhenAnsiString_ShouldReturnString()
    {
        // Arrange
        var testString = "Test ANSI String";
        IntPtr stringPtr = Marshal.StringToHGlobalAnsi(testString);

        try
        {
            var variant = CreateVariant(EvtVariantType.AnsiString, stringPtr);

            // Act
            var result = EventMethods.ConvertVariant(variant);

            // Assert
            Assert.NotNull(result);
            Assert.IsType<string>(result);
            Assert.Equal(testString, result);
        }
        finally
        {
            Marshal.FreeHGlobal(stringPtr);
        }
    }

    [Fact]
    public void ConvertVariant_WhenBinary_ShouldReturnByteArray()
    {
        // Arrange
        byte[] expectedBytes = [1, 2, 3, 4, 5];
        IntPtr binaryPtr = Marshal.AllocHGlobal(expectedBytes.Length);

        try
        {
            Marshal.Copy(expectedBytes, 0, binaryPtr, expectedBytes.Length);

            var variant = CreateVariantWithCount(EvtVariantType.Binary, binaryPtr, (uint)expectedBytes.Length);

            // Act
            var result = EventMethods.ConvertVariant(variant);

            // Assert
            Assert.NotNull(result);
            Assert.IsType<byte[]>(result);
            Assert.Equal(expectedBytes, (byte[])result);
        }
        finally
        {
            Marshal.FreeHGlobal(binaryPtr);
        }
    }

    [Fact]
    public void ConvertVariant_WhenBooleanFalse_ShouldReturnFalse()
    {
        // Arrange
        var variant = CreateVariant(EvtVariantType.Boolean, 0u);

        // Act
        var result = EventMethods.ConvertVariant(variant);

        // Assert
        Assert.NotNull(result);
        Assert.IsType<bool>(result);
        Assert.False((bool)result);
    }

    [Fact]
    public void ConvertVariant_WhenBooleanTrue_ShouldReturnTrue()
    {
        // Arrange
        var variant = CreateVariant(EvtVariantType.Boolean, 1u);

        // Act
        var result = EventMethods.ConvertVariant(variant);

        // Assert
        Assert.NotNull(result);
        Assert.IsType<bool>(result);
        Assert.True((bool)result);
    }

    [Fact]
    public void ConvertVariant_WhenByte_ShouldReturnByte()
    {
        // Arrange
        byte expectedValue = 255;
        var variant = CreateVariant(EvtVariantType.Byte, expectedValue);

        // Act
        var result = EventMethods.ConvertVariant(variant);

        // Assert
        Assert.NotNull(result);
        Assert.IsType<byte>(result);
        Assert.Equal(expectedValue, result);
    }

    [Fact]
    public void ConvertVariant_WhenDouble_ShouldReturnDouble()
    {
        // Arrange
        double expectedValue = 3.141592653589793;
        var variant = CreateVariant(EvtVariantType.Double, expectedValue);

        // Act
        var result = EventMethods.ConvertVariant(variant);

        // Assert
        Assert.NotNull(result);
        Assert.IsType<double>(result);
        Assert.Equal(expectedValue, result);
    }

    [Fact]
    public void ConvertVariant_WhenFileTime_ShouldReturnDateTime()
    {
        // Arrange
        var expectedDateTime = new DateTime(2024, 3, 15, 10, 30, 0, DateTimeKind.Utc);
        ulong fileTime = (ulong)expectedDateTime.ToFileTimeUtc();

        var variant = CreateVariant(EvtVariantType.FileTime, fileTime);

        // Act
        var result = EventMethods.ConvertVariant(variant);

        // Assert
        Assert.NotNull(result);
        Assert.IsType<DateTime>(result);
        Assert.Equal(expectedDateTime, result);
    }

    [Fact]
    public void ConvertVariant_WhenGuid_ShouldReturnGuid()
    {
        // Arrange
        var expectedGuid = Guid.NewGuid();
        IntPtr guidPtr = Marshal.AllocHGlobal(Marshal.SizeOf<Guid>());

        try
        {
            Marshal.StructureToPtr(expectedGuid, guidPtr, false);

            var variant = CreateVariant(EvtVariantType.Guid, guidPtr);

            // Act
            var result = EventMethods.ConvertVariant(variant);

            // Assert
            Assert.NotNull(result);
            Assert.IsType<Guid>(result);
            Assert.Equal(expectedGuid, result);
        }
        finally
        {
            Marshal.FreeHGlobal(guidPtr);
        }
    }

    [Fact]
    public void ConvertVariant_WhenHexInt32_ShouldReturnInt32()
    {
        // Arrange
        int expectedValue = unchecked((int)0x1234ABCD);
        var variant = CreateVariant(EvtVariantType.HexInt32, expectedValue);

        // Act
        var result = EventMethods.ConvertVariant(variant);

        // Assert
        Assert.NotNull(result);
        Assert.IsType<int>(result);
        Assert.Equal(expectedValue, result);
    }

    [Fact]
    public void ConvertVariant_WhenHexInt64_ShouldReturnUInt64()
    {
        // Arrange
        ulong expectedValue = 0x123456789ABCDEF0;
        var variant = CreateVariant(EvtVariantType.HexInt64, expectedValue);

        // Act
        var result = EventMethods.ConvertVariant(variant);

        // Assert
        Assert.NotNull(result);
        Assert.IsType<ulong>(result);
        Assert.Equal(expectedValue, result);
    }

    [Fact]
    public void ConvertVariant_WhenInt16_ShouldReturnShort()
    {
        // Arrange
        short expectedValue = -1234;
        var variant = CreateVariant(EvtVariantType.Int16, expectedValue);

        // Act
        var result = EventMethods.ConvertVariant(variant);

        // Assert
        Assert.NotNull(result);
        Assert.IsType<short>(result);
        Assert.Equal(expectedValue, result);
    }

    [Fact]
    public void ConvertVariant_WhenInt32_ShouldReturnInt32()
    {
        // Arrange
        int expectedValue = -12345;
        var variant = CreateVariant(EvtVariantType.Int32, expectedValue);

        // Act
        var result = EventMethods.ConvertVariant(variant);

        // Assert
        Assert.NotNull(result);
        Assert.IsType<int>(result);
        Assert.Equal(expectedValue, result);
    }

    [Fact]
    public void ConvertVariant_WhenInt64_ShouldReturnInt64()
    {
        // Arrange
        long expectedValue = -9000000000000000000;
        var variant = CreateVariant(EvtVariantType.Int64, expectedValue);

        // Act
        var result = EventMethods.ConvertVariant(variant);

        // Assert
        Assert.NotNull(result);
        Assert.IsType<long>(result);
        Assert.Equal(expectedValue, result);
    }

    [Fact]
    public void ConvertVariant_WhenInvalidType_ShouldThrowInvalidDataException()
    {
        // Arrange
        var variant = CreateVariant((EvtVariantType)9999);

        // Act & Assert
        var exception = Assert.Throws<InvalidDataException>(
            () => EventMethods.ConvertVariant(variant));

        Assert.Contains(nameof(EvtVariantType), exception.Message);
        Assert.Contains("9999", exception.Message);
    }

    [Fact]
    public void ConvertVariant_WhenNull_ShouldReturnNull()
    {
        // Arrange
        var variant = CreateVariant(EvtVariantType.Null);

        // Act
        var result = EventMethods.ConvertVariant(variant);

        // Assert
        Assert.Null(result);
    }

    [Fact]
    public void ConvertVariant_WhenSByte_ShouldReturnByte()
    {
        // Arrange
        byte expectedValue = 127;
        var variant = CreateVariant(EvtVariantType.SByte, expectedValue);

        // Act
        var result = EventMethods.ConvertVariant(variant);

        // Assert
        Assert.NotNull(result);
        Assert.IsType<byte>(result);
        Assert.Equal(expectedValue, result);
    }

    [Fact]
    public void ConvertVariant_WhenSidNull_ShouldReturnNull()
    {
        // Arrange
        var variant = CreateVariant(EvtVariantType.Sid, IntPtr.Zero);

        // Act
        var result = EventMethods.ConvertVariant(variant);

        // Assert
        Assert.Null(result);
    }

    [Fact]
    public void ConvertVariant_WhenSingle_ShouldReturnFloat()
    {
        // Arrange
        float expectedValue = 3.14159f;
        var variant = CreateVariant(EvtVariantType.Single, expectedValue);

        // Act
        var result = EventMethods.ConvertVariant(variant);

        // Assert
        Assert.NotNull(result);
        Assert.IsType<float>(result);
        Assert.Equal(expectedValue, result);
    }

    [Fact]
    public void ConvertVariant_WhenSizeT_ShouldReturnIntPtr()
    {
        // Arrange
        nint expectedValue = 12345;
        var variant = CreateVariant(EvtVariantType.SizeT, expectedValue);

        // Act
        var result = EventMethods.ConvertVariant(variant);

        // Assert
        Assert.NotNull(result);
        Assert.IsType<nint>(result);
        Assert.Equal(expectedValue, result);
    }

    [Fact]
    public void ConvertVariant_WhenString_ShouldReturnString()
    {
        // Arrange
        var testString = "Test String";
        IntPtr stringPtr = Marshal.StringToHGlobalUni(testString);

        try
        {
            var variant = CreateVariant(EvtVariantType.String, stringPtr);

            // Act
            var result = EventMethods.ConvertVariant(variant);

            // Assert
            Assert.NotNull(result);
            Assert.IsType<string>(result);
            Assert.Equal(testString, result);
        }
        finally
        {
            Marshal.FreeHGlobal(stringPtr);
        }
    }

    [Fact]
    public void ConvertVariant_WhenStringArray_ShouldReturnStringArray()
    {
        // Arrange
        string[] expectedStrings = ["First", "Second", "Third"];
        IntPtr[] stringPtrs = new IntPtr[expectedStrings.Length];
        IntPtr arrayPtr = Marshal.AllocHGlobal(IntPtr.Size * expectedStrings.Length);

        try
        {
            for (int i = 0; i < expectedStrings.Length; i++)
            {
                stringPtrs[i] = Marshal.StringToHGlobalUni(expectedStrings[i]);
                Marshal.WriteIntPtr(arrayPtr, i * IntPtr.Size, stringPtrs[i]);
            }

            var variant = CreateVariantWithCount(EvtVariantType.StringArray, arrayPtr, (uint)expectedStrings.Length);

            // Act
            var result = EventMethods.ConvertVariant(variant);

            // Assert
            Assert.NotNull(result);
            Assert.IsType<string[]>(result);
            Assert.Equal(expectedStrings, (string[])result);
        }
        finally
        {
            foreach (var ptr in stringPtrs)
            {
                if (ptr != IntPtr.Zero)
                {
                    Marshal.FreeHGlobal(ptr);
                }
            }

            Marshal.FreeHGlobal(arrayPtr);
        }
    }

    [Fact]
    public void ConvertVariant_WhenStringArrayEmpty_ShouldReturnEmptyArray()
    {
        // Arrange
        var variant = CreateVariantWithCount(EvtVariantType.StringArray, IntPtr.Zero, 0);

        // Act
        var result = EventMethods.ConvertVariant(variant);

        // Assert
        Assert.NotNull(result);
        Assert.IsType<string[]>(result);
        Assert.Empty((string[])result);
    }

    [Fact]
    public void ConvertVariant_WhenSysTime_ShouldReturnDateTime()
    {
        // Arrange
        var expectedDateTime = new DateTime(2024, 3, 15, 14, 30, 45, 123, DateTimeKind.Utc);
        IntPtr sysTimePtr = Marshal.AllocHGlobal(Marshal.SizeOf<SystemTime>());

        try
        {
            unsafe
            {
                short* sysTime = (short*)sysTimePtr;
                sysTime[0] = 2024; // Year
                sysTime[1] = 3;    // Month
                sysTime[2] = 5;    // DayOfWeek
                sysTime[3] = 15;   // Day
                sysTime[4] = 14;   // Hour
                sysTime[5] = 30;   // Minute
                sysTime[6] = 45;   // Second
                sysTime[7] = 123;  // Milliseconds
            }

            var variant = CreateVariant(EvtVariantType.SysTime, sysTimePtr);

            // Act
            var result = EventMethods.ConvertVariant(variant);

            // Assert
            Assert.NotNull(result);
            Assert.IsType<DateTime>(result);
            Assert.Equal(expectedDateTime, result);
        }
        finally
        {
            Marshal.FreeHGlobal(sysTimePtr);
        }
    }

    [Fact]
    public void ConvertVariant_WhenUInt16_ShouldReturnUInt16()
    {
        // Arrange
        ushort expectedValue = 65000;
        var variant = CreateVariant(EvtVariantType.UInt16, expectedValue);

        // Act
        var result = EventMethods.ConvertVariant(variant);

        // Assert
        Assert.NotNull(result);
        Assert.IsType<ushort>(result);
        Assert.Equal(expectedValue, result);
    }

    [Fact]
    public void ConvertVariant_WhenUInt32_ShouldReturnUInt32()
    {
        // Arrange
        uint expectedValue = 4000000000;
        var variant = CreateVariant(EvtVariantType.UInt32, expectedValue);

        // Act
        var result = EventMethods.ConvertVariant(variant);

        // Assert
        Assert.NotNull(result);
        Assert.IsType<uint>(result);
        Assert.Equal(expectedValue, result);
    }

    [Fact]
    public void ConvertVariant_WhenUInt64_ShouldReturnUInt64()
    {
        // Arrange
        ulong expectedValue = 18000000000000000000;
        var variant = CreateVariant(EvtVariantType.UInt64, expectedValue);

        // Act
        var result = EventMethods.ConvertVariant(variant);

        // Assert
        Assert.NotNull(result);
        Assert.IsType<ulong>(result);
        Assert.Equal(expectedValue, result);
    }

    [Fact]
    public void ConvertVariant_WhenXml_ShouldReturnString()
    {
        // Arrange
        var testXml = "<Event><Data>TestValue</Data></Event>";
        IntPtr xmlPtr = Marshal.StringToHGlobalUni(testXml);

        try
        {
            var variant = CreateVariant(EvtVariantType.Xml, xmlPtr);

            // Act
            var result = EventMethods.ConvertVariant(variant);

            // Assert
            Assert.NotNull(result);
            Assert.IsType<string>(result);
            Assert.Equal(testXml, result);
        }
        finally
        {
            Marshal.FreeHGlobal(xmlPtr);
        }
    }

    [Fact]
    public void ThrowEventLogException_WhenAccessDenied_ShouldThrowUnauthorizedAccessException()
    {
        // Arrange
        int error = Interop.ERROR_ACCESS_DENIED;

        // Act & Assert
        Assert.Throws<UnauthorizedAccessException>(() => EventMethods.ThrowEventLogException(error));
    }

    [Fact]
    public void ThrowEventLogException_WhenCancelled_ShouldThrowOperationCanceledException()
    {
        // Arrange
        int error = Interop.ERROR_CANCELLED;

        // Act & Assert
        Assert.Throws<OperationCanceledException>(() => EventMethods.ThrowEventLogException(error));
    }

    [Fact]
    public void ThrowEventLogException_WhenChannelNotFound_ShouldThrowFileNotFoundException()
    {
        // Arrange
        int error = Interop.ERROR_EVT_CHANNEL_NOT_FOUND;

        // Act & Assert
        Assert.Throws<FileNotFoundException>(() => EventMethods.ThrowEventLogException(error));
    }

    [Fact]
    public void ThrowEventLogException_WhenFileNotFound_ShouldThrowFileNotFoundException()
    {
        // Arrange
        int error = Interop.ERROR_FILE_NOT_FOUND;

        // Act & Assert
        Assert.Throws<FileNotFoundException>(() => EventMethods.ThrowEventLogException(error));
    }

    [Fact]
    public void ThrowEventLogException_WhenInvalidData_ShouldThrowInvalidDataException()
    {
        // Arrange
        int error = Interop.ERROR_INVALID_DATA;

        // Act & Assert
        Assert.Throws<InvalidDataException>(() => EventMethods.ThrowEventLogException(error));
    }

    [Fact]
    public void ThrowEventLogException_WhenInvalidEventData_ShouldThrowInvalidDataException()
    {
        // Arrange
        int error = Interop.ERROR_EVT_INVALID_EVENT_DATA;

        // Act & Assert
        Assert.Throws<InvalidDataException>(() => EventMethods.ThrowEventLogException(error));
    }

    [Fact]
    public void ThrowEventLogException_WhenInvalidHandle_ShouldThrowUnauthorizedAccessException()
    {
        // Arrange
        int error = Interop.ERROR_INVALID_HANDLE;

        // Act & Assert
        Assert.Throws<UnauthorizedAccessException>(() => EventMethods.ThrowEventLogException(error));
    }

    [Fact]
    public void ThrowEventLogException_WhenMessageIdNotFound_ShouldThrowFileNotFoundException()
    {
        // Arrange
        int error = Interop.ERROR_EVT_MESSAGE_ID_NOT_FOUND;

        // Act & Assert
        Assert.Throws<FileNotFoundException>(() => EventMethods.ThrowEventLogException(error));
    }

    [Fact]
    public void ThrowEventLogException_WhenMessageNotFound_ShouldThrowFileNotFoundException()
    {
        // Arrange
        int error = Interop.ERROR_EVT_MESSAGE_NOT_FOUND;

        // Act & Assert
        Assert.Throws<FileNotFoundException>(() => EventMethods.ThrowEventLogException(error));
    }

    [Fact]
    public void ThrowEventLogException_WhenPathNotFound_ShouldThrowFileNotFoundException()
    {
        // Arrange
        int error = Interop.ERROR_PATH_NOT_FOUND;

        // Act & Assert
        Assert.Throws<FileNotFoundException>(() => EventMethods.ThrowEventLogException(error));
    }

    [Fact]
    public void ThrowEventLogException_WhenPublisherMetadataNotFound_ShouldThrowFileNotFoundException()
    {
        // Arrange
        int error = Interop.ERROR_EVT_PUBLISHER_METADATA_NOT_FOUND;

        // Act & Assert
        Assert.Throws<FileNotFoundException>(() => EventMethods.ThrowEventLogException(error));
    }

    [Fact]
    public void ThrowEventLogException_WhenRpcCallCanceled_ShouldThrowOperationCanceledException()
    {
        // Arrange
        int error = Interop.RPC_S_CALL_CANCELED;

        // Act & Assert
        Assert.Throws<OperationCanceledException>(() => EventMethods.ThrowEventLogException(error));
    }

    [Fact]
    public void ThrowEventLogException_WhenUnknownError_ShouldThrowException()
    {
        // Arrange
        int error = 9999; // Unknown error code

        // Act & Assert
        Assert.Throws<Exception>(() => EventMethods.ThrowEventLogException(error));
    }

    // Helper methods to create EvtVariant instances
    private static EvtVariant CreateVariant(EvtVariantType type, object? value = null)
    {
        return CreateVariantWithCount(type, value, 0);
    }

    private static EvtVariant CreateVariantWithCount(EvtVariantType type, object? value, uint count)
    {
        IntPtr buffer = Marshal.AllocHGlobal(Marshal.SizeOf<EvtVariant>());

        try
        {
            unsafe
            {
                // Zero out the memory
                Unsafe.InitBlock((void*)buffer, 0, (uint)Marshal.SizeOf<EvtVariant>());

                // Set the type at offset 12 (based on EvtVariant structure)
                *(uint*)(buffer + 12) = (uint)type;

                // Set the count at offset 8
                *(uint*)(buffer + 8) = count;

                // Set the value based on type
                if (value == null)
                {
                    return Marshal.PtrToStructure<EvtVariant>(buffer);
                }

                switch (type)
                {
                    case EvtVariantType.String:
                    case EvtVariantType.AnsiString:
                    case EvtVariantType.Xml:
                    case EvtVariantType.Sid:
                    case EvtVariantType.Guid:
                    case EvtVariantType.SysTime:
                    case EvtVariantType.Binary:
                    case EvtVariantType.StringArray:
                        *(nint*)buffer = (IntPtr)value;
                        break;
                    case EvtVariantType.SByte:
                    case EvtVariantType.Byte:
                        *(byte*)buffer = (byte)value;
                        break;
                    case EvtVariantType.Int16:
                        *(short*)buffer = (short)value;
                        break;
                    case EvtVariantType.UInt16:
                        *(ushort*)buffer = (ushort)value;
                        break;
                    case EvtVariantType.Int32:
                    case EvtVariantType.HexInt32:
                        *(int*)buffer = (int)value;
                        break;
                    case EvtVariantType.UInt32:
                    case EvtVariantType.Boolean:
                        *(uint*)buffer = (uint)value;
                        break;
                    case EvtVariantType.Int64:
                        *(long*)buffer = (long)value;
                        break;
                    case EvtVariantType.UInt64:
                    case EvtVariantType.FileTime:
                    case EvtVariantType.HexInt64:
                        *(ulong*)buffer = (ulong)value;
                        break;
                    case EvtVariantType.Single:
                        *(float*)buffer = (float)value;
                        break;
                    case EvtVariantType.Double:
                        *(double*)buffer = (double)value;
                        break;
                    case EvtVariantType.SizeT:
                        *(nint*)buffer = (nint)value;
                        break;
                }

                return Marshal.PtrToStructure<EvtVariant>(buffer);
            }
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
    }
}
