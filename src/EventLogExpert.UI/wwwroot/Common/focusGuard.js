// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

// Does focus rest on a real control other than the document root? null, body, documentElement, and non-HTMLElement
// nodes (SVG, text) all count as "not elsewhere" so a caller's restore proceeds. Exported for unit testing.
export function isActiveElementElsewhere(active, body, documentElement) {
    return active instanceof HTMLElement && active !== body && active !== documentElement;
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

// Unguarded focus for a sibling landmark (the filters pane) the caller holds no ElementReference to. A null match is a
// no-op, not an error: closing all logs unmounts the pane too. Returns whether focus landed.
export function focusSelector(selector, preventScroll) {
    const element = document.querySelector(selector);

    if (!element) { return false; }

    element.focus({ preventScroll });

    return document.activeElement === element;
}

// Guarded (focusIfNotElsewhere) focus against a selector, for the deferred group-clear restore.
export function focusSelectorIfNotElsewhere(selector, preventScroll) {
    const element = document.querySelector(selector);

    if (!element) { return false; }

    return focusIfNotElsewhere(element, preventScroll);
}
