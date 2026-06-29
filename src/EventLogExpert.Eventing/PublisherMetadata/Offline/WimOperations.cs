// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Eventing.Interop;
using EventLogExpert.Logging.Abstractions;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Security.Principal;
using System.Xml;
using System.Xml.Linq;

namespace EventLogExpert.Eventing.PublisherMetadata.Offline;

/// <summary>The production <see cref="IWimOperations" />, calling the real <c>wimgapi.dll</c> exports.</summary>
internal sealed class WimOperations : IWimOperations
{
    internal static WimOperations Instance { get; } = new();

    public int ApplyImage(
        string wimPath,
        int imageIndex,
        string destinationDirectory,
        string scratchDirectory,
        CancellationToken cancellationToken,
        ITraceLogger? logger)
    {
        using WimFileSafeHandle wim = NativeMethods.WIMCreateFile(
            wimPath, NativeMethods.WIM_GENERIC_READ, NativeMethods.WIM_OPEN_EXISTING, 0, NativeMethods.WIM_COMPRESS_NONE, out _);

        if (wim.IsInvalid || !NativeMethods.WIMSetTemporaryPath(wim, scratchDirectory)) { return Marshal.GetLastWin32Error(); }

        using WimImageSafeHandle image = NativeMethods.WIMLoadImage(wim, (uint)imageIndex);

        if (image.IsInvalid) { return Marshal.GetLastWin32Error(); }

        // Cancellation is opt-in: the CLI passes a non-cancellable token, so no callback is marshalled at all. When a
        // cancellable token IS supplied, the delegate MUST stay rooted for the whole apply (it fires per-message for
        // minutes); a collected/moved delegate would be an intermittent AccessViolationException.
        NativeMethods.WimMessageCallback? abortCallback = null;
        IntPtr callbackPointer = IntPtr.Zero;
        uint registeredCallback = NativeMethods.WIM_INVALID_CALLBACK_VALUE;

        if (cancellationToken.CanBeCanceled)
        {
            abortCallback = (_, _, _, _) =>
                cancellationToken.IsCancellationRequested ? NativeMethods.WIM_MSG_ABORT_IMAGE : NativeMethods.WIM_MSG_SUCCESS;
            callbackPointer = Marshal.GetFunctionPointerForDelegate(abortCallback);
            registeredCallback = NativeMethods.WIMRegisterMessageCallback(wim, callbackPointer, IntPtr.Zero);

            if (registeredCallback == NativeMethods.WIM_INVALID_CALLBACK_VALUE)
            {
                logger?.Debug($"{nameof(WimOperations)}: WIMRegisterMessageCallback failed (error {Marshal.GetLastWin32Error()}); apply will not be cancellable.");
            }
        }

        try
        {
            bool applied = NativeMethods.WIMApplyImage(
                image, destinationDirectory, NativeMethods.WIM_FLAG_NO_FILEACL | NativeMethods.WIM_FLAG_NO_DIRACL);

            return applied ? 0 : Marshal.GetLastWin32Error();
        }
        finally
        {
            if (registeredCallback != NativeMethods.WIM_INVALID_CALLBACK_VALUE)
            {
                NativeMethods.WIMUnregisterMessageCallback(wim, callbackPointer);
            }

            // Root the delegate across the entire apply + unregister; only now may it be collected.
            GC.KeepAlive(abortCallback);
        }
    }

    public bool IsProcessElevated()
    {
        using var identity = WindowsIdentity.GetCurrent();

        return new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator);
    }

    public WimImageList ReadImageList(string wimPath, ITraceLogger? logger)
    {
        try
        {
            using WimFileSafeHandle wim = NativeMethods.WIMCreateFile(
                wimPath, NativeMethods.WIM_GENERIC_READ, NativeMethods.WIM_OPEN_EXISTING, 0, NativeMethods.WIM_COMPRESS_NONE, out _);

            if (wim.IsInvalid)
            {
                logger?.Debug($"{nameof(WimOperations)}: WIMCreateFile failed for {wimPath} (error {Marshal.GetLastWin32Error()}).");

                return WimImageList.NotAWim;
            }

            if (!NativeMethods.WIMGetImageInformation(wim, out IntPtr imageInfo, out uint imageInfoBytes) || imageInfo == IntPtr.Zero)
            {
                logger?.Debug($"{nameof(WimOperations)}: WIMGetImageInformation failed for {wimPath} (error {Marshal.GetLastWin32Error()}).");

                return WimImageList.NotAWim;
            }

            try
            {
                string xml = Marshal.PtrToStringUni(imageInfo, (int)(imageInfoBytes / sizeof(char))) ?? string.Empty;

                return new WimImageList(WimImageListStatus.Ok, ParseImageEntries(xml));
            }
            finally
            {
                NativeMethods.LocalFree(imageInfo);
            }
        }
        catch (Exception ex) when (ex is not OutOfMemoryException and not StackOverflowException)
        {
            logger?.Debug($"{nameof(WimOperations)}: reading WIM image list for {wimPath} failed: {ex.Message}");

            return WimImageList.NotAWim;
        }
    }

    private static IReadOnlyList<WimImageEntry> ParseImageEntries(string xml)
    {
        // The buffer is UTF-16 and may carry a BOM; XDocument rejects a leading BOM character.
        string trimmed = xml.TrimStart('\uFEFF', '\u200B').Trim();

        if (trimmed.Length == 0) { return []; }

        XDocument document;

        try
        {
            document = XDocument.Parse(trimmed);
        }
        catch (XmlException)
        {
            return [];
        }

        var entries = new List<WimImageEntry>();

        foreach (XElement image in document.Descendants("IMAGE"))
        {
            if (!int.TryParse((string?)image.Attribute("INDEX"), NumberStyles.Integer, CultureInfo.InvariantCulture, out int index))
            {
                continue;
            }

            string name = (string?)image.Element("NAME") ?? (string?)image.Element("DISPLAYNAME") ?? string.Empty;
            string edition = (string?)image.Element("FLAGS")
                ?? (string?)image.Element("WINDOWS")?.Element("EDITIONID")
                ?? string.Empty;
            long? totalBytes = long.TryParse(
                (string?)image.Element("TOTALBYTES"), NumberStyles.Integer, CultureInfo.InvariantCulture, out long bytes)
                ? bytes
                : null;

            entries.Add(new WimImageEntry(index, name.Trim(), edition.Trim(), totalBytes));
        }

        entries.Sort(static (left, right) => left.Index.CompareTo(right.Index));

        return entries;
    }
}
