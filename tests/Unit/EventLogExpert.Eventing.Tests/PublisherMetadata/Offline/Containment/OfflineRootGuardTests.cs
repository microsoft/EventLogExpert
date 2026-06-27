// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Eventing.PublisherMetadata.Offline.Containment;
using System.ComponentModel;
using System.Diagnostics;

namespace EventLogExpert.Eventing.Tests.PublisherMetadata.Offline.Containment;

public sealed class OfflineRootGuardTests
{
    [Fact]
    public void Assert_HostPathOutsideImage_ThrowsEvenOnSameDrive()
    {
        // The scaffold lives on the host drive, so the host's own C:\Windows is NOT under the image root - the guard must
        // reject it, which a Path.GetPathRoot-based guard (treating C:\ as the root) would wrongly allow.
        using OfflineTestImage image = OfflineTestImage.Create();
        var guard = new OfflineRootGuard(image.ImageRoot, logger: null);

        string hostPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "ntdll.dll");

        Assert.Throws<OfflineRootGuardViolationException>(() => guard.Assert(hostPath, "resource"));
    }

    [Fact]
    public void Assert_PathTraversingAJunctionThatEscapesTheImage_Throws()
    {
        using OfflineTestImage image = OfflineTestImage.Create();
        string outsideTarget = Path.Combine(Path.GetTempPath(), "elx_outside_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(outsideTarget);

        try
        {
            string junctionPath = Path.Combine(image.ImageRoot.System32Directory, "linkdir");

            Assert.SkipUnless(TryCreateJunction(junctionPath, outsideTarget), "Could not create an NTFS junction for the reparse-point test.");

            var guard = new OfflineRootGuard(image.ImageRoot, logger: null);
            string throughJunction = Path.Combine(junctionPath, "evil.dll");

            // Lexically the path is under the image root, but the junction redirects outside it - the guard must resolve
            // the reparse point and fail closed.
            Assert.Throws<OfflineRootGuardViolationException>(() => guard.Assert(throughJunction, "resource"));
        }
        finally
        {
            if (Directory.Exists(outsideTarget)) { Directory.Delete(outsideTarget, recursive: true); }
        }
    }

    [Fact]
    public void Assert_PathUnderImageRoot_DoesNotThrow()
    {
        using OfflineTestImage image = OfflineTestImage.Create();
        var guard = new OfflineRootGuard(image.ImageRoot, logger: null);

        string inside = Path.Combine(image.ImageRoot.System32Directory, "foo.dll");

        guard.Assert(inside, "resource");
    }

    private static bool TryCreateJunction(string junctionPath, string targetPath)
    {
        try
        {
            // Directory junctions (mklink /J) do not require elevation, unlike symbolic links.
            using var process = Process.Start(new ProcessStartInfo("cmd.exe", $"/c mklink /J \"{junctionPath}\" \"{targetPath}\"")
            {
                CreateNoWindow = true,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            });

            if (process is null) { return false; }

            process.WaitForExit(10_000);

            return process.HasExited && process.ExitCode == 0 && Directory.Exists(junctionPath);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or Win32Exception)
        {
            return false;
        }
    }
}
