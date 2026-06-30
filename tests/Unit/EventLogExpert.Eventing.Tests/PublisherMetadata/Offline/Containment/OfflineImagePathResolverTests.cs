// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Eventing.PublisherMetadata.Offline.Containment;

namespace EventLogExpert.Eventing.Tests.PublisherMetadata.Offline.Containment;

public sealed class OfflineImagePathResolverTests
{
    [Fact]
    public void Resolve_ReparsePointEscapingTheImage_DropsInsteadOfThrowing()
    {
        using OfflineTestImage image = OfflineTestImage.Create();
        string outsideTarget = Path.Combine(Path.GetTempPath(), "elx_outside_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(outsideTarget);
        string junctionPath = Path.Combine(image.ImageRoot.System32Directory, "linkdir");

        Assert.SkipUnless(OfflineTestImage.TryCreateJunction(junctionPath, outsideTarget), "Could not create an NTFS junction for the reparse-point test.");

        try
        {
            var resolver = new OfflineImagePathResolver(
                new OfflineImagePathMapper(image.ImageRoot, logger: null),
                new OfflineRootGuard(image.ImageRoot, logger: null));

            // Escaping junctions must be dropped so a hostile image cannot abort offline enumeration.
            Assert.Null(resolver.Resolve(@"C:\Windows\System32\linkdir\evil.dll", "resource"));
        }
        finally
        {
            if (Directory.Exists(outsideTarget)) { Directory.Delete(outsideTarget, recursive: true); }
        }
    }
}
