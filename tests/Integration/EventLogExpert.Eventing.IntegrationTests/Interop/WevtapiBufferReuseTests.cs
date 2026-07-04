// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Eventing.Common.Channels;
using EventLogExpert.Eventing.Interop;
using EventLogExpert.Eventing.PublisherMetadata;
using EventLogExpert.Eventing.Readers;
using EventLogExpert.Eventing.TestUtils.Constants;
using System.Runtime.InteropServices;

namespace EventLogExpert.Eventing.IntegrationTests.Interop;

// Item 1: validates the wevtapi [ThreadStatic] buffer-reuse rewrite of NativeMethods.RenderEvent* / FormatMessage /
// GetObjectArrayProperty - the skip-probe P/Invoke counts and the continuous-pin GC-move guarantee. Real wevtapi ->
// container-gated integration tier (no committed .evtx exists; the convention is ProviderMetadata.Create + live
// handles). The P/Invoke counters and the BeforeVariantReadForTest hook are #if DEBUG-only seams, so the tests that
// use them are #if DEBUG-guarded.
public sealed class WevtapiBufferReuseTests
{
    [Fact]
    public void FormatMessage_WhenUnknownMessageId_Throws()
    {
        using ProviderMetadata? metadata = ProviderMetadata.Create(Constants.SecurityAuditingLogName);
        Assert.SkipUnless(metadata is not null, "Requires the Microsoft-Windows-Security-Auditing provider on the host.");

        // 0xFFFFFFFE is not a resolvable resource id. The rewrite preserves the pre-existing behavior: any error that is
        // not a tolerated unresolved-insert and not ERROR_INSUFFICIENT_BUFFER is surfaced via ThrowEventLogException -
        // it does NOT degrade to string.Empty (callers depend on the throw/degrade path via ToRawContent's try/catch).
        // The concrete type varies by id / provider / host locale (FileNotFoundException for MESSAGE_ID_NOT_FOUND, a
        // generic Exception for a missing locale resource), so assert only that it throws.
        Assert.ThrowsAny<Exception>(() => metadata!.FormatMessageById(0xFFFFFFFEu));
    }

#if DEBUG
    private const uint NoMessage = uint.MaxValue;

    [Fact]
    public void RenderEvent_WhenWarmed_IssuesSingleProbelessPInvoke()
    {
        using EvtHandle handle = FirstEventHandle(Constants.ApplicationLogName);

        _ = NativeMethods.RenderEvent(handle);   // warm the [ThreadStatic] buffer on this thread
        NativeMethods.RenderPInvokeCountForTest = 0;
        _ = NativeMethods.RenderEvent(handle);

        Assert.Equal(1, NativeMethods.RenderPInvokeCountForTest);
    }

    [Fact]
    public void FormatMessage_WhenWarmed_IssuesSingleProbelessPInvoke()
    {
        using ProviderMetadata? metadata = ProviderMetadata.Create(Constants.SecurityAuditingLogName);
        Assert.SkipUnless(metadata is not null, "Requires the Microsoft-Windows-Security-Auditing provider on the host.");

        uint messageId = FirstResolvableMessageId(metadata!);
        Assert.SkipUnless(messageId != NoMessage, "No resolvable message id on this provider.");

        _ = metadata!.FormatMessageById(messageId);   // warm
        NativeMethods.FormatPInvokeCountForTest = 0;
        _ = metadata.FormatMessageById(messageId);

        Assert.Equal(1, NativeMethods.FormatPInvokeCountForTest);
    }

    [Fact]
    public void RenderEvent_WhenCompactingGcDuringVariantRead_ReturnsIntactRecord()
    {
        using EvtHandle handle = FirstEventHandle(Constants.ApplicationLogName);

        EventRecord expected = NativeMethods.RenderEvent(handle);

        try
        {
            NativeMethods.BeforeVariantReadForTest = ForceCompactingGc;
            EventRecord actual = NativeMethods.RenderEvent(handle);

            Assert.Equal(expected.Id, actual.Id);
            Assert.Equal(expected.ProviderName, actual.ProviderName);
            Assert.Equal(expected.ComputerName, actual.ComputerName);
            Assert.Equal(expected.LogName, actual.LogName);
        }
        finally
        {
            NativeMethods.BeforeVariantReadForTest = null;
        }
    }

    [Fact]
    public void RenderEventProperties_WhenCompactingGcDuringVariantRead_ReturnsIntactValues()
    {
        using EvtHandle handle = FirstEventHandle(Constants.ApplicationLogName);

        var expected = NativeMethods.RenderEventProperties(handle).ToList();

        try
        {
            NativeMethods.BeforeVariantReadForTest = ForceCompactingGc;
            var actual = NativeMethods.RenderEventProperties(handle).ToList();

            Assert.Equal(expected, actual);
        }
        finally
        {
            NativeMethods.BeforeVariantReadForTest = null;
        }
    }

    [Fact]
    public void GetObjectArrayProperty_WhenCompactingGcDuringVariantRead_ReturnsIntactChannels()
    {
        using ProviderMetadata? reference = ProviderMetadata.Create(Constants.SecurityAuditingLogName);
        Assert.SkipUnless(reference is not null, "Requires the Microsoft-Windows-Security-Auditing provider on the host.");

        var expected = reference!.ToRawContent(Constants.SecurityAuditingLogName, null).Channels;
        Assert.NotEmpty(expected);

        using ProviderMetadata? underGc = ProviderMetadata.Create(Constants.SecurityAuditingLogName);
        Assert.SkipUnless(underGc is not null, "Requires the Microsoft-Windows-Security-Auditing provider on the host.");

        try
        {
            NativeMethods.BeforeVariantReadForTest = ForceCompactingGc;
            var actual = underGc!.ToRawContent(Constants.SecurityAuditingLogName, null).Channels;

            Assert.Equal(expected, actual);
        }
        finally
        {
            NativeMethods.BeforeVariantReadForTest = null;
        }
    }

    // Forces a compacting collection with a released heap gap so the movable [ThreadStatic] scratch buffer is a
    // relocation candidate at the moment of the variant read. If the buffer is (correctly) pinned across the read, the
    // marshalled strings/SIDs survive; a future edit that read the variant OUTSIDE the fixed scope would corrupt or AV.
    private static void ForceCompactingGc()
    {
        byte[]? gap = new byte[1 << 20];
        GC.KeepAlive(gap);
        gap = null;
        GC.Collect(2, GCCollectionMode.Forced, blocking: true, compacting: true);
        GC.WaitForPendingFinalizers();
        GC.Collect(2, GCCollectionMode.Forced, blocking: true, compacting: true);
    }

    private static uint FirstResolvableMessageId(ProviderMetadata metadata)
    {
        RawProviderContent content = metadata.ToRawContent(Constants.SecurityAuditingLogName, null);

        var candidates = content.Events.Select(providerEvent => providerEvent.MessageId)
            .Concat(content.Keywords.Select(keyword => keyword.MessageId))
            .Where(messageId => messageId != NoMessage && messageId != 0);

        foreach (uint messageId in candidates)
        {
            try
            {
                if (!string.IsNullOrEmpty(metadata.FormatMessageById(messageId))) { return messageId; }
            }
            catch (Exception ex) when (ex is not OutOfMemoryException and not StackOverflowException)
            {
                // Skip ids that do not resolve on this host and keep looking.
            }
        }

        return NoMessage;
    }

    private static EvtHandle FirstEventHandle(string channelName)
    {
        EvtHandle query = NativeMethods.EvtQuery(EventLogSession.GlobalSession.Handle, channelName, null, LogPathType.Channel);
        Assert.False(query.IsInvalid, $"EvtQuery failed for channel '{channelName}'.");

        try
        {
            var batch = new IntPtr[1];
            int returned = 0;

            Assert.True(
                NativeMethods.EvtNext(query, batch.Length, batch, 0, 0, ref returned) && returned == 1,
                $"Channel '{channelName}' has no readable events.");

            return new EvtHandle(batch[0]);
        }
        finally
        {
            query.Dispose();
        }
    }

    [Fact]
    public void RenderEvent_WhenItemExceedsInitialBuffer_GrowsRetriesThenReuses()
    {
        using EvtHandle handle = FirstEventHandle(Constants.ApplicationLogName);
        EventRecord expected = NativeMethods.RenderEvent(handle);

        int savedInitial = NativeMethods.InitialRenderChars;

        try
        {
            NativeMethods.InitialRenderChars = 1;   // force the initial render to under-fit -> grow + retry
            NativeMethods.ResetRenderScratchForTest();

            EventRecord grown = NativeMethods.RenderEvent(handle);

            Assert.Equal(2, NativeMethods.RenderPInvokeCountForTest);   // probe-less: failed render + one retry
            Assert.Equal(expected.Id, grown.Id);
            Assert.Equal(expected.ProviderName, grown.ProviderName);
            Assert.Equal(expected.ComputerName, grown.ComputerName);

            NativeMethods.RenderPInvokeCountForTest = 0;
            _ = NativeMethods.RenderEvent(handle);

            Assert.Equal(1, NativeMethods.RenderPInvokeCountForTest);   // buffer grew for good -> single call thereafter
        }
        finally
        {
            NativeMethods.InitialRenderChars = savedInitial;
            NativeMethods.ResetRenderScratchForTest();
        }
    }

    [Fact]
    public void RenderEvent_WhenItemExceedsRetentionCap_UsesTransientAndKeepsRetainedBounded()
    {
        using EvtHandle handle = FirstEventHandle(Constants.ApplicationLogName);
        EventRecord expected = NativeMethods.RenderEvent(handle);

        int savedInitial = NativeMethods.InitialRenderChars;
        int savedCap = NativeMethods.MaxRetainedChars;

        try
        {
            NativeMethods.InitialRenderChars = 8;
            NativeMethods.MaxRetainedChars = 8;   // any real event exceeds this -> transient array, not retained
            NativeMethods.ResetRenderScratchForTest();

            EventRecord viaTransient = NativeMethods.RenderEvent(handle);

            Assert.Equal(expected.Id, viaTransient.Id);   // correct output produced through the transient buffer
            Assert.Equal(expected.ProviderName, viaTransient.ProviderName);
            Assert.True(
                NativeMethods.RetainedRenderBufferChars is int retained && retained <= NativeMethods.MaxRetainedChars,
                $"retained {NativeMethods.RetainedRenderBufferChars} exceeded cap {NativeMethods.MaxRetainedChars}");
        }
        finally
        {
            NativeMethods.InitialRenderChars = savedInitial;
            NativeMethods.MaxRetainedChars = savedCap;
            NativeMethods.ResetRenderScratchForTest();
        }
    }

    [Fact]
    public void FormatMessage_WhenMessageExceedsInitialBuffer_GrowsRetriesThenReuses()
    {
        using ProviderMetadata? metadata = ProviderMetadata.Create(Constants.SecurityAuditingLogName);
        Assert.SkipUnless(metadata is not null, "Requires the Microsoft-Windows-Security-Auditing provider on the host.");

        uint messageId = FirstResolvableMessageId(metadata!);
        Assert.SkipUnless(messageId != NoMessage, "No resolvable message id on this provider.");

        string expected = metadata!.FormatMessageById(messageId);

        int savedInitial = NativeMethods.InitialRenderChars;

        try
        {
            NativeMethods.InitialRenderChars = 1;
            NativeMethods.ResetRenderScratchForTest();

            string grown = metadata.FormatMessageById(messageId);

            Assert.Equal(2, NativeMethods.FormatPInvokeCountForTest);
            Assert.Equal(expected, grown);

            NativeMethods.FormatPInvokeCountForTest = 0;
            _ = metadata.FormatMessageById(messageId);

            Assert.Equal(1, NativeMethods.FormatPInvokeCountForTest);
        }
        finally
        {
            NativeMethods.InitialRenderChars = savedInitial;
            NativeMethods.ResetRenderScratchForTest();
        }
    }

    [Fact]
    public void FormatMessage_WhenMessageHasInserts_ReturnsBestEffortTextWithoutThrowing()
    {
        using ProviderMetadata? metadata = ProviderMetadata.Create(Constants.SecurityAuditingLogName);
        Assert.SkipUnless(metadata is not null, "Requires the Microsoft-Windows-Security-Auditing provider on the host.");

        // Event messages formatted by id (with no insert values) exercise the unresolved-insert tolerance:
        // EvtFormatMessage returns ERROR_EVT_UNRESOLVED_*_INSERT with best-effort text, which the wrapper must copy out
        // rather than throw (callers depend on that best-effort text via ToRawContent).
        uint messageId = metadata!.ToRawContent(Constants.SecurityAuditingLogName, null)
            .Events.Select(providerEvent => providerEvent.MessageId)
            .FirstOrDefault(id => id != NoMessage && id != 0);

        Assert.SkipUnless(messageId != 0, "Provider has no event messages on this host.");

        string text = metadata.FormatMessageById(messageId);   // must not throw on unresolved inserts

        Assert.False(string.IsNullOrEmpty(text));
    }

    [Fact]
    public void GetObjectArrayProperty_WhenWarmed_IssuesSingleProbelessPInvoke()
    {
        (EvtHandle metadata, EvtHandle array) = OpenKeywordArray(Constants.KernelPowerLogName);

        using (metadata)
        {
            using (array)
            {
                Assert.SkipUnless(!array.IsInvalid && NativeMethods.GetObjectArraySize(array) > 0, "Provider has no keyword items on this host.");

                _ = NativeMethods.GetObjectArrayProperty(array, 0, EvtPublisherMetadataPropertyId.KeywordName);   // warm
                NativeMethods.ObjectArrayPInvokeCountForTest = 0;
                _ = NativeMethods.GetObjectArrayProperty(array, 0, EvtPublisherMetadataPropertyId.KeywordName);

                Assert.Equal(1, NativeMethods.ObjectArrayPInvokeCountForTest);
            }
        }
    }

    [Fact]
    public void GetObjectArrayProperty_WhenItemExceedsInitialBuffer_GrowsRetriesThenReuses()
    {
        (EvtHandle metadata, EvtHandle array) = OpenKeywordArray(Constants.KernelPowerLogName);

        using (metadata)
        {
            using (array)
            {
                Assert.SkipUnless(!array.IsInvalid && NativeMethods.GetObjectArraySize(array) > 0, "Provider has no keyword items on this host.");

                var expected = (string)NativeMethods.GetObjectArrayProperty(array, 0, EvtPublisherMetadataPropertyId.KeywordName);

                int savedInitial = NativeMethods.InitialRenderChars;

                try
                {
                    NativeMethods.InitialRenderChars = 1;
                    NativeMethods.ResetRenderScratchForTest();

                    var grown = (string)NativeMethods.GetObjectArrayProperty(array, 0, EvtPublisherMetadataPropertyId.KeywordName);

                    Assert.Equal(2, NativeMethods.ObjectArrayPInvokeCountForTest);
                    Assert.Equal(expected, grown);

                    NativeMethods.ObjectArrayPInvokeCountForTest = 0;
                    _ = NativeMethods.GetObjectArrayProperty(array, 0, EvtPublisherMetadataPropertyId.KeywordName);

                    Assert.Equal(1, NativeMethods.ObjectArrayPInvokeCountForTest);
                }
                finally
                {
                    NativeMethods.InitialRenderChars = savedInitial;
                    NativeMethods.ResetRenderScratchForTest();
                }
            }
        }
    }

    [Fact]
    public void RenderEvent_WhenRenderedConcurrentlyOnManyThreads_EachThreadGetsIntactRecords()
    {
        const int threads = 8;
        List<EvtHandle> handles = FirstEventHandles(Constants.ApplicationLogName, threads);

        try
        {
            Assert.SkipUnless(handles.Count == threads, "Not enough readable events for the concurrency test.");

            // Each thread renders its OWN distinct handle repeatedly on its OWN [ThreadStatic] buffer (the watcher's
            // overlapping-callback shape). No torn reads if the per-thread buffers stay isolated.
            var expected = handles.Select(handle => NativeMethods.RenderEvent(handle).Id).ToList();

            Parallel.For(0, threads, thread =>
            {
                for (int rep = 0; rep < 50; rep++)
                {
                    Assert.Equal(expected[thread], NativeMethods.RenderEvent(handles[thread]).Id);
                }
            });
        }
        finally
        {
            foreach (EvtHandle handle in handles) { handle.Dispose(); }
        }
    }

    private static (EvtHandle Metadata, EvtHandle Array) OpenKeywordArray(string providerName)
    {
        EvtHandle metadata = NativeMethods.EvtOpenPublisherMetadata(EventLogSession.GlobalSession.Handle, providerName, null, 0, 0);
        Assert.False(metadata.IsInvalid, $"EvtOpenPublisherMetadata failed for '{providerName}'.");

        NativeMethods.EvtGetPublisherMetadataProperty(metadata, EvtPublisherMetadataPropertyId.Keywords, 0, 0, IntPtr.Zero, out int bufferUsed);

        // The size-probe reports the required byte count via ERROR_INSUFFICIENT_BUFFER; if it fails for any other reason
        // (e.g. the provider exposes no Keywords), bufferUsed is 0/garbage - return an invalid array so the caller skips
        // rather than allocating a bad buffer.
        if (bufferUsed <= 0) { return (metadata, EvtHandle.Zero); }

        IntPtr buffer = Marshal.AllocHGlobal(bufferUsed);

        try
        {
            Assert.True(
                NativeMethods.EvtGetPublisherMetadataProperty(metadata, EvtPublisherMetadataPropertyId.Keywords, 0, bufferUsed, buffer, out bufferUsed),
                $"EvtGetPublisherMetadataProperty(Keywords) failed for '{providerName}'.");

            var variant = Marshal.PtrToStructure<EvtVariant>(buffer);

            return (metadata, variant.EvtHandleVal == IntPtr.Zero ? EvtHandle.Zero : new EvtHandle(variant.EvtHandleVal));
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
    }

    private static List<EvtHandle> FirstEventHandles(string channelName, int count)
    {
        var handles = new List<EvtHandle>(count);
        EvtHandle query = NativeMethods.EvtQuery(EventLogSession.GlobalSession.Handle, channelName, null, LogPathType.Channel);
        Assert.False(query.IsInvalid, $"EvtQuery failed for channel '{channelName}'.");

        try
        {
            var batch = new IntPtr[count];
            int returned = 0;

            if (NativeMethods.EvtNext(query, batch.Length, batch, 0, 0, ref returned))
            {
                for (int i = 0; i < returned; i++) { handles.Add(new EvtHandle(batch[i])); }
            }
        }
        finally
        {
            query.Dispose();
        }

        return handles;
    }
#endif
}
