// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using System.Runtime.InteropServices;

namespace EventLogExpert.Eventing.Models;

[StructLayout(LayoutKind.Sequential)]
internal readonly record struct MessageResourceBlock
{
    internal readonly int LowId;
    internal readonly int HighId;
    internal readonly int OffsetToEntries;
}
