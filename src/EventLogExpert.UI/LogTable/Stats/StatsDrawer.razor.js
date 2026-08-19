// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

const drawerState = {
    controller: null,
    dotNetRef: null,
    activeDocumentListeners: []
};

function trackDocumentListener(event, handler) {
    const options = drawerState.controller ? { signal: drawerState.controller.signal } : undefined;
    document.addEventListener(event, handler, options);
    const entry = { event, handler };
    drawerState.activeDocumentListeners.push(entry);

    return () => {
        document.removeEventListener(event, handler);
        const i = drawerState.activeDocumentListeners.indexOf(entry);
        if (i !== -1) { drawerState.activeDocumentListeners.splice(i, 1); }
    };
}

// Drag the top edge to resize the drawer. Height lives in the --stats-drawer-height custom property so the collapsed
// (height:0) state still wins without !important; the .NET side persists the final value on release.
export function enableStatsDrawerResizer(dotNetRef, savedHeight) {
    disposeStatsDrawerResizer();

    const drawer = document.querySelector(".stats-drawer");
    const resizer = drawer?.querySelector(".stats-drawer-resizer");

    if (drawer == null || resizer == null) {
        return;
    }

    if (savedHeight && savedHeight > 0) {
        const maxHeight = Math.max(80, Math.floor(window.innerHeight * 0.8));
        drawer.style.setProperty("--stats-drawer-height", `${Math.min(savedHeight, maxHeight)}px`);
    }

    drawerState.dotNetRef = dotNetRef;
    drawerState.controller = new AbortController();
    const signal = drawerState.controller.signal;

    let y = 0;
    let h = 0;
    let maxHeight = 0;
    let untrackMove = null;
    let untrackUp = null;

    const mouseMoveHandler = function(e) {
        const distance = e.clientY - y;
        // The drawer sits below the content, so dragging UP (negative distance) grows it. Clamp to a min so it stays
        // grabbable and to an 80% viewport cap so growing it can never push the status-bar toggle off-screen.
        const newHeight = Math.min(maxHeight, Math.max(80, h - distance));
        drawer.style.setProperty("--stats-drawer-height", `${newHeight}px`);
    };

    const mouseUpHandler = function() {
        if (untrackMove) { untrackMove(); untrackMove = null; }
        if (untrackUp) { untrackUp(); untrackUp = null; }

        drawer.classList.remove("stats-drawer-resizing");

        const ref = drawerState.dotNetRef;

        if (ref && drawer.isConnected) {
            const newHeight = parseInt(window.getComputedStyle(drawer).height, 10);
            ref.invokeMethodAsync("OnStatsDrawerHeightChanged", newHeight).catch(() => { });
        }
    };

    const mouseDownHandler = function(e) {
        if (e.button !== 0) { return; }

        y = e.clientY;
        h = parseInt(window.getComputedStyle(drawer).height, 10);
        maxHeight = Math.max(80, Math.floor(window.innerHeight * 0.8));
        // Suppress the open/close transition while dragging so the height tracks the pointer instead of easing.
        drawer.classList.add("stats-drawer-resizing");

        untrackMove = trackDocumentListener("mousemove", mouseMoveHandler);
        untrackUp = trackDocumentListener("mouseup", mouseUpHandler);
    };

    resizer.addEventListener("mousedown", mouseDownHandler, { signal });
}

export function disposeStatsDrawerResizer() {
    if (drawerState.controller) {
        drawerState.controller.abort();
        drawerState.controller = null;
    }

    for (const { event, handler } of drawerState.activeDocumentListeners) {
        document.removeEventListener(event, handler);
    }

    drawerState.activeDocumentListeners = [];
    drawerState.dotNetRef = null;
}
