// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.WindowsPlatform.Dialogs;
using Xunit;

namespace EventLogExpert.Windows.Tests;

public sealed class Win32FileDialogFilterTests
{
    [Fact]
    public void BuildExtensionPattern_JoinsExtensionsAsWildcardTokens()
    {
        Assert.Equal("*.evtx;*.etl", Win32FileDialog.BuildExtensionPattern([".evtx", ".etl"]));
        Assert.Equal("*.evtx", Win32FileDialog.BuildExtensionPattern([".evtx"]));
    }

    [Fact]
    public void BuildFilter_MeasurePassSizeMatchesWritePassAndLaysOutTheNullSeparatedBuffer()
    {
        var chrome = new FilterChrome("Supported types (*.evtx)", "All files", "*.evtx");

        var measured = Win32FileDialog.BuildFilter(default, chrome);
        var buffer = new char[measured];
        var written = Win32FileDialog.BuildFilter(buffer, chrome);

        Assert.Equal(measured, written);

        // OPENFILENAME filter format: label\0pattern\0allFiles\0*.*\0\0
        var segments = new string(buffer).Split('\0');
        Assert.Equal("Supported types (*.evtx)", segments[0]);
        Assert.Equal("*.evtx", segments[1]);
        Assert.Equal("All files", segments[2]);
        Assert.Equal("*.*", segments[3]);
        Assert.Equal(string.Empty, segments[4]); // trailing double-null terminator
    }

    [Fact]
    public void BuildFilter_UsesTheLocalizedChromeLabelsVerbatim()
    {
        var chrome = new FilterChrome("Types pris en charge (*.evtx)", "Tous les fichiers", "*.evtx");

        var buffer = new char[Win32FileDialog.BuildFilter(default, chrome)];
        Win32FileDialog.BuildFilter(buffer, chrome);

        var segments = new string(buffer).Split('\0');
        Assert.Equal("Types pris en charge (*.evtx)", segments[0]);
        Assert.Equal("Tous les fichiers", segments[2]);
    }

    [Fact]
    public void CreateFilterChrome_BlankAllFiles_FallsBackToTheEnglishLabel()
    {
        var chrome = Win32FileDialog.CreateFilterChrome("Supported types ({0})", "   ", [".evtx"]);

        Assert.Equal("All files", chrome.AllFilesLabel);
    }

    [Fact]
    public void CreateFilterChrome_BlankFormat_FallsBackToTheEnglishLabel()
    {
        var chrome = Win32FileDialog.CreateFilterChrome("   ", "All files", [".evtx"]);

        Assert.Equal("Supported types (*.evtx)", chrome.SupportedTypesLabel);
    }

    [Fact]
    public void CreateFilterChrome_ClampsBothLabelsToMaxFilterLabelChars()
    {
        var longFormat = "{0}" + new string('x', 500);
        var longAllFiles = new string('y', 500);

        var chrome = Win32FileDialog.CreateFilterChrome(longFormat, longAllFiles, [".evtx"]);

        Assert.Equal(Win32FileDialog.MaxFilterLabelChars, chrome.SupportedTypesLabel.Length);
        Assert.Equal(Win32FileDialog.MaxFilterLabelChars, chrome.AllFilesLabel.Length);
    }

    [Fact]
    public void CreateFilterChrome_FormatsTheSupportedTypesLabelWithThePattern()
    {
        var chrome = Win32FileDialog.CreateFilterChrome("Supported types ({0})", "All files", [".evtx", ".etl"]);

        Assert.Equal("Supported types (*.evtx;*.etl)", chrome.SupportedTypesLabel);
        Assert.Equal("All files", chrome.AllFilesLabel);
        Assert.Equal("*.evtx;*.etl", chrome.Pattern);
    }

    [Fact]
    public void CreateFilterChrome_MalformedFormat_FallsBackToTheEnglishLabel()
    {
        // A translator-authored format with a bad placeholder index must never break the picker.
        var chrome = Win32FileDialog.CreateFilterChrome("Localized {1}", "All files", [".evtx"]);

        Assert.Equal("Supported types (*.evtx)", chrome.SupportedTypesLabel);
    }

    [Fact]
    public void CreateFilterChrome_UnbalancedBraceFormat_FallsBackToTheEnglishLabel()
    {
        var chrome = Win32FileDialog.CreateFilterChrome("Supported {0", "All files", [".evtx"]);

        Assert.Equal("Supported types (*.evtx)", chrome.SupportedTypesLabel);
    }
}
