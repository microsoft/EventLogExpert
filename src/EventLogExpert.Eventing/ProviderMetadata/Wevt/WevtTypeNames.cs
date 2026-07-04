// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using System.Diagnostics.CodeAnalysis;

namespace EventLogExpert.Eventing.ProviderMetadata.Wevt;

// OutType 0 uses winmeta defaults; live-validated non-zero outTypes include corrected 0x0e-0x11 and NTStatus casing.
internal static class WevtTypeNames
{
    internal const byte ArrayFlag = 0x80;

    private static readonly Dictionary<byte, string> s_defaultOutTypes = new()
    {
        [0x01] = "xs:string",        // win:UnicodeString
        [0x02] = "xs:string",        // win:AnsiString
        [0x03] = "xs:byte",          // win:Int8
        [0x04] = "xs:unsignedByte",  // win:UInt8
        [0x05] = "xs:short",         // win:Int16
        [0x06] = "xs:unsignedShort", // win:UInt16
        [0x07] = "xs:int",           // win:Int32
        [0x08] = "xs:unsignedInt",   // win:UInt32
        [0x09] = "xs:long",          // win:Int64
        [0x0a] = "xs:unsignedLong",  // win:UInt64
        [0x0b] = "xs:float",         // win:Float (canonical default; unobserved in the probe corpus)
        [0x0c] = "xs:double",        // win:Double
        [0x0d] = "xs:boolean",       // win:Boolean
        [0x0e] = "xs:hexBinary",     // win:Binary
        [0x0f] = "xs:GUID",          // win:GUID
        [0x10] = "win:HexInt64",     // win:Pointer
        [0x11] = "xs:dateTime",      // win:FILETIME
        [0x12] = "xs:dateTime",      // win:SYSTEMTIME
        [0x13] = "xs:string",        // win:SID
        [0x14] = "win:HexInt32",     // win:HexInt32
        [0x15] = "win:HexInt64"      // win:HexInt64
    };

    private static readonly Dictionary<byte, string> s_inTypes = new()
    {
        [0x01] = "win:UnicodeString",
        [0x02] = "win:AnsiString",
        [0x03] = "win:Int8",
        [0x04] = "win:UInt8",
        [0x05] = "win:Int16",
        [0x06] = "win:UInt16",
        [0x07] = "win:Int32",
        [0x08] = "win:UInt32",
        [0x09] = "win:Int64",
        [0x0a] = "win:UInt64",
        [0x0b] = "win:Float",
        [0x0c] = "win:Double",
        [0x0d] = "win:Boolean",
        [0x0e] = "win:Binary",
        [0x0f] = "win:GUID",
        [0x10] = "win:Pointer",
        [0x11] = "win:FILETIME",
        [0x12] = "win:SYSTEMTIME",
        [0x13] = "win:SID",
        [0x14] = "win:HexInt32",
        [0x15] = "win:HexInt64"
    };

    private static readonly Dictionary<byte, string> s_outTypes = new()
    {
        [0x01] = "xs:string",
        [0x02] = "xs:dateTime",
        [0x03] = "xs:byte",
        [0x04] = "xs:unsignedByte",
        [0x05] = "xs:short",
        [0x06] = "xs:unsignedShort",
        [0x07] = "xs:int",
        [0x08] = "xs:unsignedInt",
        [0x09] = "xs:long",
        [0x0a] = "xs:unsignedLong",
        [0x0b] = "xs:float",
        [0x0c] = "xs:double",
        [0x0d] = "xs:boolean",
        [0x0e] = "xs:GUID",
        [0x0f] = "xs:hexBinary",
        [0x10] = "win:HexInt8",
        [0x11] = "win:HexInt16",
        [0x12] = "win:HexInt32",
        [0x13] = "win:HexInt64",
        [0x14] = "win:PID",
        [0x15] = "win:TID",
        [0x16] = "win:Port",
        [0x17] = "win:IPv4",
        [0x18] = "win:IPv6",
        [0x19] = "win:SocketAddress",
        [0x1a] = "win:CIMDateTime",
        [0x1b] = "win:ETWTIME",
        [0x1c] = "win:Xml",
        [0x1d] = "win:ErrorCode",
        [0x1e] = "win:Win32Error",
        [0x1f] = "win:NTStatus",
        [0x20] = "win:Hresult"
    };

    internal static bool TryGetInType(byte inType, [MaybeNullWhen(false)] out string value) =>
        s_inTypes.TryGetValue((byte)(inType & ~ArrayFlag), out value);

    internal static bool TryGetOutType(byte inType, byte outType, [MaybeNullWhen(false)] out string value) =>
        outType == 0
            ? s_defaultOutTypes.TryGetValue((byte)(inType & ~ArrayFlag), out value)
            : s_outTypes.TryGetValue(outType, out value);
}
