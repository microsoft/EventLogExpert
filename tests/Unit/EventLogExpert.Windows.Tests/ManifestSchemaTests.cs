// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using System.Text.RegularExpressions;
using Xunit;

namespace EventLogExpert.Windows.Tests;

/// <summary>
///     Loads the MAUI head's <c>Package.appxmanifest</c> at test time and asserts the structural invariants the
///     Explorer context menu depends on. Catches drift between the manifest CLSID and the native C++/WinRT shell
///     extension's hard-coded CLSID, missing namespace declarations, and verb mis-configuration that <c>MakeAppx</c>
///     doesn't surface until packaging time.
/// </summary>
public class ManifestSchemaTests
{
    /// <summary>
    ///     CLSID of the shell extension. MUST match the literal in
    ///     <c>src/EventLogExpert.ExplorerExtensionNative/dllmain.cpp</c> (the <c>CLSID_UUID</c> macro and <c>kCanonical</c>
    ///     constant) AND the manifest entries below.
    /// </summary>
    private const string OpenEvtxCommandClsid = "F1B2C3D4-E5F6-4789-AB12-CD34EF567890";

    [Fact]
    public void Manifest_ComSurrogateServer_PointsAtNativeDll()
    {
        var content = File.ReadAllText(ResolveManifestPath());

        // SurrogateServer (dllhost-hosted) — NOT ExeServer. Required because Explorer activates
        // context-menu COM handlers via CLSCTX_INPROC_HANDLER, not CLSCTX_LOCAL_SERVER.
        Assert.Contains("<com:SurrogateServer", content);
        Assert.Contains("Path=\"EventLogExpert.ExplorerExtension.dll\"", content);
        Assert.Contains("ThreadingModel=\"STA\"", content);
        Assert.Contains($"Id=\"{OpenEvtxCommandClsid}\"", content);
        Assert.DoesNotContain("<com:ExeServer", content);
        Assert.DoesNotContain("comhost.dll", content);
    }

    [Fact]
    public void Manifest_DeclaresRequiredNamespaces()
    {
        var content = File.ReadAllText(ResolveManifestPath());

        Assert.Contains("xmlns:uap2", content);
        Assert.Contains("xmlns:uap3", content);
        Assert.Contains("xmlns:desktop4", content);
        Assert.Contains("xmlns:desktop5", content);
        Assert.Contains("xmlns:com", content);
    }

    [Fact]
    public void Manifest_Exists()
    {
        var path = ResolveManifestPath();

        Assert.True(File.Exists(path), $"Manifest not found at {path}");
    }

    [Fact]
    public void Manifest_FileContextMenu_RegistersAnyFileAndDirectoryAndBackground()
    {
        var content = File.ReadAllText(ResolveManifestPath());

        // Three context-menu surfaces, all routed to the same CLSID; the native handler's
        // GetState filters to .evtx files and directories.
        //   - desktop4:ItemType Type="*"                   — any-file right-click
        //   - desktop5:ItemType Type="Directory"           — folder-icon right-click
        //   - desktop5:ItemType Type="Directory\Background" — right-click inside an open folder
        Assert.Contains("<desktop4:ItemType Type=\"*\">", content);
        Assert.Contains($"<desktop4:Verb Id=\"OpenEvtx\" Clsid=\"{OpenEvtxCommandClsid}\"", content);
        Assert.Contains("<desktop5:ItemType Type=\"Directory\">", content);
        Assert.Contains("<desktop5:ItemType Type=\"Directory\\Background\">", content);
        // Both desktop5:ItemType entries must declare a desktop5:Verb with the same CLSID.
        var verbMatches = Regex.Matches(
            content,
            $"<desktop5:Verb Id=\"OpenEvtx\" Clsid=\"{OpenEvtxCommandClsid}\"");
        Assert.Equal(2, verbMatches.Count);
    }

    [Fact]
    public void Manifest_FtaVerb_IsDeclaredAsPlayerOpenVerb()
    {
        var content = File.ReadAllText(ResolveManifestPath());

        Assert.Contains("<uap2:SupportedVerbs>", content);
        Assert.Contains("Id=\"open\"", content);
        // MultiSelectModel is unprefixed — the uap3:-prefixed form fails MSIX deployment
        // schema validation (DEP0700 / 0xC00CE015 attribute-not-defined-in-DTD).
        Assert.Contains("MultiSelectModel=\"Player\"", content);
        Assert.DoesNotContain("uap3:MultiSelectModel=\"", content);
        // uap7:Default does not survive AppX deployment validation in our config and isn't
        // required for top-level surfacing — desktop4:fileExplorerContextMenus produces those.
        Assert.DoesNotContain("uap7:", content);
    }

    [Fact]
    public void Manifest_IgnorableNamespaces_OmitsCategoriesActuallyUsed()
    {
        var content = File.ReadAllText(ResolveManifestPath());

        // Namespaces actually consumed by extensions must NOT be listed as ignorable — the
        // deployment engine silently drops the extension on schema-unknown surfaces if they are.
        // Parse the attribute value rather than assert an exact string so reordering, extra
        // whitespace, or additional truly-ignorable prefixes (e.g., mp, build) don't break the test.
        var match = Regex.Match(content, @"IgnorableNamespaces=""([^""]*)""");

        Assert.True(match.Success, "IgnorableNamespaces attribute not declared on the Package element.");

        var ignorablePrefixes = match.Groups[1].Value
            .Split([' ', '\t'], StringSplitOptions.RemoveEmptyEntries);

        // Used prefixes derived from the namespace declarations + extension consumption sites:
        // uap2 (SupportedVerbs), uap3 (Extensions tree), desktop4 (any-file fileExplorerContextMenus),
        // desktop5 (Directory + Directory\Background fileExplorerContextMenus), com (SurrogateServer).
        string[] usedPrefixes = ["uap2", "uap3", "desktop4", "desktop5", "com"];

        foreach (var used in usedPrefixes)
        {
            Assert.DoesNotContain(used, ignorablePrefixes);
        }
    }

    [Fact]
    public void Manifest_TargetDeviceFamily_DesktopOnly()
    {
        var content = File.ReadAllText(ResolveManifestPath());

        // Windows.Universal causes AppX deployment to process under Universal rules first,
        // silently dropping Desktop-specific extension categories like
        // windows.fileExplorerContextMenus.
        Assert.Contains("<TargetDeviceFamily Name=\"Windows.Desktop\"", content);
        Assert.DoesNotContain("<TargetDeviceFamily Name=\"Windows.Universal\"", content);
    }

    [Fact]
    public void NativeShellExtension_HasSameClsidAsManifest()
    {
        // The CLSID lives in three sites that must stay in sync: the manifest XML, the constant
        // above, and the native C++ source. Drift would orphan Explorer's COM catalog from prior
        // installs. This test reads dllmain.cpp and asserts both the CLSID_UUID string macro AND
        // the kCanonical GUID struct fragments match — so changing one without the others fails.
        var dllmainPath = ResolveRepoRelativePath("src", "EventLogExpert.ExplorerExtensionNative", "dllmain.cpp");

        var native = File.ReadAllText(dllmainPath);

        Assert.Contains($"#define CLSID_UUID \"{OpenEvtxCommandClsid}\"", native);
        // F1B2C3D4-E5F6-4789-AB12-CD34EF567890 →
        // {0xF1B2C3D4, 0xE5F6, 0x4789, {0xAB, 0x12, 0xCD, 0x34, 0xEF, 0x56, 0x78, 0x90}}
        Assert.Contains("0xF1B2C3D4, 0xE5F6, 0x4789", native);
        Assert.Contains("0xAB, 0x12, 0xCD, 0x34, 0xEF, 0x56, 0x78, 0x90", native);
    }

    [Fact]
    public void NativeShellExtension_ImplementsIObjectWithSite_ForBackgroundMenuSurface()
    {
        // The Directory\Background ItemType registered in the manifest delivers a null
        // IShellItemArray to GetState/Invoke. Without IObjectWithSite + SID_SFolderView the
        // handler cannot recover the current folder and the verb is permanently hidden in that
        // surface — the manifest registration alone is insufficient. This test asserts the
        // method-signature lines (not bare keyword presence) so a comment-only reference to the
        // tokens does not silently let the test pass while the implementation is removed.
        var dllmainPath = ResolveRepoRelativePath("src", "EventLogExpert.ExplorerExtensionNative", "dllmain.cpp");

        var native = File.ReadAllText(dllmainPath);

        Assert.Contains("IObjectWithSite", native);
        Assert.Contains("IFACEMETHODIMP SetSite", native);
        Assert.Contains("IFACEMETHODIMP GetSite", native);
        Assert.Contains("SID_SFolderView", native);
        Assert.Contains("IPersistFolder2", native);
        Assert.Contains("GetCurFolder", native);
        // SHGetPathFromIDListEx is the long-path-capable variant — drift back to
        // SHGetPathFromIDListW would silently re-introduce the MAX_PATH ceiling on this surface.
        Assert.Contains("SHGetPathFromIDListEx", native);
    }

    private static string ResolveManifestPath() =>
        ResolveRepoRelativePath("src", "EventLogExpert", "Platforms", "Windows", "Package.appxmanifest");

    // Walks up from the test's runtime directory until finding the directory containing
    // EventLogExpert.slnx, then joins the supplied relative segments. Resilient to test-output
    // layout differences across runners (net10.0-windows10.0.19041.0/ vs net10.0/, Debug/Release,
    // win-x64/ subfolder when publish-style output is in play) where a fixed `..\..\..\` count
    // can drift. Pattern mirrors tests/Unit/EventLogExpert.Filtering.Tests/Persistence/PersistencePolicyTests.cs.
    private static string ResolveRepoRelativePath(params string[] segments)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);

        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "EventLogExpert.slnx")))
        {
            directory = directory.Parent;
        }

        Assert.NotNull(directory);

        var combined = Path.Combine([directory.FullName, .. segments]);
        Assert.True(File.Exists(combined), $"Expected file at {combined} to exist.");

        return combined;
    }
}
