// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

// Does focus rest on a real control other than the document root? Only null (orphaned) and the two document roots are
// "not elsewhere" so the restore proceeds; any other focused element, including a non-HTMLElement SVG control, counts as
// elsewhere and suppresses the restore. Exported for unit testing.
export function isActiveElementElsewhere(active, body, documentElement) {
    return active !== null && active !== body && active !== documentElement;
}

// Check-and-focus in one JS round trip (two interop calls would let a deferred removal move focus between them, then
// steal it). Returns whether focus actually landed - a no-op focus() on a hidden/non-focusable target reports false.
export function focusIfNotElsewhere(element, preventScroll) {
    if (isActiveElementElsewhere(document.activeElement, document.body, document.documentElement)) {
        return false;
    }

    const focusTarget = getStatusBarFocusTarget(element);
    focusTarget.focus({ preventScroll });

    return document.activeElement === focusTarget;
}

function getStatusBarFocusTarget(element) {
    if (typeof element.closest !== 'function') {
        return element;
    }

    const hiddenItem = element.closest('[data-statusbar-hidden]');
    if (!hiddenItem) {
        return element;
    }

    const content = hiddenItem.closest('.status-bar-content');
    const countsButton = content?.querySelector('[data-statusbar-counts]');
    if (isUsableFocusTarget(countsButton)) {
        return countsButton;
    }

    const footer = content?.closest('footer[data-pane="status"]');
    const statusBar = content?.closest('.status-bar') ?? footer;
    const notificationTrigger = statusBar?.querySelector('[data-notification-trigger]');
    if (isUsableFocusTarget(notificationTrigger)) {
        return notificationTrigger;
    }

    return [footer, statusBar].find(isUsableFocusTarget) ?? element;
}

export function isUsableFocusTarget(element) {
    return element instanceof HTMLElement
        && element.isConnected
        && !element.disabled
        && !element.closest('[inert]')
        && !element.closest('[aria-hidden="true"]')
        && !element.closest('[data-statusbar-hidden]')
        && element.getClientRects().length > 0
        && element.ownerDocument.defaultView.getComputedStyle(element).display !== 'none';
}

// Guarded (focusIfNotElsewhere) focus against a selector, for the filters-pane fallback the caller holds no
// ElementReference to. A null match is a no-op, not an error: closing all logs unmounts the pane too.
export function focusSelectorIfNotElsewhere(selector, preventScroll) {
    const element = document.querySelector(selector);

    if (!element) { return false; }

    return focusIfNotElsewhere(element, preventScroll);
}
