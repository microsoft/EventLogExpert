// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

const statsState = {
    observer: null,
    dotNetRef: null,
    element: null,
    debounceTimer: 0,
    session: 0,
    lastHeight: -1
};

// Report ONE section's height to .NET so it can fit the row count to the available per-column space. Sections share a
// height (grid-auto-rows: 1fr), so the first one represents every column across the 1/2/4-column reflow. The .NET side
// owns the session token (assigned before this interop), so a pane disposed mid-init tears down the exact observer it
// created, and a stale teardown from a superseded pane no-ops - the live pane always keeps its observer.
export function initStatsResize(session, dotNetRef, element) {
    stopObserver();

    if (element == null) {
        return;
    }

    statsState.session = session;
    statsState.dotNetRef = dotNetRef;
    statsState.element = element;
    statsState.lastHeight = -1;

    statsState.observer = new ResizeObserver(() => measureAndReport(session));
    statsState.observer.observe(element);
}

// One-shot re-measure after a scan first renders the section elements: their appearance does not resize the pane box,
// so the ResizeObserver would not otherwise re-fire to correct the initial (sectionless) fit.
export function remeasureStatsResize(session) {
    measureAndReport(session);
}

export function disposeStatsResize(session) {
    // Only the owning pane tears down; a stale token (a newer pane already re-init'd, or a pane that never bound) is a
    // no-op, so a late-disposing pane can never disconnect the live pane's observer.
    if (session !== statsState.session) {
        return;
    }

    stopObserver();
}

function measureAndReport(session) {
    const element = statsState.element;

    if (element == null || session !== statsState.session) {
        return;
    }

    // Sections exist only after the first scan publishes, and are display:none (clientHeight 0) in the narrow
    // single-column layout; in both cases keep the current fit - which still feeds the always-visible header - rather
    // than measuring a zero-height box.
    const section = element.querySelector(".stats-section");

    if (section == null || section.clientHeight === 0) {
        return;
    }

    const height = Math.round(section.clientHeight);

    if (statsState.debounceTimer) {
        clearTimeout(statsState.debounceTimer);
    }

    // Debounced so a drag-resize (a burst of ResizeObserver callbacks) can't flood the circuit.
    statsState.debounceTimer = setTimeout(() => {
        statsState.debounceTimer = 0;

        const ref = statsState.dotNetRef;

        if (ref == null || session !== statsState.session || height === statsState.lastHeight) {
            return;
        }

        statsState.lastHeight = height;
        ref.invokeMethodAsync("OnStatsResized", height).catch(() => { });
    }, 100);
}

function stopObserver() {
    if (statsState.observer) {
        statsState.observer.disconnect();
        statsState.observer = null;
    }

    if (statsState.debounceTimer) {
        clearTimeout(statsState.debounceTimer);
        statsState.debounceTimer = 0;
    }

    statsState.session = 0;
    statsState.dotNetRef = null;
    statsState.element = null;
    statsState.lastHeight = -1;
}
