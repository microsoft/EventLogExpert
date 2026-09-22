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

    // Dismiss any focus tooltip before the dialog enters the top layer, so a launcher tooltip can't be
    // stranded behind the modal or consume an Escape meant for it. focusTooltip.js listens for this on
    // document; the event is a no-op when the tooltip isn't registered or nothing is showing.
    document.dispatchEvent(new Event("focus-tooltip:dismiss"));

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

    if (ref.open) {
        // Symmetric with showModal (which gates on ref.open too): tear down any tooltip showing over the
        // dialog before it leaves the top layer, so one anchored to in-dialog content isn't stranded on the
        // body-level popover after the dialog detaches. A no-op when nothing is showing; the focus restored
        // below re-fires focusin to re-show a launcher tooltip.
        document.dispatchEvent(new Event("focus-tooltip:dismiss"));
        ref.close();
    }

    // Defer focus to the next frame so the dialog's close + DOM detach completes first; otherwise
    // browsers may move focus to <body> after we set it.
    if (returnTarget && document.body.contains(returnTarget) && typeof returnTarget.focus === "function") {
        requestAnimationFrame(() => {
            try { returnTarget.focus({ preventScroll: true }); }
            catch { /* element detached between frames; nothing to restore */ }
        });
    }
}

