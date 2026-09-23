// Unit tests for the pure decision helpers + the state-machine reducer extracted from
// wwwroot/Common/focusTooltip.js. Run with Node's built-in test runner: `node --test tests/JsUnit`.
//
// The bubble's layout, DOM event wiring, and overflow-gate resolution stay manually verified (there is no DOM
// here); these tests lock the pure branch logic that regressed repeatedly in design review: sub-pixel-tolerant
// clip detection, hovered-over-focused precedence, nested-anchor ascent, cursor placement geometry, and the
// show/hide/jitter timer state machine (the reducer).

import { test } from "node:test";
import assert from "node:assert/strict";
import {
    isClipped,
    anchorSurfaces,
    resolveEffectiveAnchor,
    hoverTransferAnchor,
    activeDescendantTarget,
    cursorPoint,
    hugWidth,
    initialTooltipState,
    reduce,
    showTip,
    hideTip,
    isTipShown,
} from "../../src/EventLogExpert.UI/wwwroot/Common/focusTooltip.js";

// --- isClipped: sub-pixel-tolerant clip detection ---

test("isClipped: not clipped when scroll size equals client size", () => {
    assert.equal(isClipped({ scrollWidth: 100, clientWidth: 100, scrollHeight: 20, clientHeight: 20 }), false);
});

test("isClipped: a single pixel of overflow is tolerated (fractional-scaling rounding), not clipped", () => {
    assert.equal(isClipped({ scrollWidth: 101, clientWidth: 100, scrollHeight: 20, clientHeight: 20 }), false);
});

test("isClipped: two or more pixels of horizontal overflow is clipped", () => {
    assert.equal(isClipped({ scrollWidth: 102, clientWidth: 100, scrollHeight: 20, clientHeight: 20 }), true);
});

test("isClipped: vertical (line-clamp) overflow beyond tolerance is clipped", () => {
    assert.equal(isClipped({ scrollWidth: 100, clientWidth: 100, scrollHeight: 60, clientHeight: 20 }), true);
});

// --- resolveEffectiveAnchor: overflow gate + nested-anchor ascent ---

// Minimal DOM-node mock exposing only the members resolveEffectiveAnchor touches. Every mock node is itself a
// non-empty tooltip anchor, so a [data-tooltip] closest() query resolves to the node it is called on (the
// nearest ancestor).
function anchorNode({ overflow = false, parent = null, dims = null } = {}) {
    return {
        _overflow: overflow,
        _dims: dims,
        parentElement: parent,
        hasAttribute(name) { return name === "data-tooltip-overflow" ? this._overflow : true; },
        closest(selector) { return selector.startsWith("[data-tooltip]") ? this : null; },
    };
}

const clippedByDims = (el) => isClipped(el._dims);

test("resolveEffectiveAnchor: a plain (non-overflow) anchor is returned unchanged", () => {
    const control = anchorNode({ overflow: false });
    assert.equal(resolveEffectiveAnchor(control, clippedByDims), control);
});

test("resolveEffectiveAnchor: a clipped overflow anchor shows itself", () => {
    const chip = anchorNode({
        overflow: true,
        dims: { scrollWidth: 200, clientWidth: 100, scrollHeight: 20, clientHeight: 20 },
    });
    assert.equal(resolveEffectiveAnchor(chip, clippedByDims), chip);
});

test("resolveEffectiveAnchor: an UNclipped overflow leaf inside a control ascends to the control (no shadowing)", () => {
    const button = anchorNode({ overflow: false });
    const innerLabel = anchorNode({
        overflow: true,
        parent: button,
        dims: { scrollWidth: 100, clientWidth: 100, scrollHeight: 20, clientHeight: 20 },
    });
    assert.equal(resolveEffectiveAnchor(innerLabel, clippedByDims), button);
});

test("resolveEffectiveAnchor: an unclipped overflow leaf with no ancestor anchor resolves to null (conceal)", () => {
    const lone = anchorNode({
        overflow: true,
        parent: null,
        dims: { scrollWidth: 100, clientWidth: 100, scrollHeight: 20, clientHeight: 20 },
    });
    assert.equal(resolveEffectiveAnchor(lone, clippedByDims), null);
});

test("resolveEffectiveAnchor: a null leaf resolves to null", () => {
    assert.equal(resolveEffectiveAnchor(null, clippedByDims), null);
});

// --- hoverTransferAnchor: hand the hover to an enclosing control anchor (nested-anchor bug fix) ---

test("hoverTransferAnchor: transfers to an ancestor anchor that contains the hovered leaf", () => {
    const innerLabel = {};
    const enclosingButton = { contains: (node) => node === innerLabel };
    assert.equal(hoverTransferAnchor(innerLabel, enclosingButton), enclosingButton);
});

test("hoverTransferAnchor: returns null for a sibling anchor that does not contain the leaf", () => {
    const innerLabel = {};
    const sibling = { contains: () => false };
    assert.equal(hoverTransferAnchor(innerLabel, sibling), null);
});

test("hoverTransferAnchor: returns null when there is no related anchor", () => {
    assert.equal(hoverTransferAnchor({}, null), null);
});

test("hoverTransferAnchor: returns null when the related anchor is the hovered anchor itself", () => {
    const anchor = { contains: () => true };
    assert.equal(hoverTransferAnchor(anchor, anchor), null);
});

// --- activeDescendantTarget: keyboard combobox option surfacing decision (pure 4-branch table) ---

test("activeDescendantTarget: not armed always conceals (mouse modality owns the list, hover system handles it)", () => {
    assert.equal(activeDescendantTarget({ armed: false, expanded: true, optionAnchorPresent: true, optionSurfaces: true }), "conceal");
    assert.equal(activeDescendantTarget({ armed: false, expanded: false, optionAnchorPresent: false, optionSurfaces: false }), "conceal");
});

test("activeDescendantTarget: armed + list closed surfaces the combobox's own value tooltip", () => {
    assert.equal(activeDescendantTarget({ armed: true, expanded: false, optionAnchorPresent: true, optionSurfaces: true }), "combobox");
    assert.equal(activeDescendantTarget({ armed: true, expanded: false, optionAnchorPresent: false, optionSurfaces: false }), "combobox");
});

test("activeDescendantTarget: armed + open + a surfacing active option surfaces that option", () => {
    assert.equal(activeDescendantTarget({ armed: true, expanded: true, optionAnchorPresent: true, optionSurfaces: true }), "option");
});

test("activeDescendantTarget: armed + open + a non-surfacing or absent active option conceals (no bubble over the list)", () => {
    assert.equal(activeDescendantTarget({ armed: true, expanded: true, optionAnchorPresent: true, optionSurfaces: false }), "conceal");
    assert.equal(activeDescendantTarget({ armed: true, expanded: true, optionAnchorPresent: false, optionSurfaces: false }), "conceal");
});

// --- anchorSurfaces: whether an anchor would show its tooltip right now (shared by the active-descendant resolver) ---

test("anchorSurfaces: a non-overflow anchor with a data-tooltip always surfaces", () => {
    assert.equal(anchorSurfaces({ hasAttribute: () => false, getAttribute: () => "Label" }), true);
});

test("anchorSurfaces: a non-overflow anchor with no data-tooltip does not surface", () => {
    assert.equal(anchorSurfaces({ hasAttribute: () => false, getAttribute: () => null }), false);
});

test("anchorSurfaces: a null/undefined anchor does not surface (exported helper stays null-safe like isOverflowAnchor)", () => {
    assert.equal(anchorSurfaces(null), false);
    assert.equal(anchorSurfaces(undefined), false);
});

test("anchorSurfaces: an overflow anchor surfaces only when its own text is clipped", () => {
    const overflow = (scrollWidth, clientWidth) => ({
        hasAttribute: (name) => name === "data-tooltip-overflow",
        getAttribute: () => "Label",
        scrollWidth, clientWidth, scrollHeight: 0, clientHeight: 0,
    });
    assert.equal(anchorSurfaces(overflow(100, 50)), true);
    assert.equal(anchorSurfaces(overflow(50, 50)), false);
});

// A ChildContent option (e.g. a filter-set row) exposes several overflow spans; the resolver picks the first
// that surfaces, so the keyboard matches a mouse even when only a later (not the first) span is clipped.
const overflowSpan = (scrollWidth, clientWidth) => ({
    hasAttribute: (name) => name === "data-tooltip-overflow",
    getAttribute: () => "Filter set",
    scrollWidth, clientWidth, scrollHeight: 0, clientHeight: 0,
});

test("anchorSurfaces via find: a multi-anchor option resolves the clipped span even when it is not first", () => {
    const name = overflowSpan(50, 50);   // not clipped
    const meta = overflowSpan(100, 50);  // clipped
    assert.equal([name, meta].find(anchorSurfaces), meta);
});

test("anchorSurfaces via find: no surfacing span returns undefined (resolver falls back to the option itself)", () => {
    assert.equal([overflowSpan(50, 50), overflowSpan(50, 50)].find(anchorSurfaces), undefined);
});

// --- cursorPoint: placement geometry + pointer exclusion ---

const VIEWPORT = { w: 1000, h: 800 };
// Offsets are intentionally distinct from the production CURSOR_OFFSET_X/Y (10/15): passing
// arbitrary offsets proves cursorPoint derives placement from its parameters rather than
// reading the module-level constants (a constant-leak regression would be masked if they matched).
const OPTS = { offsetX: 14, offsetY: 18, margin: 4 };

test("cursorPoint: default places the bubble down-right of the cursor, excluding the pointer", () => {
    const p = cursorPoint(100, 100, 200, 60, VIEWPORT.w, VIEWPORT.h, OPTS);
    assert.equal(p.left, 114);
    assert.equal(p.top, 118);
    assert.ok(p.left > 100 && p.top > 100, "pointer is above-left of the box");
});

test("cursorPoint: overflowing the right edge flips left of the cursor (right edge < pointer)", () => {
    const p = cursorPoint(950, 100, 200, 60, VIEWPORT.w, VIEWPORT.h, OPTS);
    assert.equal(p.left, 950 - 14 - 200);
    assert.ok(p.left + 200 < 950, "box is entirely to the left of the pointer");
});

test("cursorPoint: overflowing the bottom edge flips above the cursor (bottom edge < pointer)", () => {
    const p = cursorPoint(100, 780, 200, 60, VIEWPORT.w, VIEWPORT.h, OPTS);
    assert.equal(p.top, 780 - 18 - 60);
    assert.ok(p.top + 60 < 780, "box is entirely above the pointer");
});

test("cursorPoint: always clamps within the viewport margins", () => {
    const p = cursorPoint(5, 5, 200, 60, VIEWPORT.w, VIEWPORT.h, OPTS);
    assert.ok(p.left >= 4 && p.left + 200 <= VIEWPORT.w - 4);
    assert.ok(p.top >= 4 && p.top + 60 <= VIEWPORT.h - 4);
});

test("cursorPoint: a box taller than the viewport clamps its top to the margin (top of text stays on screen)", () => {
    const p = cursorPoint(100, 400, 200, 900, VIEWPORT.w, VIEWPORT.h, OPTS);
    assert.equal(p.top, 4);
});

// --- reduce: show/hide/jitter timer state machine ---

// Drive a fresh hover to maturity (adapter feedback simulated: a real render reports back renderResolved).
function matureHover(state, anchor, { visible = true } = {}) {
    let r = reduce(state, { type: "hoverEnter", anchor });
    const scheduled = r.effects.find((e) => e.type === "scheduleShow");
    r = reduce(r.state, { type: "showTimerFire", gen: scheduled.gen, stillOver: true });
    r = reduce(r.state, { type: "renderResolved", anchor, visible });
    return r.state;
}

function types(effects) { return effects.map((e) => e.type); }

test("reduce: adjacent sweep - leaving a matured A then entering B keeps B's pending (the cross-control race)", () => {
    const A = { id: "A" };
    const B = { id: "B" };
    let s = matureHover(initialTooltipState(), A);
    assert.equal(s.hoveredAnchor, A);
    assert.equal(s.visible, true);

    let r = reduce(s, { type: "hoverLeave", anchor: A, transferTarget: null });
    assert.equal(r.state.hoveredAnchor, null);
    assert.ok(types(r.effects).includes("scheduleHide"), "routine hover-hide uses the conceal path");
    assert.ok(!types(r.effects).includes("cancelShowTimer"), "never cancels a show while hiding");

    r = reduce(r.state, { type: "hoverEnter", anchor: B });
    assert.equal(r.state.pendingAnchor, B);
    const scheduled = r.effects.find((e) => e.type === "scheduleShow");

    const afterHide = reduce(r.state, { type: "hideTimerFire" });
    assert.equal(afterHide.state.pendingAnchor, B, "A's conceal must not cancel B's pending show");

    const promoted = reduce(afterHide.state, { type: "showTimerFire", gen: scheduled.gen, stillOver: true });
    assert.equal(promoted.state.hoveredAnchor, B);
});

test("reduce: jitter-bridge - re-entering a still-visible cursor bubble restores it without a re-delay", () => {
    const A = { id: "A" };
    let s = matureHover(initialTooltipState(), A);
    let r = reduce(s, { type: "hoverLeave", anchor: A, transferTarget: null });
    assert.equal(r.state.hideScheduled, true);
    assert.equal(r.state.displayLeaf, A);
    assert.equal(r.state.visible, true);

    r = reduce(r.state, { type: "hoverEnter", anchor: A });
    assert.equal(r.state.hoveredAnchor, A, "bridged back to mature without re-pending");
    assert.equal(r.state.pendingAnchor, null);
    assert.ok(types(r.effects).includes("cancelHideTimer"));
    assert.ok(!types(r.effects).includes("scheduleShow"), "no fresh 500ms delay");
});

test("reduce: jitter-on-concealed - a concealed leaf (visible=false) does NOT bridge; it re-pends", () => {
    const L = { id: "L" };
    // Mature the hover but the render resolved to nothing (untruncated overflow leaf).
    let s = matureHover(initialTooltipState(), L, { visible: false });
    assert.equal(s.visible, false);
    let r = reduce(s, { type: "hoverLeave", anchor: L, transferTarget: null });
    r = reduce(r.state, { type: "hoverEnter", anchor: L });
    assert.equal(r.state.pendingAnchor, L, "concealed leaf re-pends so it can show if it became clipped");
    assert.ok(types(r.effects).includes("scheduleShow"));
});

test("reduce: focus-grace - entering a control whose focus bubble is hiding does NOT bypass the show delay", () => {
    const A = { id: "A" };
    let r = reduce(initialTooltipState(), { type: "focusIn", anchor: A });
    r = reduce(r.state, { type: "renderResolved", anchor: A, visible: true });
    assert.equal(r.state.displayedMode, "element");
    r = reduce(r.state, { type: "focusOut", anchor: A });
    assert.equal(r.state.hideScheduled, true);

    r = reduce(r.state, { type: "hoverEnter", anchor: A });
    assert.equal(r.state.pendingAnchor, A, "element-mode grace bubble is not a cursor bubble, so no bridge");
    assert.ok(types(r.effects).includes("scheduleShow"));
});

test("reduce: attrChange re-renders in the stored display mode, not one derived from the (now-cleared) hover", () => {
    const A = { id: "A" };
    let s = matureHover(initialTooltipState(), A);
    let r = reduce(s, { type: "hoverLeave", anchor: A, transferTarget: null }); // grace: hovered cleared, displayLeaf=A, mode=cursor
    r = reduce(r.state, { type: "attrChange" });
    const render = r.effects.find((e) => e.type === "render");
    assert.equal(render.mode, "cursor", "still a cursor bubble during the hide grace");
});

test("reduce: pending nested transfer - leaving a pending inner leaf for its ancestor re-pends the ancestor", () => {
    const inner = { id: "inner" };
    const outer = { id: "outer" };
    let r = reduce(initialTooltipState(), { type: "hoverEnter", anchor: inner });
    assert.equal(r.state.pendingAnchor, inner);
    r = reduce(r.state, { type: "hoverLeave", anchor: inner, transferTarget: outer });
    assert.equal(r.state.pendingAnchor, outer);
    assert.ok(types(r.effects).includes("scheduleShow"));
});

// --- Adapter visibility helpers: showTip / hideTip / isTipShown (popover path + fallback) ---
//
// These three helpers own the ONLY DOM writes that flip tooltip visibility. On the popover path they call
// showPopover()/hidePopover(), which throw InvalidStateError on already-open / not-open / disconnected. There
// is no DOM here (and jsdom implements neither showPopover nor :popover-open), so a hand-rolled mock models
// the open-state and the throw contract, locking the guard logic a browser would otherwise be the only judge
// of. supportsPopover is passed in, matching the production adapter that computes it once and forwards it.

function mockPopover({ connected = true } = {}) {
    let open = false;
    const calls = [];
    return {
        isConnected: connected,
        hidden: true,
        calls,
        get open() { return open; },
        showPopover() {
            calls.push("show");
            if (!this.isConnected) { throw new Error("InvalidStateError: not connected"); }
            if (open) { throw new Error("InvalidStateError: already open"); }
            open = true;
        },
        hidePopover() {
            calls.push("hide");
            if (!open) { throw new Error("InvalidStateError: not open"); }
            open = false;
        },
        matches(selector) { return selector === ":popover-open" ? open : false; },
    };
}

test("showTip (popover): opens a closed popover", () => {
    const el = mockPopover();
    showTip(el, true);
    assert.equal(el.open, true);
    assert.equal(isTipShown(el, true), true);
});

test("showTip (popover): re-promotes an already-open popover via hide+show (top-layer reorder)", () => {
    const el = mockPopover();
    showTip(el, true);
    assert.deepEqual(el.calls, ["show"]);
    // Second show on an open popover MUST hide then show to move it back to the top of the top layer (above a
    // dialog opened after it). A plain `if (:popover-open) return;` would leave calls at ["show"] and fail here.
    showTip(el, true);
    assert.deepEqual(el.calls, ["show", "hide", "show"]);
    assert.equal(el.open, true);
});

test("showTip (popover): no-op on a disconnected element (showPopover would throw)", () => {
    const el = mockPopover({ connected: false });
    assert.doesNotThrow(() => showTip(el, true));
    assert.equal(el.open, false);
});

test("hideTip (popover): closes an open popover and is a no-op when already closed", () => {
    const el = mockPopover();
    showTip(el, true);
    hideTip(el, true);
    assert.equal(el.open, false);
    assert.doesNotThrow(() => hideTip(el, true)); // would throw 'not open' without the :popover-open guard
    assert.equal(el.open, false);
});

test("isTipShown (popover): reflects the popover open state", () => {
    const el = mockPopover();
    assert.equal(isTipShown(el, true), false);
    showTip(el, true);
    assert.equal(isTipShown(el, true), true);
});

test("showTip/hideTip (fallback): toggle the hidden attribute when popover is unsupported", () => {
    const el = { hidden: true };
    showTip(el, false);
    assert.equal(el.hidden, false);
    assert.equal(isTipShown(el, false), true);
    hideTip(el, false);
    assert.equal(el.hidden, true);
    assert.equal(isTipShown(el, false), false);
});

test("showTip/hideTip/isTipShown: a null element is a safe no-op", () => {
    assert.doesNotThrow(() => showTip(null, true));
    assert.doesNotThrow(() => hideTip(null, true));
    assert.equal(isTipShown(null, true), false);
    assert.equal(isTipShown(null, false), false);
});

// --- hugWidth: pure width arithmetic for the tooltip hug (widest line + border-box add-back) ---

test("hugWidth: picks the widest line and adds horizontal padding+border under border-box", () => {
    const rects = [{ width: 100.2 }, { width: 140.6 }, { width: 90 }];
    // ceil(140.6)=141, + padding(6+6) + border(1+1)=14 => 155
    assert.equal(hugWidth(rects, "border-box", 6, 6, 1, 1), 155);
});

test("hugWidth: content-box adds no chrome (width already is the content box)", () => {
    assert.equal(hugWidth([{ width: 140.6 }], "content-box", 6, 6, 1, 1), 141);
});

test("hugWidth: returns null when there is no positive-width line to hug", () => {
    assert.strictEqual(hugWidth([], "border-box", 6, 6, 1, 1), null);
    assert.strictEqual(hugWidth([{ width: 0 }], "border-box", 6, 6, 1, 1), null);
});

test("reduce: transfer-to-mature - a pending inner leaf returning to its already-shown ancestor retains the ancestor", () => {
    const outer = { id: "outer" };
    const inner = { id: "inner" };
    let s = matureHover(initialTooltipState(), outer);
    // Move onto an inner leaf: a new pending starts while outer stays shown.
    let r = reduce(s, { type: "hoverEnter", anchor: inner });
    assert.equal(r.state.pendingAnchor, inner);
    assert.equal(r.state.hoveredAnchor, outer);
    // Return to outer: transferTarget === hoveredAnchor -> cancel pending, keep outer (INV4).
    r = reduce(r.state, { type: "hoverLeave", anchor: inner, transferTarget: outer });
    assert.equal(r.state.pendingAnchor, null);
    assert.equal(r.state.hoveredAnchor, outer);
    assert.notEqual(r.state.pendingAnchor, r.state.hoveredAnchor, "INV4: pending never equals hovered");
    // Leaving outer now reaches the hovered branch and hides.
    r = reduce(r.state, { type: "hoverLeave", anchor: outer, transferTarget: null });
    assert.equal(r.state.hoveredAnchor, null);
    assert.ok(types(r.effects).includes("scheduleHide"));
});

test("reduce: hover->focus fallback renders the focused control immediately (no linger) on leaving the hover", () => {
    const focusCtl = { id: "focus" };
    const hoverCtl = { id: "hover" };
    let r = reduce(initialTooltipState(), { type: "focusIn", anchor: focusCtl });
    r = reduce(r.state, { type: "renderResolved", anchor: focusCtl, visible: true });
    let s = matureHover(r.state, hoverCtl);
    assert.equal(s.hoveredAnchor, hoverCtl);

    r = reduce(s, { type: "hoverLeave", anchor: hoverCtl, transferTarget: null });
    assert.equal(r.state.displayLeaf, focusCtl);
    assert.equal(r.state.displayedMode, "element");
    const render = r.effects.find((e) => e.type === "render");
    assert.equal(render.anchor, focusCtl);
    assert.ok(!types(r.effects).includes("scheduleHide"), "fallback is a reposition, not a hide");
});

test("reduce: scroll with focus + hover clears the hover, renders the focus bubble, and cancels the pending", () => {
    const focusCtl = { id: "focus" };
    const hoverCtl = { id: "hover" };
    let r = reduce(initialTooltipState(), { type: "focusIn", anchor: focusCtl });
    r = reduce(r.state, { type: "renderResolved", anchor: focusCtl, visible: true });
    let s = matureHover(r.state, hoverCtl);

    r = reduce(s, { type: "scroll" });
    assert.equal(r.state.hoveredAnchor, null);
    assert.equal(r.state.pendingAnchor, null);
    assert.equal(r.state.displayLeaf, focusCtl);
    assert.equal(r.state.displayedMode, "element");
});

test("reduce: a stale showTimerFire (superseded generation) does not promote", () => {
    const A = { id: "A" };
    let r = reduce(initialTooltipState(), { type: "hoverEnter", anchor: A });
    const staleGen = r.effects.find((e) => e.type === "scheduleShow").gen;
    // Something bumps the generation (e.g. a pointerdown) before the timer fires.
    r = reduce(r.state, { type: "pointerDown" });
    const after = reduce(r.state, { type: "showTimerFire", gen: staleGen, stillOver: true });
    assert.equal(after.state.hoveredAnchor, null, "stale timer is ignored");
});

test("reduce: promotion liveness - showTimerFire with stillOver=false drops the pending without showing", () => {
    const A = { id: "A" };
    let r = reduce(initialTooltipState(), { type: "hoverEnter", anchor: A });
    const gen = r.effects.find((e) => e.type === "scheduleShow").gen;
    const after = reduce(r.state, { type: "showTimerFire", gen, stillOver: false });
    assert.equal(after.state.hoveredAnchor, null);
    assert.equal(after.state.pendingAnchor, null);
    assert.ok(!types(after.effects).includes("render"));
});

test("reduce: Escape and pointerDown both cancel a pending show", () => {
    const A = { id: "A" };
    let escaped = reduce(reduce(initialTooltipState(), { type: "hoverEnter", anchor: A }).state, { type: "escape" });
    assert.equal(escaped.state.pendingAnchor, null);
    assert.ok(types(escaped.effects).includes("cancelShowTimer"));

    let clicked = reduce(reduce(initialTooltipState(), { type: "hoverEnter", anchor: A }).state, { type: "pointerDown" });
    assert.equal(clicked.state.pendingAnchor, null);
    assert.ok(types(clicked.effects).includes("cancelShowTimer"));
});

test("reduce: a detached focused leaf (focusOut then scroll, as the adapter routes it) conceals instead of stranding", () => {
    const A = { id: "A" };
    let s = reduce(initialTooltipState(), { type: "focusIn", anchor: A }).state;
    s = reduce(s, { type: "renderResolved", anchor: A, visible: true }).state;
    s = reduce(s, { type: "focusOut", anchor: A }).state;
    const afterScroll = reduce(s, { type: "scroll" });
    assert.equal(afterScroll.state.focusedAnchor, null);
    assert.equal(afterScroll.state.displayLeaf, null, "display leaf cleared, not repositioned");
    assert.ok(types(afterScroll.effects).includes("conceal"));
});

test("reduce: scroll clears a matured hover so a later focusIn on another control still renders (no stale-hover suppression)", () => {
    const A = { id: "A" };
    const B = { id: "B" };
    const afterScroll = reduce(matureHover(initialTooltipState(), A), { type: "scroll" });
    assert.equal(afterScroll.state.hoveredAnchor, null, "scroll clears the matured hover");
    const afterFocus = reduce(afterScroll.state, { type: "focusIn", anchor: B });
    assert.equal(afterFocus.state.displayLeaf, B, "focusIn on B renders because no stale hover blocks it");
    assert.ok(types(afterFocus.effects).includes("render"));
});

test("reduce: pointerDown dismisses a matured hover (native click-to-dismiss; no bubble stranded on a removed trigger)", () => {
    const A = { id: "A" };
    const clicked = reduce(matureHover(initialTooltipState(), A), { type: "pointerDown" });
    assert.equal(clicked.state.hoveredAnchor, null, "the matured hover is cleared on click");
    assert.equal(clicked.state.displayLeaf, null, "the display leaf is cleared so a detached trigger cannot strand the bubble");
    assert.equal(clicked.state.displayedMode, null, "displayedMode is cleared to match the concealed DOM (reducer invariant)");
    assert.equal(clicked.state.visible, false, "visible mirrors the concealed bubble (reducer invariant)");
    assert.ok(types(clicked.effects).includes("conceal"), "the visible bubble is concealed");
    assert.ok(types(clicked.effects).includes("cancelHideTimer"));
});

test("reduce: renderResolved only updates visibility for the current request (stale reports are ignored)", () => {
    const A = { id: "A" };
    const B = { id: "B" };
    let s = matureHover(initialTooltipState(), A); // displayLeaf=A, visible=true
    // A stale report for a superseded leaf must not flip visibility.
    let r = reduce(s, { type: "renderResolved", anchor: B, visible: false });
    assert.equal(r.state.visible, true);
    r = reduce(s, { type: "renderResolved", anchor: A, visible: false });
    assert.equal(r.state.visible, false);
});

test("reduce: scrolling during the hover-intent window cancels the pending show (no late tooltip)", () => {
    const A = { id: "A" };
    let r = reduce(initialTooltipState(), { type: "hoverEnter", anchor: A });
    const gen = r.effects.find((e) => e.type === "scheduleShow").gen;
    r = reduce(r.state, { type: "scroll" });
    assert.equal(r.state.pendingAnchor, null, "scroll clears the pending show");
    assert.ok(types(r.effects).includes("cancelShowTimer"));
    // A now-stale show timer that still fires must not promote.
    const after = reduce(r.state, { type: "showTimerFire", gen, stillOver: true });
    assert.equal(after.state.hoveredAnchor, null);
});

test("reduce: INV1v - visibility is false after a conceal", () => {
    const A = { id: "A" };
    let s = matureHover(initialTooltipState(), A);
    // Leave with no focus, then let the hide timer conceal it.
    let r = reduce(s, { type: "hoverLeave", anchor: A, transferTarget: null });
    r = reduce(r.state, { type: "hideTimerFire" });
    assert.ok(types(r.effects).includes("conceal"));
    assert.equal(r.state.displayLeaf, null);
    assert.equal(r.state.visible, false);
});
