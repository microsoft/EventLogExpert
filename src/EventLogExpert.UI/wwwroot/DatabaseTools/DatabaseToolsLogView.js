// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

// JS module backing DatabaseToolsLogView. Tracks whether the user has scrolled away from the
// bottom so the C# component can pause auto-scroll and show the "Jump to latest" pill.

const PIN_TOLERANCE_PX = 4;

export function attach(element, dotNetRef) {
    if (!element || !dotNetRef) { return; }

    let lastPinned = true;

    const computePinned = () => {
        const remaining = element.scrollHeight - element.scrollTop - element.clientHeight;
        return remaining <= PIN_TOLERANCE_PX;
    };

    const onScroll = () => {
        const pinned = computePinned();

        if (pinned !== lastPinned) {
            lastPinned = pinned;
            try {
                dotNetRef.invokeMethodAsync("OnPinStateChanged", pinned);
            } catch (e) {
                // .NET ref disposed — drop the listener
                element.removeEventListener("scroll", onScroll);
            }
        }
    };

    element.addEventListener("scroll", onScroll, { passive: true });
}

export function scrollToBottom(element) {
    if (!element) { return; }
    element.scrollTop = element.scrollHeight;
}
