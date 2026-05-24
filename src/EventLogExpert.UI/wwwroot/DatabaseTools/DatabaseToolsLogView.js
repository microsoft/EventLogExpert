// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

// JS module backing DatabaseToolsLogView. Tracks scroll-pin state for the C# component.

const PIN_TOLERANCE_PX = 4;

// Per-element map so detach() removes the SAME listener on re-attach.
const scrollHandlers = new WeakMap();

export function attach(element, dotNetRef) {
    if (!element || !dotNetRef) { return; }

    // Remove prior listener to avoid double-register.
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
            // invokeMethodAsync rejects async on disposal; .catch handles teardown + detaches the listener.
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
