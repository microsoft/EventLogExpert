// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

// Theme application. Called from MainLayout on first render and whenever
// the user changes the Theme setting. Accepts "system", "light", "dark",
// "modernlight", or "moderndark" (case-insensitive). For "system", the
// data-theme attribute is removed so that the CSS prefers-color-scheme media
// query takes over.
export function setTheme(theme) {
    const value = (theme || "system").toString().toLowerCase();
    if (value === "light" || value === "dark" || value === "modernlight" || value === "moderndark") {
        document.documentElement.setAttribute("data-theme", value);
    } else {
        document.documentElement.removeAttribute("data-theme");
    }
}
