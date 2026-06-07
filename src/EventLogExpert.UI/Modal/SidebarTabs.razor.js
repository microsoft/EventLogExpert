// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

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
