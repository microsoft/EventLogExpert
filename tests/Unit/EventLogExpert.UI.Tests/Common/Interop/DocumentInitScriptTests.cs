// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Runtime.Settings;
using EventLogExpert.UI.Common.Interop;

namespace EventLogExpert.UI.Tests.Common.Interop;

public sealed class DocumentInitScriptTests
{
    [Fact]
    public void Build_NullTheme_RemovesThemeAttribute()
    {
        var script = DocumentInitScript.Build(null, "ltr", "en");

        Assert.Equal(
            "(function(){function apply(){var root=document.documentElement;if(!root){return false;}" +
            "root.removeAttribute('data-theme');root.setAttribute('dir','ltr');root.setAttribute('lang','en');" +
            "return true;}if(!apply()){document.addEventListener('DOMContentLoaded',apply);}})();",
            script);
    }

    [Theory]
    [InlineData("modernlight")]
    [InlineData("moderndark")]
    public void Build_WithModernTheme_EmbedsThemeAttribute(string theme)
    {
        var script = DocumentInitScript.Build(theme, "ltr", "en");

        Assert.Contains($"root.setAttribute('data-theme','{theme}');", script);
    }

    [Fact]
    public void Build_WithTheme_SetsThemeDirAndLang()
    {
        var script = DocumentInitScript.Build("dark", "rtl", "ar");

        Assert.Equal(
            "(function(){function apply(){var root=document.documentElement;if(!root){return false;}" +
            "root.setAttribute('data-theme','dark');root.setAttribute('dir','rtl');root.setAttribute('lang','ar');" +
            "return true;}if(!apply()){document.addEventListener('DOMContentLoaded',apply);}})();",
            script);
    }

    [Fact]
    public void EveryExplicitTheme_MapsToASupportedDataThemeAttribute()
    {
        // The data-theme attribute the app writes (MainPage pre-render + MainLayout.razor.js setTheme)
        // is the lowercased enum name for every non-System theme. Keep this closed set in lockstep with
        // the CSS palette blocks in app.css and the JS allowlist in MainLayout.razor.js.
        string[] supported = ["light", "dark", "modernlight", "moderndark"];

        foreach (var theme in Enum.GetValues<Theme>())
        {
            if (theme == Theme.System) { continue; }

            Assert.Contains(theme.ToString().ToLowerInvariant(), supported);
        }
    }
}
