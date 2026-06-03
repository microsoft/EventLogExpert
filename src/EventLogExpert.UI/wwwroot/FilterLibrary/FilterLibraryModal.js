// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

// JS shim for FilterLibraryModal. Suppresses the browser's default action ONLY for the
// roving-focus keys the C# OnTabKeyDown handler consumes (ArrowLeft/ArrowRight/Home/End).
// Up/Down are NOT prevented because the horizontal tablist doesn't consume them.

const HANDLED_KEYS = new Set(["ArrowLeft", "ArrowRight", "Home", "End"]);

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
