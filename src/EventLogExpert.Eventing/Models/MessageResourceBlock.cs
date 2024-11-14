// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using System.Runtime.InteropServices;

namespace EventLogExpert.Eventing.Models;

[StructLayout(LayoutKind.Sequential)]
internal struct MessageResourceBlock
{
    internal int LowId;
    internal int HighId;
    internal int OffsetToEntries;
}
