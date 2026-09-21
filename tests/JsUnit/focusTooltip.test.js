// Unit tests for the pure decision helpers extracted from wwwroot/Common/focusTooltip.js.
// Run with Node's built-in test runner (zero dependencies): `node --test tests/JsUnit`.
//
// The bubble's layout/positioning and DOM event wiring stay manually verified (there is no DOM here); these
// tests lock the branch logic that regressed in design review: sub-pixel-tolerant clip detection, the
// hovered-over-focused precedence, and the nested-anchor ascent that stops an unclipped inner overflow span
// from shadowing the tooltip of the control it sits inside (and the conceal case, resolve -> null).

import { test } from "node:test";
import assert from "node:assert/strict";
import {
    isClipped,
    selectAnchor,
    resolveEffectiveAnchor,
    hoverTransferAnchor,
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

// --- selectAnchor: a hovered control beats a focused one ---

test("selectAnchor: a hovered control takes precedence over a focused one", () => {
    const focused = { id: "focused" };
    const hovered = { id: "hovered" };
    assert.equal(selectAnchor(focused, hovered), hovered);
});

test("selectAnchor: falls back to the focused control when nothing is hovered", () => {
    const focused = { id: "focused" };
    assert.equal(selectAnchor(focused, null), focused);
});

test("selectAnchor: null when neither is set", () => {
    assert.equal(selectAnchor(null, null), null);
});

// --- resolveEffectiveAnchor: overflow gate + nested-anchor ascent ---

// Minimal DOM-node mock exposing only the members resolveEffectiveAnchor touches. Every mock node is itself a
// tooltip anchor, so closest("[data-tooltip]") resolves to the node it is called on (the nearest ancestor).
function anchorNode({ overflow = false, parent = null, dims = null } = {}) {
    return {
        _overflow: overflow,
        _dims: dims,
        parentElement: parent,
        hasAttribute(name) { return name === "data-tooltip-overflow" ? this._overflow : true; },
        closest(selector) { return selector === "[data-tooltip]" ? this : null; },
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
