// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Eventing.OfflineImaging.Containment;

namespace EventLogExpert.Eventing.OfflineImaging.Tests.Containment;

public sealed class OfflineRootGuardTests
{
    [Fact]
    public void Assert_HostPathOutsideImage_ThrowsEvenOnSameDrive()
    {
        // Same-drive host paths catch Path.GetPathRoot-based containment bugs.
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

            Assert.SkipUnless(OfflineTestImage.TryCreateJunction(junctionPath, outsideTarget), "Could not create an NTFS junction for the reparse-point test.");

            var guard = new OfflineRootGuard(image.ImageRoot, logger: null);
            string throughJunction = Path.Combine(junctionPath, "evil.dll");

            // Escaping junctions must resolve outside the image and fail closed.
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
}
