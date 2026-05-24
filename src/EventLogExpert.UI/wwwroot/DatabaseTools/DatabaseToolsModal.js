// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

// JS shim for DatabaseToolsModal. Suppresses the browser's default action ONLY for the
// roving-focus keys the C# OnTabKeyDown handler consumes (Arrow/Home/End), so the dialog
// no longer scrolls underneath the tab keyboard nav. Tab/Shift+Tab/Enter/Space are NOT
// prevented — Tab still moves focus to the tabpanel per WAI-ARIA, Enter/Space activate.

const HANDLED_KEYS = new Set(["ArrowUp", "ArrowDown", "ArrowLeft", "ArrowRight", "Home", "End"]);

const handlers = new WeakMap();

export function attach(tablistElement) {
    if (!tablistElement) { return; }

    // Remove prior listener so re-attach doesn't double-register.
    detach(tablistElement);

    const onKeyDown = (event) => {
        const target = event.target;

        if (!target || target.getAttribute("role") !== "tab") { return; }
        if (!HANDLED_KEYS.has(event.key)) { return; }

        event.preventDefault();
    };

    tablistElement.addEventListener("keydown", onKeyDown);
    handlers.set(tablistElement, onKeyDown);
}

export function detach(tablistElement) {
    if (!tablistElement) { return; }

    const onKeyDown = handlers.get(tablistElement);

    if (onKeyDown) {
        tablistElement.removeEventListener("keydown", onKeyDown);
        handlers.delete(tablistElement);
    }
}
