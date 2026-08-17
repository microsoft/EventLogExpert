// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

namespace EventLogExpert.Runtime.Memory;

internal static class PhysicalMemory
{
    internal static long AvailableBytesFrom(long totalPhysicalBytes, long loadBytes)
    {
        if (loadBytes <= 0) { return 0; }

        long available = totalPhysicalBytes - loadBytes;

        return available > 0 ? available : 0;
    }
}
