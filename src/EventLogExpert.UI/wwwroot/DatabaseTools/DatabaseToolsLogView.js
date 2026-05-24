// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

// JS module backing DatabaseToolsLogView. Tracks whether the user has scrolled away from the
// bottom so the C# component can pause auto-scroll and show the "Jump to latest" pill.

const PIN_TOLERANCE_PX = 4;

// Per-element handler map so detach() can remove the SAME listener we registered, even if
// the C# component re-attaches after a JS hot-reload or test re-render.
const scrollHandlers = new WeakMap();

export function attach(element, dotNetRef) {
    if (!element || !dotNetRef) { return; }

    // If a prior attach() lingered, remove it first so we don't double-register.
    detach(element);

    let lastPinned = true;

    const computePinned = () => {
        const remaining = element.scrollHeight - element.scrollTop - element.clientHeight;
        return remaining <= PIN_TOLERANCE_PX;
    };

    const onScroll = () => {
        const pinned = computePinned();

        if (pinned !== lastPinned) {
            lastPinned = pinned;
            // invokeMethodAsync returns a Promise that rejects asynchronously when the .NET ref is disposed.
            // try/catch only catches synchronous throws, so we attach .catch() to the returned promise to
            // suppress unhandled-rejection noise during component teardown, and detach the listener inside it.
            dotNetRef.invokeMethodAsync("OnPinStateChanged", pinned)?.catch(() => {
                element.removeEventListener("scroll", onScroll);
                scrollHandlers.delete(element);
            });
        }
    };

    element.addEventListener("scroll", onScroll, { passive: true });
    scrollHandlers.set(element, onScroll);
}

export function detach(element) {
    if (!element) { return; }
    const onScroll = scrollHandlers.get(element);
    if (onScroll) {
        element.removeEventListener("scroll", onScroll);
        scrollHandlers.delete(element);
    }
}

export function scrollToBottom(element) {
    if (!element) { return; }
    element.scrollTop = element.scrollHeight;
}
