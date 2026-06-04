// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

// Suppresses the browser's default action ONLY for the roving-focus keys the C# OnTabKeyDownAsync
// handler consumes (ArrowUp/ArrowDown/Home/End), so the dialog no longer scrolls underneath the
// vertical tablist keyboard nav. ArrowLeft/ArrowRight are intentionally NOT in the handled set
// for vertical tablists per WAI-ARIA APG 1.2. Tab/Shift+Tab/Enter/Space are NOT prevented.

const HANDLED_KEYS = new Set(["ArrowUp", "ArrowDown", "Home", "End"]);

const handlers = new WeakMap();

export function attach(tablistElement) {
    if (!tablistElement) { return; }

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
