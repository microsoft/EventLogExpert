// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

const handlers = new WeakMap();

export function suppress(container, rules) {
    if (!container) { return; }

    release(container);

    const compiled = rules.map(rule => ({ selector: rule.selector, keys: new Set(rule.keys) }));

    const onKeyDown = (event) => {
        if (!(event.target instanceof Element)) { return; }

        for (const rule of compiled) {
            if (!rule.keys.has(event.key)) { continue; }

            const match = event.target.closest(rule.selector);

            if (match && container.contains(match)) {
                event.preventDefault();

                return;
            }
        }
    };

    container.addEventListener("keydown", onKeyDown);
    handlers.set(container, onKeyDown);
}

export function release(container) {
    if (!container) { return; }

    const onKeyDown = handlers.get(container);

    if (onKeyDown) {
        container.removeEventListener("keydown", onKeyDown);
        handlers.delete(container);
    }
}
