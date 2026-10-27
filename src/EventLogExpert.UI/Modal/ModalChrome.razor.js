// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

import { normalizeReturnTarget } from '../Banner/NotificationCenter.razor.js';

// Capture the previously focused element on open and restore it on close so closing a modal
// (Esc, native cancel, footer buttons) returns keyboard focus to the trigger that opened it,
// matching native dialog accessibility expectations (WAI-ARIA Authoring Practices: Dialog).
// Restoration only reclaims orphaned focus, so a caller that already moved focus keeps its target.
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
    ref._returnFocusElement = previouslyFocused instanceof HTMLElement && previouslyFocused !== document.body
        ? normalizeReturnTarget(previouslyFocused)
        : null;

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
        // Dismiss in-dialog tooltips before the dialog leaves the top layer so none remain stranded.
        document.dispatchEvent(new Event("focus-tooltip:dismiss"));
        ref.close();
    }

    // Wait for close and DOM detach; otherwise native focus restoration can overwrite our target with body.
    requestAnimationFrame(() => {
        const activeElement = document.activeElement;
        if (activeElement && activeElement !== document.body && !ref.contains(activeElement)) { return; }

        const normalizedTarget = normalizeReturnTarget(returnTarget);
        if (isUsableFocusTarget(normalizedTarget)) {
            normalizedTarget.focus({ preventScroll: true });
            return;
        }

        const openDialogs = [...document.querySelectorAll("dialog[open]")];
        const topmostDialog = openDialogs.at(-1);
        if (topmostDialog instanceof HTMLDialogElement) {
            const dialogTargets = [
                topmostDialog.querySelector(".dialog-body[tabindex='-1']"),
                topmostDialog.querySelector("h2[tabindex='-1']")
            ];
            const dialogTarget = dialogTargets.find(isUsableFocusTarget);
            if (dialogTarget) {
                dialogTarget.focus({ preventScroll: true });
                return;
            }
        }

        const workspaceTarget = document.querySelector("[data-notification-focus-fallback]");
        if (isUsableFocusTarget(workspaceTarget)) {
            workspaceTarget.focus({ preventScroll: true });
        }
    });
}

export function captureInlineAlertFocus(dialog) {
    dialog._inlineReturnFocusElement = normalizeReturnTarget(document.activeElement);
}

export function rememberInlineAlert(dialog) {
    dialog._inlineAlertElement = dialog.querySelector('.inline-alert');
}

export function restoreInlineAlertFocus(dialog) {
    const returnTarget = dialog._inlineReturnFocusElement;
    const inlineAlert = dialog._inlineAlertElement;
    dialog._inlineReturnFocusElement = null;
    dialog._inlineAlertElement = null;
    requestAnimationFrame(() => {
        if (!dialog.isConnected || !dialog.open) { return; }
        const activeElement = document.activeElement;
        if (activeElement && activeElement !== document.body && !inlineAlert?.contains(activeElement)) { return; }
        const normalizedTarget = normalizeReturnTarget(returnTarget);
        const focusTarget = isUsableFocusTarget(normalizedTarget) ? normalizedTarget
            : dialog.querySelector('h2[tabindex="-1"]') ?? dialog.querySelector('.dialog-body');
        if (focusTarget instanceof HTMLElement) {
            if (!focusTarget.hasAttribute('tabindex')) { focusTarget.tabIndex = -1; }
            focusTarget.focus({ preventScroll: true });
        }
    });
}

function isUsableFocusTarget(target) {
    return target instanceof HTMLElement
        && target.isConnected
        && target.getClientRects().length > 0
        && !target.matches(":disabled")
        && !target.closest("[inert]")
        && target.getAttribute("aria-hidden") !== "true";
}