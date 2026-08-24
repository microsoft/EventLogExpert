// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

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
}
