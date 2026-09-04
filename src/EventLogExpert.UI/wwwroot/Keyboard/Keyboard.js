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

// ----- F6 / Shift+F6 pane navigation -----
// Cycles keyboard focus across the app's main panes (elements marked data-pane, membership may span
// several elements sharing a name). Focus lands on the pane's last-focused control, else a designated
// entry (data-pane-entry, used for the composite table/histogram regions), else the first tabbable
// descendant - never an inert container, matching the WAI-ARIA landmark/composite entry pattern.
// Handled synchronously here (not routed to .NET) so Chromium treats the move as keyboard input and
// shows the :focus-visible ring. Suppressed while a modal dialog or a menu overlay is open so their
// focus containment is respected. F6 reaches the DOM and preventDefault suppresses the WebView2
// "focus next pane" default, the same way the app-global F5 handler cancels reload.
const paneLastFocused = new Map();

const paneFocusInListener = (e) => {
    const paneEl = e.target && e.target.closest ? e.target.closest("[data-pane]") : null;
    if (paneEl) { paneLastFocused.set(paneEl.getAttribute("data-pane"), e.target); }
};

const isPaneVisible = (el) => {
    if (!el || !el.isConnected) { return false; }
    if (el.closest("[hidden]")) { return false; }
    // Require BOTH dimensions: a block collapsed to height:0 by an overflow:hidden ancestor (the app's
    // drawer/detail collapse pattern) still reports width, so an || check would treat it as visible.
    return el.getClientRects().length > 0 && el.offsetWidth > 0 && el.offsetHeight > 0;
};

// Tabbable elements only: tabindex="-1" is excluded so a roving-tabindex widget (the menu bar) yields
// its single active item rather than every item.
const FOCUSABLE_SELECTOR =
    'a[href]:not([tabindex="-1"]), button:not([disabled]):not([tabindex="-1"]), ' +
    'input:not([disabled]):not([tabindex="-1"]), select:not([disabled]):not([tabindex="-1"]), ' +
    'textarea:not([disabled]):not([tabindex="-1"]), [tabindex]:not([tabindex="-1"])';

const canRestoreFocus = (el) => {
    if (!el || !isPaneVisible(el) || el.disabled) { return false; }
    // Only restore a genuinely operable control. A bare tabindex="-1" wrapper (e.g. the empty-state
    // section or the footer) must not be remembered, or F6 would park on an inert container; roving
    // widget items are the tabindex="-1" exceptions worth restoring.
    return el.matches(FOCUSABLE_SELECTOR) ||
        el.matches('[role="menuitem"], [role="tab"], [role="option"]');
};

const firstFocusable = (root) => {
    if (root.matches(FOCUSABLE_SELECTOR) && isPaneVisible(root)) { return root; }
    for (const candidate of root.querySelectorAll(FOCUSABLE_SELECTOR)) {
        if (isPaneVisible(candidate)) { return candidate; }
    }
    return null;
};

const orderedVisiblePanes = () => {
    const names = [];
    document.querySelectorAll("[data-pane]").forEach((el) => {
        const name = el.getAttribute("data-pane");
        // resolvePaneTarget != null excludes panes with no operable target (e.g. an empty status bar),
        // so F6 never stops on an unfocusable/inert pane.
        if (!names.includes(name) && isPaneVisible(el) && resolvePaneTarget(name)) { names.push(name); }
    });
    return names;
};

// Resolves the element F6 should focus for a pane, or null when the pane has no operable target: a
// still-valid remembered control, else the designated data-pane-entry (the composite table/histogram
// regions), else the first tabbable descendant. Never returns a bare inert container.
const resolvePaneTarget = (name) => {
    const members = Array.from(document.querySelectorAll(`[data-pane="${name}"]`)).filter(isPaneVisible);
    if (members.length === 0) { return null; }

    const remembered = paneLastFocused.get(name);
    if (remembered && canRestoreFocus(remembered) &&
        members.some((m) => m === remembered || m.contains(remembered))) {
        return remembered;
    }

    for (const member of members) {
        const entry = member.matches("[data-pane-entry]") ? member : member.querySelector("[data-pane-entry]");
        if (entry && isPaneVisible(entry)) { return entry; }
    }

    for (const member of members) {
        const target = firstFocusable(member);
        if (target) { return target; }
    }

    return null;
};

const isPaneNavSuppressed = () =>
    document.querySelector(".menu-host-overlay") !== null ||
    document.querySelector("dialog[open]") !== null;

const movePaneFocus = (reverse) => {
    if (isPaneNavSuppressed()) { return; }

    const panes = orderedVisiblePanes();
    if (panes.length === 0) { return; }

    const activeEl = document.activeElement;
    const currentPaneEl = activeEl && activeEl.closest ? activeEl.closest("[data-pane]") : null;
    const currentName = currentPaneEl ? currentPaneEl.getAttribute("data-pane") : null;
    const currentIndex = currentName ? panes.indexOf(currentName) : -1;

    let nextIndex;
    if (currentIndex === -1) {
        nextIndex = reverse ? panes.length - 1 : 0;
    } else {
        nextIndex = reverse
            ? (currentIndex - 1 + panes.length) % panes.length
            : (currentIndex + 1) % panes.length;
    }

    const target = resolvePaneTarget(panes[nextIndex]);
    if (target) { target.focus(); }
};

export function registerKeyboardShortcuts(ref) {
    // Always update the DotNetObjectReference so a re-register from .NET (hot reload, circuit
    // restart, WebView reuse) doesn't leave the bridge holding a stale reference whose invoke
    // calls would silently fail. The listener itself reads the latest ref via the closure-bound
    // keyboardShortcutRef variable, so reusing the existing listener with a fresh ref is safe.
    keyboardShortcutRef = ref;
    if (keyboardShortcutListener) { return; }
    keyboardShortcutListener = (e) => {
        // F6 / Shift+F6 pane navigation is owned here and handled synchronously (movePaneFocus):
        // capture phase + a synchronous focus move is what makes Chromium show the keyboard
        // :focus-visible ring and lets it bypass component-level stopPropagation. Handled before the
        // repeat guard so a held F6 still cancels the WebView2 "focus next pane" default on every
        // event, while focus advances only once per physical press.
        if (e.key === "F6" && !e.ctrlKey && !e.altKey && !e.metaKey) {
            e.preventDefault();
            e.stopPropagation();
            if (!e.repeat && !e.isComposing) { movePaneFocus(e.shiftKey); }
            return;
        }

        // Drop key auto-repeat so holding a shortcut down can't open multiple file pickers,
        // toggle "Show All Events" repeatedly, etc. The .NET handler is fire-and-forget, so
        // throttling has to live here in the bridge.
        if (e.repeat || e.isComposing) { return; }

        if (!e.ctrlKey || e.metaKey) { return; }
        if (e.altKey || e.shiftKey) { return; }

        const code = e.code;
        if (code !== "KeyO" && code !== "KeyH" && code !== "KeyC" && code !== "KeyF") { return; }

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
    document.addEventListener("focusin", paneFocusInListener, true);
}

export function unregisterKeyboardShortcuts() {
    if (keyboardShortcutListener) {
        document.removeEventListener("keydown", keyboardShortcutListener, true);
        document.removeEventListener("focusin", paneFocusInListener, true);
        keyboardShortcutListener = null;
    }
    keyboardShortcutRef = null;
    paneLastFocused.clear();
}
