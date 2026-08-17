// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using System.Diagnostics;

namespace EventLogExpert.Runtime.Memory;

internal interface IProcessMemoryMeter
{
    long GetAvailablePhysicalBytes();

    long GetProcessUsedBytes(bool forceFullCollection);
}

internal sealed class ProcessMemoryMeter : IProcessMemoryMeter
{
    public long GetAvailablePhysicalBytes()
    {
        GCMemoryInfo info = GC.GetGCMemoryInfo();

        if (info.MemoryLoadBytes <= 0)
        {
            GC.Collect();
            info = GC.GetGCMemoryInfo();
        }

        return PhysicalMemory.AvailableBytesFrom(info.TotalAvailableMemoryBytes, info.MemoryLoadBytes);
    }

    public long GetProcessUsedBytes(bool forceFullCollection)
    {
        if (forceFullCollection) { GC.Collect(); }

        using Process self = Process.GetCurrentProcess();

        return self.PrivateMemorySize64;
    }
}
