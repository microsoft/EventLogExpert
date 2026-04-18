// App-global keydown handlers. Loaded once via index.html and intentionally
// not tied to any component lifecycle, since the behavior they suppress is
// WebView-wide (F5/Ctrl+R reload) or applies to the always-present
// TableColumnMenu in MainLayout (Space-key default scroll).

// Prevent F5 and Ctrl+R from refreshing the WebView
document.addEventListener("keydown",
    function(e) {
        if (e.key === "F5" || (e.ctrlKey && (e.key === "r" || e.key === "R"))) {
            e.preventDefault();
        }
    },
    true);

// Prevent the browser's default Space-key scroll when activating a
// role="button"/menuitem inside the column menu (via keyboard). This runs
// natively alongside Blazor's @onkeydown so Tab navigation still works.
document.addEventListener("keydown",
    function(e) {
        if (e.key !== " ") {
            return;
        }

        const target = e.target;
        if (target && target.closest &&
            target.closest('#table-column-menu [role="button"], #table-column-menu [role="menuitem"]')) {
            e.preventDefault();
        }
    });
