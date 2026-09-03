// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

// Capture the previously focused element on open and restore it on close so closing a modal
// (Esc, native cancel, footer buttons) returns keyboard focus to the trigger that opened it,
// matching native dialog accessibility expectations (WAI-ARIA Authoring Practices: Dialog).
// Sequentially-focusable controls, used to move initial focus onto the modal's content.
const focusableInBody =
    'a[href]:not([tabindex="-1"]),' +
    'button:not([disabled]):not([tabindex="-1"]),' +
    'input:not([disabled]):not([tabindex="-1"]),' +
    'select:not([disabled]):not([tabindex="-1"]),' +
    'textarea:not([disabled]):not([tabindex="-1"]),' +
    'details > summary:not([tabindex="-1"]),' +
    '[tabindex]:not([tabindex="-1"]):not([disabled])';

export function showModal(ref) {
    if (ref == null || ref.open) { return; }

    // Stash on the dialog element so close can restore even if the host component is torn down
    // before close runs (e.g. async disposal racing with native cancel).
    const previouslyFocused = document.activeElement;
    ref._returnFocusElement = (previouslyFocused instanceof HTMLElement && previouslyFocused !== document.body) ?
        previouslyFocused : null;

    ref.showModal();

    // Content modals drop their footer autofocus, so the native dialog may land focus on the
    // header/footer close button instead of the content. When focus lands outside the body and
    // was not placed by an explicit autofocus (confirm/alert footers keep theirs), redirect it to
    // the first body control, or to the body region itself for text-only modals.
    const body = ref.querySelector(":scope > .dialog-group > .dialog-body");
    const focused = document.activeElement;
    if (body instanceof HTMLElement && focused instanceof HTMLElement
        && !body.contains(focused) && !focused.hasAttribute("autofocus")) {
        const firstControl = body.querySelector(focusableInBody);
        if (firstControl instanceof HTMLElement) {
            firstControl.focus();
        } else {
            // Text-only modals: focus the actual scrolling region so Arrow/PageDown scroll the content.
            // Flex-layout bodies set overflow: hidden and place the scroll on an inner .flex-column-scroll,
            // so focusing the body itself would leave keyboard scrolling on a non-scrollable ancestor.
            const scrollTarget = body.querySelector(".flex-column-scroll") ?? body;
            scrollTarget.tabIndex = -1;
            scrollTarget.focus();
        }
    }
}

export function closeModal(ref) {
    if (ref == null) { return; }

    const returnTarget = ref._returnFocusElement;
    ref._returnFocusElement = null;

    if (ref.open) { ref.close(); }

    // Defer focus to the next frame so the dialog's close + DOM detach completes first; otherwise
    // browsers may move focus to <body> after we set it.
    if (returnTarget && document.body.contains(returnTarget) && typeof returnTarget.focus === "function") {
        requestAnimationFrame(() => {
            try { returnTarget.focus({ preventScroll: true }); }
            catch { /* element detached between frames; nothing to restore */ }
        });
    }
}

