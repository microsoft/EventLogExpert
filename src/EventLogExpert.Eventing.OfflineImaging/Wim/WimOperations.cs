// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Eventing.OfflineImaging.Interop;
using EventLogExpert.Logging.Abstractions;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Security.Principal;
using System.Xml;
using System.Xml.Linq;

namespace EventLogExpert.Eventing.OfflineImaging.Wim;

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

        // Keep the WIM callback delegate rooted until after unregister; native callbacks may fire for minutes.
        NativeMethods.WimMessageCallback? messageCallback = null;
        IntPtr callbackPointer = IntPtr.Zero;
        uint registeredCallback = NativeMethods.WIM_INVALID_CALLBACK_VALUE;

        if (cancellationToken.CanBeCanceled || logger is not null)
        {
            int lastReportedDecile = 0;

            messageCallback = (messageId, wParam, _, _) =>
            {
                if (cancellationToken.IsCancellationRequested) { return NativeMethods.WIM_MSG_ABORT_IMAGE; }

                if (messageId != NativeMethods.WIM_MSG_PROGRESS || logger is null)
                {
                    return NativeMethods.WIM_MSG_SUCCESS;
                }

                int percent = (int)wParam; // WIM_MSG_PROGRESS: wParam is percent complete (0-100).

                if (TryAdvanceProgressDecile(percent, ref lastReportedDecile))
                {
                    logger.Information($"Extracting image: {percent}% complete...");
                }

                return NativeMethods.WIM_MSG_SUCCESS;
            };

            callbackPointer = Marshal.GetFunctionPointerForDelegate(messageCallback);
            registeredCallback = NativeMethods.WIMRegisterMessageCallback(wim, callbackPointer, IntPtr.Zero);

            if (registeredCallback == NativeMethods.WIM_INVALID_CALLBACK_VALUE)
            {
                logger?.Debug($"{nameof(WimOperations)}: WIMRegisterMessageCallback failed (error {Marshal.GetLastWin32Error()}); apply will not be cancellable and will not report progress.");
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

            // Native callback lifetime ends only after unregister returns.
            GC.KeepAlive(messageCallback);
        }
    }

    internal static bool TryAdvanceProgressDecile(int percent, ref int lastReportedDecile)
    {
        if (percent is < 0 or > 100) { return false; }

        int decile = percent / 10;

        if (decile <= lastReportedDecile) { return false; }

        lastReportedDecile = decile;

        return true;
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
        // WIM XML may include a BOM; XDocument rejects it as a leading character.
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
