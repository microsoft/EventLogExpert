// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

// A single shared, position:fixed tooltip that surfaces an icon-only control's label on BOTH keyboard focus
// and pointer hover. The native `title` attribute only appears on mouse hover, leaving keyboard users with no
// visible label; a CSS ::after bubble can't stand in because these controls live inside overflow-clipping
// scroll panes (e.g. the filter pane), so a bubble anchored to the button would be clipped. One fixed-position
// element appended to <body> escapes every ancestor clip.
//
// Opt in with data-tooltip="text" on the control. The accessible NAME still comes from the control's own
// aria-label, so this element is a purely visual aid and is NOT wired via aria-describedby (that would
// double-announce).
//
// The interaction imitates a native OS tooltip for the pointer: it waits SHOW_DELAY_MS before appearing, shows
// near the cursor's rest point, and hides promptly on leave. The bubble is `pointer-events:none` (inert): it
// can never be hovered - a deliberate tradeoff for the native feel, since the text is duplicated on the
// control and exposed via aria (so no information is lost), Escape still dismisses it, and it stays while the
// pointer is over the control. Keyboard focus shows the bubble promptly, anchored to the control (there is no
// cursor).
//
// The logic is a pure `reduce(state, event)` state machine (exported, unit-tested) driven by a thin adapter
// that owns the DOM, the timers, and the overflow-gate resolution. The reducer tracks RAW [data-tooltip]
// leaves; the adapter resolves each to the effective anchor at render time.

const SHOW_DELAY_MS = 500;
const HIDE_DELAY_MS = 100;
const CURSOR_OFFSET_X = 10;
const CURSOR_OFFSET_Y = 15;
const VIEWPORT_MARGIN = 4;

let registered = false;

// --- Pure decision helpers (no module state, no side effects), exported for unit tests. ---

// Whether an element's own text is actually clipped by its box. The >1 tolerance defeats the sub-pixel
// scrollWidth/clientWidth rounding that fractional display scaling (125-175%) produces, which would otherwise
// flip an unclipped element to "clipped" (or vice-versa) by a single device pixel.
export function isClipped(el) {
    return (el.scrollWidth - el.clientWidth > 1) || (el.scrollHeight - el.clientHeight > 1);
}

function isOverflowAnchor(el) { return !!el && !!el.hasAttribute && el.hasAttribute("data-tooltip-overflow"); }

// An overflow anchor shows its tooltip only when its text is actually clipped. Climb from the hovered/focused
// leaf past any unclipped overflow anchors to the nearest ancestor [data-tooltip] that should show (a clipped
// overflow anchor, or any non-overflow anchor); return null when nothing qualifies, so an unclipped inner
// overflow span never shadows the tooltip of the control it sits inside. `clipped` is injected for testing.
export function resolveEffectiveAnchor(leaf, clipped) {
    let anchor = leaf;
    while (anchor && isOverflowAnchor(anchor) && !clipped(anchor)) {
        anchor = anchor.parentElement ? anchor.parentElement.closest('[data-tooltip]:not([data-tooltip=""]), [data-tooltip-overflow]') : null;
    }
    return anchor;
}

// When the pointer leaves a hovered/pending anchor for a related target inside an ANCESTOR tooltip anchor, the
// hover should transfer to that ancestor - otherwise the ancestor's own pointerover is suppressed (the
// relatedTarget is still inside it) and its tooltip would never start. Returns the ancestor to hand off to, or
// null. Pure; exported for tests.
export function hoverTransferAnchor(leaf, relatedAnchor) {
    return relatedAnchor && relatedAnchor !== leaf && relatedAnchor.contains(leaf) ? relatedAnchor : null;
}

// Place a cursor-anchored bubble near the pointer rest point: offset down-right, flip to the left of the
// cursor when it would overflow the right edge and above the cursor when it would overflow the bottom, then
// clamp into the viewport. On each non-degenerate branch the returned box excludes the pointer (default:
// left>px && top>py; flip-left: right<px; flip-up: bottom<py); an over-large box clamps to the margin so the
// top-left of the text stays on screen (the bubble is inert, so an unavoidable overlap is harmless). Pure;
// exported for tests.
export function cursorPoint(px, py, tipWidth, tipHeight, viewportWidth, viewportHeight, options) {
    const offsetX = options && options.offsetX != null ? options.offsetX : CURSOR_OFFSET_X;
    const offsetY = options && options.offsetY != null ? options.offsetY : CURSOR_OFFSET_Y;
    const margin = options && options.margin != null ? options.margin : VIEWPORT_MARGIN;

    let left = px + offsetX;
    if (left + tipWidth > viewportWidth - margin) { left = px - offsetX - tipWidth; }

    let top = py + offsetY;
    if (top + tipHeight > viewportHeight - margin) { top = py - offsetY - tipHeight; }

    left = Math.max(margin, Math.min(left, viewportWidth - margin - tipWidth));
    top = Math.max(margin, Math.min(top, viewportHeight - margin - tipHeight));
    return { left, top };
}

// Given the per-line client rects of a Range over the tooltip's text, return the box width (px) that hugs the
// widest wrapped line, or null when there is nothing to measure. box-sizing:border-box (the app default) makes
// the `width` property the border box, so horizontal padding+border are added back to keep the content box
// wide enough for the widest line; content-box needs no add-back. ceil so a sub-pixel measurement can't clip
// the widest line onto a new row. Pure; exported for tests.
export function hugWidth(lineRects, boxSizing, paddingLeft, paddingRight, borderLeft, borderRight) {
    let widest = 0;
    for (const rect of lineRects) {
        if (rect.width > widest) { widest = rect.width; }
    }
    if (widest <= 0) { return null; }
    const boxExtra = boxSizing === "border-box" ?
        paddingLeft + paddingRight + borderLeft + borderRight : 0;
    return Math.ceil(widest) + boxExtra;
}

// --- Pure state machine (no DOM, no timers), exported for unit tests. ---
// State fields are RAW [data-tooltip] leaves (or null) plus display bookkeeping:
//   focusedAnchor  keyboard-focused control
//   pendingAnchor  control under the pointer whose SHOW_DELAY_MS timer is running (excluded from display)
//   hoveredAnchor  matured hovered control (wins over focus)
//   displayLeaf    the raw leaf whose display was last requested (drives the observer + attrChange)
//   displayedMode  'cursor' | 'element' | null - how displayLeaf is currently placed
//   visible        whether the adapter currently shows a bubble (set by renderResolved; read only by jitter)
//   showGen        bumped on every (re)schedule/cancel; a showTimerFire promotes only if its token still matches
//   hideScheduled  a conceal timer is pending
export function initialTooltipState() {
    return {
        focusedAnchor: null,
        pendingAnchor: null,
        hoveredAnchor: null,
        displayLeaf: null,
        displayedMode: null,
        visible: false,
        showGen: 0,
        hideScheduled: false,
    };
}

// reduce(state, event) -> { state, effects }. Pure. Events and effects are plain objects; the adapter owns the
// DOM/timer side of each effect. Invariants: render(x,m) <=> displayLeaf=x,displayedMode=m; conceal/teardown
// <=> displayLeaf=null,displayedMode=null; scheduleHide <=> hideScheduled=true, cancelHideTimer <=> false;
// pendingAnchor is never displayed and never equals hoveredAnchor; visible mirrors real on-screen presence.
// render, when emitted, is the LAST effect in the list: the adapter's render can re-enter dispatch (see
// applyRender's detached-leaf path), so terminal ordering guarantees that re-entry runs against fully
// committed state, which bounds the recursion.
export function reduce(state, event) {
    const next = { ...state };
    const effects = [];

    switch (event.type) {
        case "hoverEnter": {
            const anchor = event.anchor;
            if (anchor === next.hoveredAnchor) {
                next.hideScheduled = false;
                effects.push({ type: "cancelHideTimer" });
            } else if (anchor === next.pendingAnchor) {
                // Already counting down for this exact leaf; keep the running timer.
            } else if (anchor === next.displayLeaf && next.hideScheduled && next.displayedMode === "cursor" && next.visible) {
                // Jitter-bridge: the pointer briefly left a still-visible cursor bubble and came back to the
                // same leaf. Restore it without re-waiting the show delay.
                next.hoveredAnchor = anchor;
                next.pendingAnchor = null;
                next.showGen++;
                next.hideScheduled = false;
                effects.push({ type: "cancelHideTimer" }, { type: "cancelShowTimer" });
            } else {
                next.pendingAnchor = anchor;
                next.showGen++;
                effects.push({ type: "cancelShowTimer" }, { type: "scheduleShow", gen: next.showGen });
            }
            break;
        }
        case "hoverLeave": {
            const anchor = event.anchor;
            const transferTarget = event.transferTarget;
            if (anchor === next.pendingAnchor) {
                if (transferTarget === next.hoveredAnchor) {
                    // The pointer went back to the already-shown ancestor; drop the pending, keep the mature.
                    next.pendingAnchor = null;
                    next.showGen++;
                    effects.push({ type: "cancelShowTimer" });
                } else if (transferTarget) {
                    next.pendingAnchor = transferTarget;
                    next.showGen++;
                    effects.push({ type: "cancelShowTimer" }, { type: "scheduleShow", gen: next.showGen });
                } else {
                    next.pendingAnchor = null;
                    next.showGen++;
                    effects.push({ type: "cancelShowTimer" });
                }
            } else if (anchor === next.hoveredAnchor) {
                if (transferTarget) {
                    next.hoveredAnchor = transferTarget;
                    next.displayLeaf = transferTarget;
                    next.displayedMode = "cursor";
                    effects.push({ type: "render", anchor: transferTarget, mode: "cursor" });
                } else {
                    next.hoveredAnchor = null;
                    if (next.focusedAnchor) {
                        // Fall back to the still-focused control's tooltip immediately (no linger).
                        next.displayLeaf = next.focusedAnchor;
                        next.displayedMode = "element";
                        next.hideScheduled = false;
                        effects.push({ type: "cancelHideTimer" }, { type: "render", anchor: next.focusedAnchor, mode: "element" });
                    } else {
                        next.hideScheduled = true;
                        effects.push({ type: "scheduleHide" });
                    }
                }
            }
            break;
        }
        case "showTimerFire": {
            if (event.gen !== next.showGen || !next.pendingAnchor) { break; }
            if (!event.stillOver) { next.pendingAnchor = null; break; }
            next.hoveredAnchor = next.pendingAnchor;
            next.displayLeaf = next.pendingAnchor;
            next.displayedMode = "cursor";
            next.pendingAnchor = null;
            effects.push({ type: "capturePoint" }, { type: "render", anchor: next.hoveredAnchor, mode: "cursor" });
            break;
        }
        case "hideTimerFire": {
            if (!next.hideScheduled) { break; }
            next.hideScheduled = false;
            if (next.hoveredAnchor == null && next.focusedAnchor == null) {
                next.displayLeaf = null;
                next.displayedMode = null;
                next.visible = false;
                effects.push({ type: "conceal" });
            } else {
                const leaf = next.hoveredAnchor || next.focusedAnchor;
                const mode = next.hoveredAnchor ? "cursor" : "element";
                next.displayLeaf = leaf;
                next.displayedMode = mode;
                effects.push({ type: "render", anchor: leaf, mode });
            }
            break;
        }
        case "focusIn": {
            next.focusedAnchor = event.anchor;
            next.hideScheduled = false;
            effects.push({ type: "cancelHideTimer" });
            if (!next.hoveredAnchor) {
                next.displayLeaf = event.anchor;
                next.displayedMode = "element";
                effects.push({ type: "render", anchor: event.anchor, mode: "element" });
            }
            break;
        }
        case "focusOut": {
            if (event.anchor === next.focusedAnchor) {
                next.focusedAnchor = null;
                if (!next.hoveredAnchor) {
                    next.hideScheduled = true;
                    effects.push({ type: "scheduleHide" });
                }
            }
            break;
        }
        case "pointerDown": {
            // Native tooltips dismiss on click. Clear every surfaced anchor (including a matured hover) and
            // conceal synchronously: a top-layer bubble left showing would otherwise strand on a trigger that
            // removes itself on mousedown (Chromium fires no pointerout for a node detached under a stationary
            // pointer) and paint above a menu or dropdown opened under the cursor.
            next.focusedAnchor = null;
            next.pendingAnchor = null;
            next.hoveredAnchor = null;
            next.displayLeaf = null;
            next.displayedMode = null;
            next.visible = false;
            next.hideScheduled = false;
            next.showGen++;
            effects.push({ type: "cancelShowTimer" }, { type: "cancelHideTimer" }, { type: "conceal" });
            break;
        }
        case "escape": {
            next.focusedAnchor = null;
            next.pendingAnchor = null;
            next.hoveredAnchor = null;
            next.displayLeaf = null;
            next.displayedMode = null;
            next.visible = false;
            next.showGen++;
            next.hideScheduled = false;
            effects.push({ type: "cancelShowTimer" }, { type: "cancelHideTimer" }, { type: "teardown" });
            break;
        }
        case "scroll": {
            // A cursor bubble is pinned to a stale rest point once content scrolls (native tooltips vanish on
            // scroll), so drop it; a focus bubble stays put and repositions to its control.
            next.pendingAnchor = null;
            next.showGen++;
            effects.push({ type: "cancelShowTimer" });
            if (next.hoveredAnchor) {
                next.hoveredAnchor = null;
                next.hideScheduled = false;
                if (next.focusedAnchor) {
                    next.displayLeaf = next.focusedAnchor;
                    next.displayedMode = "element";
                    effects.push({ type: "cancelHideTimer" }, { type: "render", anchor: next.focusedAnchor, mode: "element" });
                } else {
                    next.displayLeaf = null;
                    next.displayedMode = null;
                    next.visible = false;
                    effects.push({ type: "cancelHideTimer" }, { type: "conceal" });
                }
            } else if (next.focusedAnchor) {
                effects.push({ type: "reposition" });
            } else if (next.displayLeaf) {
                next.displayLeaf = null;
                next.displayedMode = null;
                next.hideScheduled = false;
                next.visible = false;
                effects.push({ type: "cancelHideTimer" }, { type: "conceal" });
            }
            break;
        }
        case "attrChange": {
            if (next.displayLeaf) {
                effects.push({ type: "render", anchor: next.displayLeaf, mode: next.displayedMode });
            }
            break;
        }
        case "renderResolved": {
            if (event.anchor === next.displayLeaf) { next.visible = event.visible; }
            break;
        }
    }

    return { state: next, effects };
}

// --- Adapter: owns the DOM, timers, pointer tracking, and overflow-gate resolution. ---

// Visibility primitives for the single shared tooltip element. Exported (taking supportsPopover as a
// parameter, so the module reads no DOM globals at import time) to stay unit-testable with a mock element
// under `node --test`. On the popover path the element is a manual popover promoted into the browser top
// layer by showPopover(), so it renders ABOVE an open modal <dialog> (showModal() puts the dialog in the top
// layer, which no z-index can reach); on the fallback path it degrades to the prior hidden-attribute toggle.
export function showTip(element, supportsPopover) {
    if (!element) { return; }
    if (supportsPopover) {
        if (!element.isConnected) { return; }
        // Re-promote an already-open tooltip to the TOP of the top layer (above a dialog opened after it):
        // hide+show is synchronous so nothing paints between the calls (no flicker), and it moves no focus
        // (the bubble has no focusable content and no autofocus).
        if (element.matches(":popover-open")) { element.hidePopover(); }
        element.showPopover();
    } else {
        element.hidden = false;
    }
}

export function hideTip(element, supportsPopover) {
    if (!element) { return; }
    if (supportsPopover) {
        if (element.matches(":popover-open")) { element.hidePopover(); }
    } else {
        element.hidden = true;
    }
}

export function isTipShown(element, supportsPopover) {
    if (!element) { return false; }
    return supportsPopover ? element.matches(":popover-open") : !element.hidden;
}

export function registerFocusTooltip() {
    if (registered) { return; }
    registered = true;

    let state = initialTooltipState();
    let tip = null;
    let showTimer = 0;
    let hideTimer = 0;
    let pointerX = 0;
    let pointerY = 0;
    let showPointX = 0;
    let showPointY = 0;
    // The effective anchor currently on screen (may be an ancestor of displayLeaf), kept for reposition.
    let shownAnchor = null;
    // Direct references to the raw leaves the reducer last held, so a leave still matches even if the node
    // loses its data-tooltip attribute mid-hover (attribute-based re-lookup would then fail).
    let hoveredNode = null;
    let pendingNode = null;
    let attributeObserver = null;
    let escapeConsumed = false;
    // The Popover API (top-layer) shipped alongside the :popover-open selector in Chromium 114; WebView2
    // Evergreen is current Chromium. Detect once here (never at module top level - `node --test` imports this
    // DOM-free module and a top-level HTMLElement/document access would ReferenceError). Probe the SELECTOR
    // too, not just showPopover: the visibility helpers match(":popover-open"), which throws SyntaxError on a
    // UA that exposes showPopover but not the selector - fall back to the hidden-attribute toggle there rather
    // than letting every show/hide/Escape throw. Fallback still works, just without top-layer reach.
    function popoverOpenSelectorSupported() {
        try { document.createElement("div").matches(":popover-open"); return true; }
        catch { return false; }
    }
    const supportsPopover =
        typeof HTMLElement !== "undefined" && typeof HTMLElement.prototype.showPopover === "function" &&
        popoverOpenSelectorSupported();

    function ensureTip() {
        if (tip) { return tip; }
        tip = document.createElement("div");
        tip.className = "focus-tooltip";
        tip.setAttribute("role", "tooltip");
        if (supportsPopover) {
            // Manual popover: enters the top layer on showPopover(), so it can render above an open modal
            // <dialog>. Do NOT also set the `hidden` attribute - the author rule
            // `.focus-tooltip[hidden] { display:none }` would override the UA popover display and keep the
            // bubble hidden even after showPopover().
            tip.setAttribute("popover", "manual");
        } else {
            tip.hidden = true;
        }
        document.body.appendChild(tip);
        return tip;
    }

    function positionElement(anchor, element) {
        // Keyboard focus has no cursor: sit flush below the control, right edges aligned, clamped horizontally.
        const rect = anchor.getBoundingClientRect();
        const tipRect = element.getBoundingClientRect();
        const width = tipRect.width;
        const height = tipRect.height;
        const viewportWidth = document.documentElement.clientWidth;
        const viewportHeight = document.documentElement.clientHeight;

        let top = rect.bottom;
        if (top + height > viewportHeight - VIEWPORT_MARGIN && rect.top - height >= VIEWPORT_MARGIN) {
            top = rect.top - height;
        }
        // Clamp vertically too (not just horizontally): when the bubble fits neither below nor above (a tall
        // wrapped tooltip in a short viewport), keep its top edge on screen so the text stays reachable.
        top = Math.max(VIEWPORT_MARGIN, Math.min(top, viewportHeight - VIEWPORT_MARGIN - height));
        let left = rect.right - width;
        left = Math.max(VIEWPORT_MARGIN, Math.min(left, viewportWidth - VIEWPORT_MARGIN - width));

        element.style.left = `${Math.round(left)}px`;
        element.style.top = `${top}px`;
    }

    function positionCursor(element) {
        const tipRect = element.getBoundingClientRect();
        const point = cursorPoint(
            showPointX, showPointY, tipRect.width, tipRect.height,
            document.documentElement.clientWidth, document.documentElement.clientHeight,
            { offsetX: CURSOR_OFFSET_X, offsetY: CURSOR_OFFSET_Y, margin: VIEWPORT_MARGIN });
        element.style.left = `${Math.round(point.left)}px`;
        element.style.top = `${Math.round(point.top)}px`;
    }

    function hugToWrappedWidth(element) {
        // A shrink-to-fit box (width:auto) sizes to the *unwrapped* max-content width, so once a message is
        // long enough to wrap it sits at the full max-width with dead space to the right of the shorter
        // wrapped lines (text-wrap:balance evens the lines but does NOT pull the box in). Measure the widest
        // line actually rendered and set that as the width so the bubble hugs its text like a native OS
        // tooltip. Runs synchronously between showTip and positioning, so no intermediate state is painted.
        // Reset left to 0 and clear any prior hug first: a leftover inline left from a previous show would
        // otherwise shrink the available width (shrink-to-fit is min(max-content, viewport - left)) and
        // over-wrap the text before we lock the box; positionElement/positionCursor set the real left next.
        element.style.left = "0px";
        element.style.width = "";
        const range = document.createRange();
        range.selectNodeContents(element);
        const style = getComputedStyle(element);
        const width = hugWidth(
            range.getClientRects(), style.boxSizing,
            parseFloat(style.paddingLeft), parseFloat(style.paddingRight),
            parseFloat(style.borderLeftWidth), parseFloat(style.borderRightWidth));
        if (width !== null) { element.style.width = `${width}px`; }
    }

    function observe(rawLeaf, effective) {
        // Watch data-tooltip on BOTH the requested leaf and the resolved effective anchor (when different), so
        // a control that rewrites its own tooltip while a hovered inner label ascends to it still updates.
        attributeObserver ??= new MutationObserver(() => dispatch({ type: "attrChange" }));
        attributeObserver.disconnect();
        attributeObserver.observe(rawLeaf, { attributes: true, attributeFilter: ["data-tooltip"] });
        if (effective && effective !== rawLeaf) {
            attributeObserver.observe(effective, { attributes: true, attributeFilter: ["data-tooltip"] });
        }
    }

    function stopObserving() {
        if (attributeObserver) { attributeObserver.disconnect(); }
    }

    function applyRender(rawLeaf, mode) {
        const element = ensureTip();
        // A leaf removed from the DOM while displayed must be fully dropped, not concealed-in-place or
        // repositioned: Chromium fires neither pointerout (a node detached under a stationary pointer) nor
        // focusout (focus silently falls to <body>), so a stale hovered/focused anchor survives. dispatchScrollReset
        // drops a dead focused anchor (so scroll CONCEALS instead of repositioning a zero-rect node into the
        // corner) and clears a dead hovered anchor, while a hover-only dead leaf still transfers to a live focus.
        if (rawLeaf && !rawLeaf.isConnected) { dispatchScrollReset(); return; }
        const effective = resolveEffectiveAnchor(rawLeaf, isClipped);
        // An overflow anchor shows its OWN clipped text: prefer an explicit data-tooltip (e.g. a richer label),
        // else fall back to the rendered textContent (custom ChildContent / clear options carry no data-tooltip).
        let text = effective ? effective.getAttribute("data-tooltip") : null;
        if (effective && !text && isOverflowAnchor(effective)) { text = (effective.textContent || "").replace(/\s+/g, " ").trim(); }
        if (effective && text) {
            element.textContent = text;
            element.classList.toggle("focus-tooltip--wide", effective.hasAttribute("data-tooltip-overflow"));
            showTip(element, supportsPopover);
            hugToWrappedWidth(element);
            shownAnchor = effective;
            if (mode === "cursor") { positionCursor(element); } else { positionElement(effective, element); }
            observe(rawLeaf, effective);
            dispatch({ type: "renderResolved", anchor: rawLeaf, visible: isTipShown(element, supportsPopover) });
        } else {
            shownAnchor = null;
            // Keep watching the raw leaf while concealed: an overflow-gated anchor that is currently unclipped
            // can become clipped when its own text changes (e.g. a focused ValueSelect's value updates without
            // a fresh focusin), and the data-tooltip mutation must re-run the gate. Full teardown of the
            // observer only happens on an actual dismissal (the `conceal` effect).
            observe(rawLeaf, effective);
            hideTip(element, supportsPopover);
            dispatch({ type: "renderResolved", anchor: rawLeaf, visible: false });
        }
    }

    function computeStillOver() {
        const node = state.pendingAnchor;
        if (!node || !node.isConnected) { return false; }
        const under = document.elementFromPoint(pointerX, pointerY);
        return !!under && node.contains(under);
    }

    function applyEffect(effect) {
        switch (effect.type) {
            case "scheduleShow":
                if (showTimer) { clearTimeout(showTimer); }
                showTimer = setTimeout(() => {
                    showTimer = 0;
                    dispatch({ type: "showTimerFire", gen: effect.gen, stillOver: computeStillOver() });
                }, SHOW_DELAY_MS);
                break;
            case "cancelShowTimer":
                if (showTimer) { clearTimeout(showTimer); showTimer = 0; }
                break;
            case "scheduleHide":
                if (hideTimer) { clearTimeout(hideTimer); }
                hideTimer = setTimeout(() => { hideTimer = 0; dispatch({ type: "hideTimerFire" }); }, HIDE_DELAY_MS);
                break;
            case "cancelHideTimer":
                if (hideTimer) { clearTimeout(hideTimer); hideTimer = 0; }
                break;
            case "capturePoint":
                showPointX = pointerX;
                showPointY = pointerY;
                break;
            case "render":
                applyRender(effect.anchor, effect.mode);
                break;
            case "reposition":
                // dispatchScrollReset drops a detached focused anchor before every scroll, so reposition only
                // ever runs for a connected shownAnchor.
                if (shownAnchor && isTipShown(tip, supportsPopover)) { positionElement(shownAnchor, tip); }
                break;
            case "conceal":
                shownAnchor = null;
                stopObserving();
                hideTip(tip, supportsPopover);
                break;
            case "teardown":
                shownAnchor = null;
                hoveredNode = null;
                pendingNode = null;
                stopObserving();
                hideTip(tip, supportsPopover);
                break;
        }
    }

    function dispatch(event) {
        const result = reduce(state, event);
        state = result.state;
        // Keep the adapter's raw-node references in step with the reducer so a leave can match by containment
        // even after an anchor loses its data-tooltip attribute.
        hoveredNode = state.hoveredAnchor;
        pendingNode = state.pendingAnchor;
        for (const effect of result.effects) { applyEffect(effect); }
    }

    // Every scroll drops a detached focused anchor first: Chromium fires no focusout when a focused element is
    // removed (focus falls silently to <body>), so a dead focusedAnchor would otherwise send `scroll` into its
    // reposition branch and strand the bubble / leave stale reducer state. focusOut clears it so `scroll` conceals.
    function dispatchScrollReset() {
        if (state.focusedAnchor && !state.focusedAnchor.isConnected) { dispatch({ type: "focusOut", anchor: state.focusedAnchor }); }
        dispatch({ type: "scroll" });
    }

    function closestAnchor(node) { return node && node.closest ? node.closest('[data-tooltip]:not([data-tooltip=""]), [data-tooltip-overflow]') : null; }

    document.addEventListener("pointermove", (e) => {
        if (e.pointerType === "touch") { return; }
        pointerX = e.clientX;
        pointerY = e.clientY;
    }, true);

    document.addEventListener("pointerover", (e) => {
        if (e.pointerType === "touch") { return; }
        pointerX = e.clientX;
        pointerY = e.clientY;
        const anchor = closestAnchor(e.target);
        // A genuine enter comes from outside the anchor; movement within it (button <-> icon child) has a
        // relatedTarget already inside, and must not restart the timer or reopen after an Escape dismissal.
        if (anchor && !anchor.contains(e.relatedTarget)) { dispatch({ type: "hoverEnter", anchor }); }
    }, true);

    document.addEventListener("pointerout", (e) => {
        if (e.pointerType === "touch") { return; }
        const related = e.relatedTarget;
        // Match the leave against the stored raw nodes by containment (not a fresh attribute lookup), so it is
        // recognized even if the node lost data-tooltip while hovered.
        for (const node of [hoveredNode, pendingNode]) {
            if (node && (node === e.target || node.contains(e.target)) && node !== related && !node.contains(related)) {
                const transferTarget = hoverTransferAnchor(node, closestAnchor(related));
                dispatch({ type: "hoverLeave", anchor: node, transferTarget });
            }
        }
    }, true);

    document.addEventListener("focusin", (e) => {
        const anchor = closestAnchor(e.target);
        // Only KEYBOARD focus should surface the tooltip; a pointer click also focuses the control (and is
        // governed by hover instead). :focus-visible is the browser's keyboard-focus signal for these buttons
        // in WebView2; when the tooltip lives on a wrapper (<label> around the real <input>), accept a
        // keyboard-focused descendant.
        const active = document.activeElement;
        const keyboardFocused = !!anchor && (anchor.matches(":focus-visible") ||
            (anchor.contains(active) && !!active && active.matches?.(":focus-visible")));
        if (keyboardFocused) { dispatch({ type: "focusIn", anchor }); }
        else if (state.focusedAnchor) { dispatch({ type: "focusOut", anchor: state.focusedAnchor }); }
    }, true);

    document.addEventListener("pointerdown", () => { dispatch({ type: "pointerDown" }); }, true);

    document.addEventListener("focusout", (e) => {
        const anchor = closestAnchor(e.target);
        if (anchor && anchor === state.focusedAnchor) { dispatch({ type: "focusOut", anchor }); }
    }, true);

    document.addEventListener("keydown", (e) => {
        if (e.key !== "Escape") { return; }
        // Dismiss on Escape without moving focus (WCAG 1.4.13 dismissable), and cancel a still-pending (not yet
        // shown) hover so it can't pop after the key. Only CONSUME the key when a bubble was actually visible,
        // so a merely-pending hover doesn't steal Escape from a host dialog. Keep consuming the auto-repeat
        // keydowns from the same held press via the latch (cleared on keyup).
        const wasVisible = isTipShown(tip, supportsPopover);
        if (hasActiveTooltipState()) { dispatch({ type: "escape" }); }
        if (wasVisible) { escapeConsumed = true; }
        if (escapeConsumed) {
            e.stopPropagation();
            e.preventDefault();
        }
    }, true);

    document.addEventListener("keyup", (e) => { if (e.key === "Escape") { escapeConsumed = false; } }, true);

    window.addEventListener("blur", () => { escapeConsumed = false; });

    // A modal is about to enter the top layer: ModalChrome.razor.js dispatches this on document immediately
    // before dialog.showModal(). Tear down any showing/pending tooltip so it can't be stranded behind the
    // dialog (top-layer order is show order) or consume an Escape meant for the modal. Reuses the existing
    // `escape` transition (full reset + cancel timers + teardown/hidePopover); a safe no-op when idle, and it
    // does not touch escapeConsumed (that latch lives only in the keydown handler). When the modal later
    // closes and restores focus to its launcher, the focusin listener re-shows the tooltip normally.
    document.addEventListener("focus-tooltip:dismiss", () => { dispatch({ type: "escape" }); });

    // Reposition/drop tooltips on scroll or resize. Gate on reducer state, not bubble visibility: during the
    // 500ms hover-intent window the bubble is still hidden, but a scroll must still cancel that pending show
    // (a cursor rest point goes stale once content moves).
    function hasActiveTooltipState() {
        return !!(state.pendingAnchor || state.hoveredAnchor || state.focusedAnchor || state.displayLeaf);
    }

    document.addEventListener("scroll", () => {
        if (hasActiveTooltipState()) { dispatchScrollReset(); }
    }, true);

    window.addEventListener("resize", () => {
        if (!hasActiveTooltipState()) { return; }
        // Cancel a still-pending (not-yet-shown) hover: a drag-resize freezes the pointer coordinates, so the
        // captured rest point can go stale while the show timer runs. Only when nothing is displayed - a shown
        // leaf is re-resolved in place below rather than dropped.
        if (state.pendingAnchor && !state.displayLeaf) { dispatchScrollReset(); }
        // A resize can change both the wrap width the bubble hugged (max-width is min(32rem, 90vw)) AND whether
        // an overflow-gated anchor's text is now clipped. Re-render the current display leaf - hover (cursor) or
        // focus (element) - so it re-hugs and the overflow gate re-resolves: a newly-clipped anchor shows, a
        // no-longer-clipped one hides. Unlike a scroll (which drops a cursor bubble pinned to a rest point that
        // content has moved out from under), the pointer is stationary through a resize, so the cursor anchor
        // stays valid and the hover bubble is re-resolved in place rather than dismissed.
        dispatch({ type: "attrChange" });
    });
}
