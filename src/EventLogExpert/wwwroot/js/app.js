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

// Scroll a referenced element into the nearest scrollable ancestor so expanding inline panels
// (e.g., tag editor under a library row) don't get clipped by modal body overflow.
window.scrollElementIntoView = (element) => {
    if (element && typeof element.scrollIntoView === "function") {
        const reduceMotion = window.matchMedia("(prefers-reduced-motion: reduce)").matches;
        element.scrollIntoView({ block: "nearest", behavior: reduceMotion ? "auto" : "smooth" });
    }
};

