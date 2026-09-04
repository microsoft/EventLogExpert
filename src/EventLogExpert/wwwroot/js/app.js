// App-global keydown handler. Loaded once via index.html and intentionally
// not tied to any component lifecycle, since the behavior it suppresses is
// WebView-wide (F5/Ctrl+R reload).

// Prevent F5 and Ctrl+R from refreshing the WebView
document.addEventListener("keydown",
    function(e) {
        if (e.key === "F5" || (e.ctrlKey && (e.key === "r" || e.key === "R"))) {
            e.preventDefault();
        }
    },
    true);
