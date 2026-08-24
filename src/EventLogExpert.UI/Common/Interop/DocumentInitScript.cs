// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

namespace EventLogExpert.UI.Common.Interop;

/// <summary>
///     Builds the self-contained CoreWebView2 document-created script (runs before HTML parse, so it can't call
///     app.js) that sets the root element's theme/dir/lang from trusted, closed-set values embedded unescaped.
///     Defers to DOMContentLoaded when documentElement is not yet available, since document-created scripts can
///     precede the root.
/// </summary>
public static class DocumentInitScript
{
    public static string Build(string? themeAttribute, string direction, string language)
    {
        ArgumentNullException.ThrowIfNull(direction);
        ArgumentNullException.ThrowIfNull(language);

        var themeStatement = themeAttribute is null ?
            "root.removeAttribute('data-theme');" :
            $"root.setAttribute('data-theme','{themeAttribute}');";

        return
            "(function(){function apply(){var root=document.documentElement;if(!root){return false;}" +
            themeStatement +
            $"root.setAttribute('dir','{direction}');root.setAttribute('lang','{language}');return true;" +
            "}if(!apply()){document.addEventListener('DOMContentLoaded',apply);}})();";
    }
}
