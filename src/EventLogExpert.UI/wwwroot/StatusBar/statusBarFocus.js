// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

// Pure decision: does keyboard focus currently rest on a real control other than the document root? body,
// documentElement, null (focus orphaned by a removed element), and non-HTMLElement nodes (e.g. SVG, text) all count
// as "not elsewhere", so the status bar's restore proceeds; a real HTMLElement somewhere else counts as elsewhere, so
// the restore is suppressed. Exported separately from the DOM wrapper so its branch logic can be unit-tested.
export function isActiveElementElsewhere(active, body, documentElement) {
    return active instanceof HTMLElement && active !== body && active !== documentElement;
}

// Restores focus to element unless focus already rests on a real control elsewhere. The status bar calls this instead
// of a bare ElementReference.FocusAsync so the "is focus still orphaned?" check and the focus happen in a SINGLE JS
// round trip: were they two separate interop calls, a fault-deferred clear could let the user move focus between the
// check and the focus, and the focus would then steal it. Returns whether it moved focus.
export function focusIfNotElsewhere(element, preventScroll) {
    if (isActiveElementElsewhere(document.activeElement, document.body, document.documentElement)) {
        return false;
    }

    element.focus({ preventScroll });

    return true;
}
