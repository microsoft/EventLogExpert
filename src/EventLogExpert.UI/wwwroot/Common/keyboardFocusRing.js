// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

// Tracks the current input modality on <html data-modality="keyboard|pointer"> so CSS can keep the focus
// ring keyboard-only for the controls where the browser's native :focus-visible is unreliable: editable /
// search inputs (Chromium matches :focus-visible on a plain mouse click) and elements the chrome autofocuses
// on open (Chromium matches :focus-visible on an autofocused <dialog> button opened by pointer - crbug
// 469468111). Those selectors gate their ring on :root[data-modality="keyboard"]; every other control keeps
// native :focus-visible, which is already keyboard-only in WebView2.
//
// The attribute lives on the root and is only flipped by real input events, so it persists across the async
// gap between the opening click/keypress and a modal's deferred programmatic focus - which a per-element
// marker cannot, because programmatic focus dispatches no pointer event on the target. Capture-phase
// listeners run before any component can stopPropagation. The default is keyboard - seeded statically on
// <html data-modality> in index.html and re-asserted here as a fallback - so focus placed before the first
// input event (app load, assistive tech), or if this module ever fails to load, still shows a ring
// (WCAG 2.4.7) instead of silently dropping it app-wide. One idempotent registration wires the listeners
// for the whole app.

// Lone modifier keydowns are not navigation. AltGraph is included because Windows AltGr dispatches its own
// keydown (key "AltGraph") right before the printable character, which would otherwise flip modality mid-word
// on non-US layouts.
const MODIFIER_KEYS = new Set(["Shift", "Control", "Alt", "Meta", "AltGraph"]);
const TEXT_ENTRY_INPUT_TYPES = new Set(["text", "search", "url", "tel", "email", "password", "number"]);
// Caret / edit keys that, like a printable character, edit the field's text rather than navigate away from
// it. ArrowUp / ArrowDown are intentionally excluded: this app has no multiline text inputs, so they never
// move a caret here - they page combobox options (ValueSelect / TagPicker), which is navigation that should
// ring. Every other key in a text box (Enter, Escape, F6, Tab, a Ctrl/Alt shortcut) is likewise a command.
const EDIT_KEYS = new Set(["Backspace", "Delete", "ArrowLeft", "ArrowRight", "Home", "End"]);
let registered = false;

// True for controls where a keystroke edits text (not navigation), so the caret - not a focus ring - is the
// right focus cue. Readonly/disabled fields (e.g. the select-only value-select) are excluded: there a key is
// navigation and should ring. Tab is handled by the caller as the one navigation key that still counts here.
function isTextEntry(element) {
    if (!element) { return false; }
    if (element.isContentEditable) { return true; }
    if (element.tagName === "TEXTAREA") { return !element.readOnly && !element.disabled; }
    if (element.tagName === "INPUT") {
        return TEXT_ENTRY_INPUT_TYPES.has(element.type) && !element.readOnly && !element.disabled;
    }

    return false;
}

export function registerKeyboardFocusRing() {
    if (registered) { return; }
    registered = true;

    const root = document.documentElement;
    // The control a just-clicked <label> forwards to, so the click handler can tell that browser-synthesised
    // detail-0 click apart from a real keyboard / assistive-tech activation.
    let forwardedClickTarget = null;

    const setModality = (modality) => {
        if (root.getAttribute("data-modality") !== modality) {
            root.setAttribute("data-modality", modality);
        }
    };

    if (!root.hasAttribute("data-modality")) { setModality("keyboard"); }

    document.addEventListener("keydown", (e) => {
        // A lone modifier, or a text-composition keystroke - an IME keydown (including the first one, reported
        // as key "Process" before isComposing flips true) or a dead key (key "Dead", the accent in a dead-key
        // + vowel sequence) - is not a navigation intent, so it must not flip a mouse session into keyboard
        // mode.
        if (e.isComposing || e.key === "Process" || e.key === "Dead" || MODIFIER_KEYS.has(e.key)) { return; }

        // Plain typing or caret editing inside a text box is not navigation - the blinking caret already
        // shows focus - so it must not reveal the ring on a field the user clicked into. A command keystroke
        // there (Tab, Enter, Escape, F6, a Ctrl/Alt/Meta shortcut) still moves or acts on focus, so it flips;
        // every other control (buttons, menus, readonly selects) flips on any key. AltGr is reported as
        // Ctrl+Alt on non-US layouts but produces printable characters, so it counts as typing, not a command.
        const isAltGr = e.getModifierState("AltGraph");
        const hasCommandModifier = !isAltGr && (e.ctrlKey || e.altKey || e.metaKey);
        const isPlainTyping = !hasCommandModifier && (e.key.length === 1 || EDIT_KEYS.has(e.key));
        if (isPlainTyping && isTextEntry(e.target)) { return; }

        setModality("keyboard");
    }, true);

    document.addEventListener("pointerdown", () => {
        forwardedClickTarget = null;
        setModality("pointer");
    }, true);

    // Tell a genuine keyboard / assistive-tech activation (a trusted detail-0 click with no pointer behind
    // it) apart from a <label>-forwarded mouse click: clicking a <label> bound to a control makes the browser
    // dispatch a detail-0 click on that control right after the label's own detail>0 click. Only the former
    // is keyboard use; the latter must not flip a mouse session to keyboard and ring the control.
    document.addEventListener("click", (e) => {
        if (!e.isTrusted) { return; }

        if (e.detail > 0) {
            const label = e.target.closest?.("label");
            forwardedClickTarget = label ? label.control : null;
            return;
        }

        const isForwardedLabelClick = e.target === forwardedClickTarget;
        forwardedClickTarget = null;
        if (!isForwardedLabelClick) { setModality("keyboard"); }
    }, true);
}
