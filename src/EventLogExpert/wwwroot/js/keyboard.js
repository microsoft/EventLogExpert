// Bridges document keydown events to the Blazor KeyboardShortcutService.
// Owns native-copy guarding so Ctrl+C never breaks the user's text selection
// in inputs/textareas/contenteditables or steals an active page selection.
let keyboardShortcutRef = null;
let keyboardShortcutListener = null;

const isEditableTarget = (target) => {
    if (!target) { return false; }
    const tag = (target.tagName || "").toLowerCase();
    if (tag === "input" || tag === "textarea" || tag === "select") { return true; }
    return !!target.isContentEditable;
};

const shouldSkipCopyShortcut = (e) => {
    if (isEditableTarget(e.target)) { return true; }
    const selection = window.getSelection ? window.getSelection() : null;
    if (selection && selection.toString().length > 0) { return true; }
    return false;
};

window.registerKeyboardShortcuts = (ref) => {
    // Always update the DotNetObjectReference so a re-register from .NET (hot reload, circuit
    // restart, WebView reuse) doesn't leave the bridge holding a stale reference whose invoke
    // calls would silently fail. The listener itself reads the latest ref via the closure-bound
    // keyboardShortcutRef variable, so reusing the existing listener with a fresh ref is safe.
    keyboardShortcutRef = ref;
    if (keyboardShortcutListener) { return; }
    keyboardShortcutListener = (e) => {
        // Drop key auto-repeat so holding a shortcut down can't open multiple file pickers,
        // toggle "Show All Events" repeatedly, etc. The .NET handler is fire-and-forget, so
        // throttling has to live here in the bridge.
        if (e.repeat) { return; }
        if (!e.ctrlKey || e.metaKey) { return; }
        if (e.altKey || e.shiftKey) { return; }

        const code = e.code;
        if (code !== "KeyO" && code !== "KeyH" && code !== "KeyC") { return; }

        // Ctrl+C must yield to the browser's native copy whenever the user could
        // reasonably be copying text. The .NET handler can't tell from afar, so the
        // guard lives here.
        if (code === "KeyC" && shouldSkipCopyShortcut(e)) { return; }

        // Suppress the browser default and stop propagation SYNCHRONOUSLY (capture phase +
        // stopPropagation prevents component-level @onkeydown handlers — e.g., EventTable
        // Ctrl+C — from also processing the same keydown and firing the shortcut twice).
        // Awaiting the .NET invoke first would yield to the browser, allowing the native
        // file-open / history-nav / copy default to fire before preventDefault could run.
        e.preventDefault();
        e.stopPropagation();

        const ref = keyboardShortcutRef;
        if (ref === null) { return; }

        // Fire-and-forget into .NET. The .NET handler returns Task (no bool) — we already
        // suppressed the browser default synchronously above, and modal-gating decides on the
        // .NET side whether to actually run the action. Errors are swallowed so a transient
        // JS<->NET hiccup doesn't surface as an unhandled promise rejection in the WebView.
        ref.invokeMethodAsync(
            "HandleShortcutAsync", code, e.ctrlKey, e.altKey, e.shiftKey, e.metaKey)
            .catch(() => { /* ignore — .NET side may be tearing down */ });
    };
    document.addEventListener("keydown", keyboardShortcutListener, true);
};

window.unregisterKeyboardShortcuts = () => {
    if (keyboardShortcutListener) {
        document.removeEventListener("keydown", keyboardShortcutListener, true);
        keyboardShortcutListener = null;
    }
    keyboardShortcutRef = null;
};
