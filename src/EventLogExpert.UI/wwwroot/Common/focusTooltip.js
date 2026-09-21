// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

// A single shared, position:fixed tooltip that surfaces an icon-only control's label on BOTH keyboard
// focus and pointer hover. The native `title` attribute only appears on mouse hover, leaving keyboard
// users with no visible label. A CSS ::after bubble can't stand in because these
// controls live inside overflow-clipping scroll panes (e.g. the filter pane, which is overflow:auto and,
// when the filter list is collapsed, only header-height): a bubble anchored to the button would be clipped
// by the pane. One fixed-position element appended to <body> escapes every ancestor clip.
//
// Opt in with data-tooltip="text" on the control. The accessible NAME still comes from the control's own
// aria-label, so this element is a purely visual aid and is intentionally NOT wired via aria-describedby
// (that would double-announce). Escape dismisses it, it stays visible while the pointer is over the control
// or the bubble (so a pointer user can read it), and scroll/resize reposition it while its trigger is active
// and drop it otherwise so it never drifts away stale (WCAG 1.4.13 dismissable / hoverable / persistent).
// One idempotent registration wires delegated, capture-phase listeners for the whole app.

let registered = false;
let tip = null;
// Keyboard focus and pointer hover are tracked separately because they can rest on DIFFERENT controls at
// once (e.g. tab to A, then hover B): the hovered control wins the display while hovered, and leaving it
// restores the still-focused control's tooltip instead of dropping it (WCAG 1.4.13 persistent).
let focusedAnchor = null;
let hoveredAnchor = null;
let pointerOverTip = false;
let hideTimer = 0;
let hoverEndTimer = 0;
let attributeObserver = null;
let escapeConsumed = false;
// The control the bubble is currently anchored to (the resolved effective anchor, which can differ from the
// hovered/focused leaf when an unclipped overflow anchor was ascended past). Used to reposition on scroll.
let shownAnchor = null;

// --- Pure decision helpers (no module state, no side effects), exported for unit tests. ---

// Whether an element's own text is actually clipped by its box. The >1 tolerance defeats the sub-pixel
// scrollWidth/clientWidth rounding that fractional display scaling (125-175%) produces, which would otherwise
// flip an unclipped element to "clipped" (or vice-versa) by a single device pixel.
export function isClipped(el) {
    return (el.scrollWidth - el.clientWidth > 1) || (el.scrollHeight - el.clientHeight > 1);
}

// The control whose tooltip should show: a hovered control takes precedence over a focused one.
export function selectAnchor(focused, hovered) { return hovered || focused; }

function isOverflowAnchor(el) { return !!el && !!el.hasAttribute && el.hasAttribute("data-tooltip-overflow"); }

// An overflow anchor shows its tooltip only when its text is actually clipped. Climb from the hovered/focused
// leaf past any unclipped overflow anchors to the nearest ancestor [data-tooltip] that should show (a clipped
// overflow anchor, or any non-overflow anchor); return null when nothing qualifies, so an unclipped inner
// overflow span never shadows the tooltip of the control it sits inside. `clipped` is injected for testing.
export function resolveEffectiveAnchor(leaf, clipped) {
    let anchor = leaf;
    while (anchor && isOverflowAnchor(anchor) && !clipped(anchor)) {
        anchor = anchor.parentElement ? anchor.parentElement.closest("[data-tooltip]") : null;
    }
    return anchor;
}

// When the pointer leaves a hovered anchor for a related target inside an ANCESTOR tooltip anchor (e.g. an
// inner overflow label to its enclosing control), the hover should transfer to that ancestor - otherwise the
// ancestor's own pointerover is suppressed (relatedTarget is still inside it) and its tooltip would vanish
// after the bridge delay even though the pointer is still over the control. Returns the ancestor to hand off
// to, or null to begin the normal hover-end. Pure; exported for tests.
export function hoverTransferAnchor(hovered, relatedAnchor) {
    return relatedAnchor && relatedAnchor !== hovered && relatedAnchor.contains(hovered) ? relatedAnchor : null;
}

// The control the user is currently pointing at or has keyboard-focused (the "leaf"); a hovered control takes
// precedence. This is the raw activity signal kept alive across scroll/hover-end; the actually-shown bubble is
// resolved from it through the overflow gate in update().
function activeAnchor() { return selectAnchor(focusedAnchor, hoveredAnchor); }

function ensureTip() {
    if (tip) { return tip; }

    tip = document.createElement("div");
    tip.className = "focus-tooltip";
    tip.setAttribute("role", "tooltip");
    tip.hidden = true;

    // Let the pointer move onto the bubble to read it without it vanishing (WCAG 1.4.13 hoverable). Leaving
    // the bubble re-derives the hovered control from where the pointer went (back onto a control, or nothing).
    // Touch has no hover and its tap-compatibility events would pin the bubble open, so only mouse/pen count.
    tip.addEventListener("pointerenter", (e) => {
        if (e.pointerType === "touch") { return; }
        pointerOverTip = true; cancelHide(); cancelHoverEnd();
    });
    tip.addEventListener("pointerleave", (e) => {
        if (e.pointerType === "touch") { return; }
        pointerOverTip = false;
        hoveredAnchor = e.relatedTarget?.closest?.("[data-tooltip]") ?? null;
        update();
    });

    document.body.appendChild(tip);

    return tip;
}

function cancelHide() {
    if (hideTimer) {
        clearTimeout(hideTimer);
        hideTimer = 0;
    }
}

function scheduleHide() {
    // Delay the hide so the pointer can bridge the small gap between the control and the bubble without it
    // vanishing, and so a focus<->hover handoff doesn't flicker.
    cancelHide();
    hideTimer = setTimeout(() => {
        hideTimer = 0;
        if (!activeAnchor() && !pointerOverTip) { hide(); }
    }, 120);
}

function cancelHoverEnd() {
    if (hoverEndTimer) {
        clearTimeout(hoverEndTimer);
        hoverEndTimer = 0;
    }
}

function scheduleHoverEnd() {
    // Keep showing the just-left hovered control through the bridge delay so the pointer can still reach its
    // tooltip; without this, when another control holds keyboard focus, activeAnchor() would fall back to the
    // focused control the instant the pointer leaves and the hovered control's tooltip could never be hovered
    // (WCAG 1.4.13 hoverable). Reaching the bubble (pointerOverTip) or another control (pointerover) cancels it.
    cancelHoverEnd();
    hoverEndTimer = setTimeout(() => {
        hoverEndTimer = 0;
        if (!pointerOverTip) {
            hoveredAnchor = null;
            update();
        }
    }, 120);
}

function position(anchor, element) {
    const rect = anchor.getBoundingClientRect();
    // The bubble sits flush against the control (no gap) so the pointer path from control to bubble is
    // continuous and staying hovered is not pointer-speed-dependent (WCAG 1.4.13): a gap would leave a
    // non-hit-testable strip that a slow crossing could dwell in past the hover-end delay, losing the bubble.
    // Measure with fractional (sub-pixel) geometry and leave the trigger-facing edge unrounded so the two
    // border-boxes share an exact coordinate at every device-pixel ratio; rounding it would reopen a
    // sub-pixel seam (or a click-stealing overlap) at fractional zoom levels such as WCAG's 400%.
    const gap = 0;
    const margin = 4;
    const tipRect = element.getBoundingClientRect();
    const width = tipRect.width;
    const height = tipRect.height;
    const viewportWidth = document.documentElement.clientWidth;
    const viewportHeight = document.documentElement.clientHeight;

    // Prefer below the control; flip above only when there is no room below but there is above.
    let top = rect.bottom + gap;
    if (top + height > viewportHeight - margin && rect.top - gap - height >= margin) {
        top = rect.top - gap - height;
    }

    // Align the tooltip's right edge to the control's, then clamp into the viewport. The horizontal edge has
    // no flush neighbor, so it stays pixel-snapped to keep the label text crisp.
    let left = rect.right - width;
    left = Math.max(margin, Math.min(left, viewportWidth - margin - width));

    element.style.left = `${Math.round(left)}px`;
    element.style.top = `${top}px`;
}

function reposition() {
    if (shownAnchor && tip && !tip.hidden) { position(shownAnchor, tip); }
}

// Re-render the tooltip for whichever control is currently active, or begin hiding it when none is. Called
// on every focus/hover change (and by the attribute observer) so the bubble always reflects current state.
function update() {
    const leaf = activeAnchor();
    const anchor = resolveEffectiveAnchor(leaf, isClipped);

    if (!anchor) {
        shownAnchor = null;
        // An active-but-suppressed leaf (an unclipped overflow anchor with no showable ancestor) conceals the
        // bubble WITHOUT clearing focus/hover state, so a still-focused control's tooltip is restored once the
        // pointer leaves the unclipped hovered control. Nothing active at all begins the normal hide.
        if (leaf) { concealTip(); }
        else if (!pointerOverTip) { scheduleHide(); }
        return;
    }

    const text = anchor.getAttribute("data-tooltip");
    if (!text) { hide(); return; }

    cancelHide();

    const element = ensureTip();
    element.textContent = text;
    // Overflow/opt-in-wide anchors get the roomier bubble so long paths/messages read on fewer, wider lines.
    element.classList.toggle("focus-tooltip--wide",
        anchor.hasAttribute("data-tooltip-overflow") || anchor.hasAttribute("data-tooltip-wide"));
    element.hidden = false;
    shownAnchor = anchor;
    position(anchor, element);

    // The control can rewrite its own data-tooltip in place while it stays focused / hovered (e.g. the
    // filter-list collapse toggle flips Expanded<->Collapsed on Enter), which dispatches neither a focus nor
    // a mouse event - watch the attribute so the visible bubble tracks the current state.
    attributeObserver ??= new MutationObserver(update);
    attributeObserver.disconnect();
    attributeObserver.observe(anchor, { attributes: true, attributeFilter: ["data-tooltip"] });
}

// Hide the bubble WITHOUT resetting focus/hover bookkeeping (unlike hide()), so a concealed overflow anchor
// doesn't tear down a still-active focus/hover that should restore its own tooltip.
function concealTip() {
    cancelHide();
    if (tip) { tip.hidden = true; }
}

function hide() {
    cancelHide();
    cancelHoverEnd();
    if (attributeObserver) { attributeObserver.disconnect(); }
    focusedAnchor = null;
    hoveredAnchor = null;
    pointerOverTip = false;
    shownAnchor = null;
    if (tip) { tip.hidden = true; }
}

export function registerFocusTooltip() {
    if (registered) { return; }
    registered = true;

    document.addEventListener("focusin", (e) => {
        const anchor = e.target.closest?.("[data-tooltip]");
        // Only KEYBOARD focus should surface the tooltip. A pointer click also focuses the control, and that
        // focus would otherwise leave the bubble stuck open after the pointer moved away. :focus-visible is
        // the browser's keyboard-focus signal, which is keyboard-only for these buttons in WebView2. When the
        // tooltip lives on a wrapper (e.g. a <label> whose inner <input> is the real focus target), the wrapper
        // itself won't match :focus-visible, so also accept a keyboard-focused descendant.
        const active = document.activeElement;
        const keyboardFocused = !!anchor && (anchor.matches(":focus-visible") ||
            (anchor.contains(active) && !!active && active.matches?.(":focus-visible")));
        focusedAnchor = keyboardFocused ? anchor : null;
        update();
    }, true);

    document.addEventListener("pointerdown", () => {
        // A pointer interaction supersedes a keyboard-focus tooltip: pointer users are governed by hover
        // alone, so a control that was tabbed to and then clicked doesn't keep its bubble after the pointer
        // leaves.
        if (focusedAnchor) { focusedAnchor = null; update(); }
    }, true);

    document.addEventListener("focusout", (e) => {
        // Focus left this control; focusin resets focusedAnchor synchronously if it lands on another anchor.
        if (focusedAnchor && e.target.closest?.("[data-tooltip]") === focusedAnchor) {
            focusedAnchor = null;
            update();
        }
    }, true);

    document.addEventListener("pointerover", (e) => {
        // Ignore touch: it has no hover, and a tap's compatibility events would open the tooltip during
        // activation and leave it lingering (touch delivers no reliable pointerout on lift). Mouse and pen
        // keep hover support (pointerType is "mouse", "pen", or "touch").
        if (e.pointerType === "touch") { return; }
        const anchor = e.target.closest?.("[data-tooltip]");
        // Ignore movement WITHIN the same control (e.g. the button <-> its icon child): a genuine enter
        // comes from outside the anchor, so relatedTarget is not already inside it. Without this guard an
        // Escape dismissal would reopen on the next internal transition even though the pointer never left
        // the trigger (WCAG 1.4.13 dismissable).
        if (anchor && !anchor.contains(e.relatedTarget)) { cancelHoverEnd(); hoveredAnchor = anchor; update(); }
    }, true);

    document.addEventListener("pointerout", (e) => {
        if (e.pointerType === "touch") { return; }
        // Left the hovered control - but not when moving between its children or onto the bubble (the bubble's
        // own pointerleave handles that handoff). Defer clearing it through the bridge delay so its tooltip stays
        // reachable even when another control holds keyboard focus (which activeAnchor() would otherwise fall
        // back to immediately).
        if (hoveredAnchor
            && e.target.closest?.("[data-tooltip]") === hoveredAnchor
            && e.relatedTarget !== tip
            && !hoveredAnchor.contains(e.relatedTarget)) {
            const outer = hoverTransferAnchor(hoveredAnchor, e.relatedTarget?.closest?.("[data-tooltip]"));
            if (outer) {
                // Pointer moved out to an enclosing control's anchor - hand the hover to it now (its own
                // pointerover is suppressed because relatedTarget is still inside it), instead of hiding.
                cancelHoverEnd();
                hoveredAnchor = outer;
                update();
            } else {
                scheduleHoverEnd();
            }
        }
    }, true);

    document.addEventListener("keydown", (e) => {
        if (e.key !== "Escape") { return; }

        // Dismiss on Escape without moving focus (WCAG 1.4.13). Consume the key while a tooltip is visible so
        // the dismissal is standalone - for a control inside a dialog, an unconsumed Escape would also run the
        // host's cancel and close it. Keep consuming the auto-repeat keydowns from the SAME held press (the
        // tooltip is already hidden after the first) via the latch, so a held Escape can't fall through and
        // close the host too. The latch clears on keyup, so a fresh Escape press reaches the host as usual.
        if (tip && !tip.hidden) {
            hide();
            escapeConsumed = true;
        }

        if (escapeConsumed) {
            e.stopPropagation();
            e.preventDefault();
        }
    }, true);

    document.addEventListener("keyup", (e) => {
        if (e.key === "Escape") { escapeConsumed = false; }
    }, true);

    // Also clear the latch if focus leaves the window mid-hold (e.g. Alt+Tab while Escape is held), since the
    // matching keyup may then be delivered elsewhere and never seen here.
    window.addEventListener("blur", () => { escapeConsumed = false; });

    // Keep the pinned bubble aligned with its control while a trigger is active; only drop it once nothing is
    // focused/hovered, so a still-focused tooltip stays put (WCAG 1.4.13 persistent).
    document.addEventListener("scroll", () => {
        if (!tip || tip.hidden) { return; }
        if (activeAnchor() || pointerOverTip) { reposition(); } else { hide(); }
    }, true);

    window.addEventListener("resize", () => {
        if (!tip || tip.hidden) { return; }
        if (activeAnchor() || pointerOverTip) { reposition(); } else { hide(); }
    });
}
