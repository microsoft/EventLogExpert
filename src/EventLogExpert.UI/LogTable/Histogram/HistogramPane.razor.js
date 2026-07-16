// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

const histogramState = {
    appliedScrollLeft: 0,
    controller: null,
    dotNetRef: null,
    inFlight: false,
    pending: null,
    resizeObserver: null,
    scrollRaf: 0,
    session: 0,
    wheelRaf: 0,
    wheelZoomDir: 0,
    wheelCursorFraction: 0.5,
    pointerId: null,
    dragStartX: 0,
    dragActive: false,
    dragShift: false,
    dragCtrl: false,
    clickTimer: 0,
    clickPendingX: 0,
    tooltipRaf: 0,
    navToken: 0
};

// Latest-only backpressure: keep one interop call in flight and dispatch only the newest input when it settles, so fast pan/zoom can't queue stale renders.
function requestInterop(payload) {
    if (histogramState.inFlight) {
        histogramState.pending = payload;

        return;
    }

    runInterop(payload);
}

function runInterop(payload) {
    const ref = histogramState.dotNetRef;

    if (ref == null) {
        return;
    }

    // histogramState is module-global, so the session token makes a disposed pane's late promise a no-op instead of clobbering a new pane's state.
    const session = histogramState.session;
    histogramState.inFlight = true;
    const call = payload.kind === 'zoom'
        ? ref.invokeMethodAsync('OnHistogramZoomed', payload.zoomIn, payload.cursorFraction, payload.token)
        : ref.invokeMethodAsync('OnHistogramPanned', payload.fraction, payload.token);

    call.catch(() => { }).finally(() => {
        if (session !== histogramState.session) {
            return;
        }

        histogramState.inFlight = false;
        const next = histogramState.pending;
        histogramState.pending = null;

        if (next) { runInterop(next); }
    });
}

export function initHistogram(dotNetRef) {
    disposeHistogram();

    const scroll = document.querySelector('.histogram-pane .histogram-scroll');

    if (scroll == null) {
        return;
    }

    histogramState.dotNetRef = dotNetRef;
    histogramState.controller = new AbortController();
    const signal = histogramState.controller.signal;

    scroll.addEventListener('scroll', () => onScroll(scroll), { signal, passive: true });
    scroll.addEventListener('wheel', (e) => onWheel(scroll, e), { signal, passive: false });
    scroll.addEventListener('keydown', onKeydownPreventDefault, { signal });
    scroll.addEventListener('pointerdown', (e) => onPointerDown(scroll, e), { signal });
    scroll.addEventListener('pointermove', (e) => onPointerMove(scroll, e), { signal });
    scroll.addEventListener('pointerup', (e) => onPointerUp(scroll, e), { signal });
    scroll.addEventListener('pointercancel', () => cancelDrag(scroll), { signal });
    scroll.addEventListener('pointerleave', hideTooltip, { signal });
    scroll.addEventListener('contextmenu', (e) => onContextMenu(scroll, e), { signal });
    scroll.addEventListener('keydown', (e) => { if (e.key === 'Escape') { cancelDrag(scroll); } }, { signal });

    histogramState.resizeObserver = new ResizeObserver(() => report(scroll));
    histogramState.resizeObserver.observe(scroll);
    report(scroll);
}

const DragThresholdPx = 3;
const MinDragZoomPx = 12;
const DoubleClickMs = 250;
const DoubleClickTolerancePx = 24;

function plotArea(scroll) {
    return scroll.getBoundingClientRect();
}

function isOnPlot(e) {
    return e.target instanceof Element && e.target.closest('.histogram-viewport') != null;
}

function onPointerDown(scroll, e) {
    if (e.button !== 0 || !isOnPlot(e)) {
        return;
    }

    hideTooltip();
    histogramState.pointerId = e.pointerId;
    histogramState.dragStartX = e.clientX;
    histogramState.dragShift = e.shiftKey;
    histogramState.dragCtrl = e.ctrlKey;
    histogramState.dragActive = false;
    scroll.setPointerCapture?.(e.pointerId);
}

function onContextMenu(scroll, e) {
    const ref = histogramState.dotNetRef;

    if (ref == null || !isOnPlot(e)) {
        return;
    }

    e.preventDefault();
    cancelDrag(scroll);
    cancelPendingClick();
    ref.invokeMethodAsync('OnHistogramUndo').catch(() => { });
}

function onPointerMove(scroll, e) {
    if (histogramState.pointerId != null && e.pointerId === histogramState.pointerId) {
        if (!histogramState.dragActive && Math.abs(e.clientX - histogramState.dragStartX) > DragThresholdPx) {
            histogramState.dragActive = true;

            cancelPendingClick();
        }

        if (histogramState.dragActive) {
            updateSelection(scroll, e.clientX);
            e.preventDefault();
        }

        return;
    }

    scheduleTooltip(scroll, e.clientX, e.clientY, e.target);
}

function onPointerUp(scroll, e) {
    if (histogramState.pointerId == null || e.pointerId !== histogramState.pointerId) {
        return;
    }

    const ref = histogramState.dotNetRef;
    const wasDrag = histogramState.dragActive;
    const shift = histogramState.dragShift;
    const ctrl = histogramState.dragCtrl;
    const rect = plotArea(scroll);
    const startX = histogramState.dragStartX;
    cancelDrag(scroll);

    if (ref == null || rect.width <= 0) {
        return;
    }

    // Clamp both endpoints to the plot before enforcing the minimum: a drag that travels >=MinDragZoomPx in raw pointer space
    // but lies mostly outside the plot (near an edge) can select fewer than MinDragZoomPx on-plot pixels, and must fall through to a no-op click rather than zoom to a sliver.
    const dragStartPx = clamp(startX, rect.left, rect.left + rect.width);
    const dragEndPx = clamp(e.clientX, rect.left, rect.left + rect.width);

    if (wasDrag && Math.abs(dragEndPx - dragStartPx) >= MinDragZoomPx) {
        const startFraction = (dragStartPx - rect.left) / rect.width;
        const endFraction = (dragEndPx - rect.left) / rect.width;
        ref.invokeMethodAsync('OnHistogramDragSelected', Math.min(startFraction, endFraction), Math.max(startFraction, endFraction), shift).catch(() => { });

        return;
    }

    const fraction = clamp01((e.clientX - rect.left) / rect.width);

    if (ctrl) {
        cancelPendingClick();
        ref.invokeMethodAsync('OnHistogramScopeBin', fraction).catch(() => { });

        return;
    }

    arbitrateClick(ref, e.clientX);
}

function arbitrateClick(ref, clientX) {
    if (histogramState.clickTimer) {
        clearTimeout(histogramState.clickTimer);
        histogramState.clickTimer = 0;

        if (Math.abs(clientX - histogramState.clickPendingX) <= DoubleClickTolerancePx) {
            ref.invokeMethodAsync('OnHistogramReset').catch(() => { });

            return;
        }
    }

    histogramState.clickPendingX = clientX;
    histogramState.clickTimer = setTimeout(() => { histogramState.clickTimer = 0; }, DoubleClickMs);
}

function cancelPendingClick() {
    if (histogramState.clickTimer) {
        clearTimeout(histogramState.clickTimer);
        histogramState.clickTimer = 0;
    }
}

function updateSelection(scroll, clientX) {
    const selection = scroll.querySelector('.histogram-selection');

    if (selection == null) {
        return;
    }

    const rect = plotArea(scroll);
    const start = clamp(histogramState.dragStartX - rect.left, 0, rect.width);
    const current = clamp(clientX - rect.left, 0, rect.width);
    selection.style.left = `${Math.min(start, current)}px`;
    selection.style.width = `${Math.abs(current - start)}px`;
    selection.hidden = false;
}

function cancelDrag(scroll) {
    if (histogramState.pointerId != null) {
        scroll.releasePointerCapture?.(histogramState.pointerId);
    }

    histogramState.pointerId = null;
    histogramState.dragActive = false;
    const selection = scroll.querySelector('.histogram-selection');
    if (selection) { selection.hidden = true; }
}

function scheduleTooltip(scroll, clientX, clientY, target) {
    if (histogramState.tooltipRaf) {
        return;
    }

    histogramState.tooltipRaf = requestAnimationFrame(() => {
        histogramState.tooltipRaf = 0;
        const tooltip = document.querySelector('.histogram-pane .histogram-tooltip');

        if (tooltip == null) {
            return;
        }

        const group = target instanceof Element ? target.closest('[data-tip]') : null;

        if (group == null) {
            tooltip.hidden = true;

            return;
        }

        tooltip.textContent = group.getAttribute('data-tip');
        tooltip.style.left = `${clientX + 12}px`;
        tooltip.style.top = `${clientY + 12}px`;
        tooltip.hidden = false;
    });
}

function hideTooltip() {
    const tooltip = document.querySelector('.histogram-pane .histogram-tooltip');
    if (tooltip) { tooltip.hidden = true; }
}

function clamp(value, min, max) {
    return Math.max(min, Math.min(max, value));
}

function clamp01(value) {
    return clamp(value, 0, 1);
}

function report(scroll) {
    const ref = histogramState.dotNetRef;
    if (ref && scroll.isConnected) {
        ref.invokeMethodAsync('OnHistogramResized', Math.round(scroll.clientWidth), Math.round(scroll.clientHeight))
            .catch(() => { });
    }
}

function onScroll(scroll) {
    if (histogramState.scrollRaf) {
        return;
    }

    // Capture the generation at schedule time (not at dispatch): if an undo bumps it before this rAF drains, the pan is stamped stale and the .NET side no-ops it.
    const token = histogramState.navToken;

    histogramState.scrollRaf = requestAnimationFrame(() => {
        histogramState.scrollRaf = 0;
        const ref = histogramState.dotNetRef;

        if (!ref || !scroll.isConnected || scroll.scrollWidth <= 0) {
            return;
        }

        if (Math.abs(scroll.scrollLeft - histogramState.appliedScrollLeft) < 1) {
            return;
        }

        histogramState.appliedScrollLeft = scroll.scrollLeft;
        requestInterop({ kind: 'pan', fraction: scroll.scrollLeft / scroll.scrollWidth, token });
    });
}

function onWheel(scroll, e) {
    const ref = histogramState.dotNetRef;

    if (ref == null) {
        return;
    }

    if (e.shiftKey || e.deltaX !== 0) {
        if (scroll.scrollWidth > scroll.clientWidth) {
            scroll.scrollLeft += e.deltaY !== 0 ? e.deltaY : e.deltaX;
            e.preventDefault();
        }

        return;
    }

    const rect = scroll.getBoundingClientRect();
    histogramState.wheelCursorFraction = rect.width > 0 ? (e.clientX - rect.left) / rect.width : 0.5;
    histogramState.wheelZoomDir = e.deltaY < 0 ? 1 : -1;
    e.preventDefault();

    if (histogramState.wheelRaf) {
        return;
    }

    // Capture the generation at schedule time so an undo before this rAF drains stamps the zoom stale (the .NET side then no-ops it).
    const token = histogramState.navToken;

    histogramState.wheelRaf = requestAnimationFrame(() => {
        histogramState.wheelRaf = 0;

        if (histogramState.wheelZoomDir !== 0) {
            requestInterop({ kind: 'zoom', zoomIn: histogramState.wheelZoomDir > 0, cursorFraction: histogramState.wheelCursorFraction, token });
            histogramState.wheelZoomDir = 0;
        }
    });
}

function onKeydownPreventDefault(e) {
    const navigationKeys = ['ArrowLeft', 'ArrowRight', 'ArrowUp', 'ArrowDown', 'Home', 'PageUp', 'PageDown', '+', '=', '-', '_'];

    if (navigationKeys.includes(e.key)) {
        e.preventDefault();
    }
}

export function applyView(startFraction, navToken) {
    // Adopt the .NET generation so pan/zoom scheduled after this point is stamped current; anything scheduled before an undo keeps its stale token and is rejected.
    histogramState.navToken = navToken;

    const scroll = document.querySelector('.histogram-pane .histogram-scroll');

    if (scroll == null) {
        return;
    }

    const target = Math.max(0, Math.round(startFraction * scroll.scrollWidth));
    scroll.scrollLeft = target;
    // Read back the value the browser actually applied (it clamps to the max scroll offset) so the onScroll echo guard stays correct and can't misfire a spurious pan.
    histogramState.appliedScrollLeft = scroll.scrollLeft;
}

export function disposeHistogram() {
    histogramState.session++;

    if (histogramState.resizeObserver) {
        histogramState.resizeObserver.disconnect();
        histogramState.resizeObserver = null;
    }

    if (histogramState.scrollRaf) {
        cancelAnimationFrame(histogramState.scrollRaf);
        histogramState.scrollRaf = 0;
    }

    if (histogramState.wheelRaf) {
        cancelAnimationFrame(histogramState.wheelRaf);
        histogramState.wheelRaf = 0;
    }

    if (histogramState.tooltipRaf) {
        cancelAnimationFrame(histogramState.tooltipRaf);
        histogramState.tooltipRaf = 0;
    }

    if (histogramState.clickTimer) {
        clearTimeout(histogramState.clickTimer);
        histogramState.clickTimer = 0;
    }

    if (histogramState.controller) {
        histogramState.controller.abort();
        histogramState.controller = null;
    }

    histogramState.dotNetRef = null;
    histogramState.appliedScrollLeft = 0;
    histogramState.wheelZoomDir = 0;
    histogramState.inFlight = false;
    histogramState.pending = null;
    histogramState.pointerId = null;
    histogramState.dragActive = false;
    histogramState.navToken = 0;
}
