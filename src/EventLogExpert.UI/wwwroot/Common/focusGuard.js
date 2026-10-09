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

    element.focus({ preventScroll });

    return document.activeElement === element;
}

// Guarded (focusIfNotElsewhere) focus against a selector, for the filters-pane fallback the caller holds no
// ElementReference to. A null match is a no-op, not an error: closing all logs unmounts the pane too.
export function focusSelectorIfNotElsewhere(selector, preventScroll) {
    const element = document.querySelector(selector);

    if (!element) { return false; }

    return focusIfNotElsewhere(element, preventScroll);
}
