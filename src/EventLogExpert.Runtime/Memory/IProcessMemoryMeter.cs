// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using System.Diagnostics;

namespace EventLogExpert.Runtime.Memory;

/// <summary>
///     Reads process memory for the advisory status-bar indicator. All reads are cheap and non-collecting; the only
///     collection is the explicit, infrequent <see cref="RequestBackgroundReclaim" /> issued after a log is closed so the
///     freed managed heap becomes visible.
/// </summary>
internal interface IProcessMemoryMeter
{
    /// <summary>
    ///     Free physical memory (total minus the system-wide memory load), or 0 when it cannot yet be determined (no GC
    ///     has run, so the load reading is not populated). Used once to size the advisory color bands to the headroom actually
    ///     available to the app.
    /// </summary>
    long GetAvailablePhysicalBytes();

    /// <summary>The current managed-heap size (does not force a collection).</summary>
    long GetManagedHeapBytes();

    /// <summary>The current process working set (OS-resident bytes).</summary>
    long GetWorkingSetBytes();

    /// <summary>
    ///     Requests a non-blocking background gen2 collection so a just-closed log's managed memory is reclaimed and the
    ///     indicator can reflect the drop. Never blocks the caller.
    /// </summary>
    void RequestBackgroundReclaim();
}

internal sealed class ProcessMemoryMeter : IProcessMemoryMeter
{
    public long GetAvailablePhysicalBytes()
    {
        GCMemoryInfo info = GC.GetGCMemoryInfo();

        // MemoryLoadBytes is 0 until the first GC populates it; return 0 so the caller keeps the bands unsized (Normal)
        // rather than treating the whole machine as free.
        return info.MemoryLoadBytes > 0 && info.TotalAvailableMemoryBytes > info.MemoryLoadBytes ?
            info.TotalAvailableMemoryBytes - info.MemoryLoadBytes : 0;
    }

    public long GetManagedHeapBytes() => GC.GetTotalMemory(forceFullCollection: false);

    public long GetWorkingSetBytes()
    {
        using Process self = Process.GetCurrentProcess();

        return self.WorkingSet64;
    }

    public void RequestBackgroundReclaim() =>
        GC.Collect(2, GCCollectionMode.Forced, blocking: false, compacting: false);
}
