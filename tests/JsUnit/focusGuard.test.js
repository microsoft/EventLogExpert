// Unit tests for wwwroot/Common/focusGuard.js (`node --test tests/JsUnit`). No DOM here, so HTMLElement/document are
// stubbed; these lock the branch logic bUnit cannot exercise (it mocks the JS interop).

import { test } from "node:test";
import assert from "node:assert/strict";
import {
    isActiveElementElsewhere,
    focusIfNotElsewhere,
    focusSelector,
    focusSelectorIfNotElsewhere,
} from "../../src/EventLogExpert.UI/wwwroot/Common/focusGuard.js";

// focusGuard.js uses `instanceof HTMLElement`; Node has no DOM, so define a minimal class and make every stub node
// an instance of it. The module reads HTMLElement from the global scope at call time.
class HTMLElement {}
globalThis.HTMLElement = HTMLElement;

const body = new HTMLElement();
const documentElement = new HTMLElement();

// --- isActiveElementElsewhere: the pure "focus rests on a real control elsewhere" decision ---

test("isActiveElementElsewhere: null active element is not elsewhere (focus orphaned, restore proceeds)", () => {
    assert.equal(isActiveElementElsewhere(null, body, documentElement), false);
});

test("isActiveElementElsewhere: the document body is not elsewhere", () => {
    assert.equal(isActiveElementElsewhere(body, body, documentElement), false);
});

test("isActiveElementElsewhere: the document element is not elsewhere", () => {
    assert.equal(isActiveElementElsewhere(documentElement, body, documentElement), false);
});

test("isActiveElementElsewhere: a real control somewhere else is elsewhere (restore suppressed)", () => {
    const control = new HTMLElement();

    assert.equal(isActiveElementElsewhere(control, body, documentElement), true);
});

test("isActiveElementElsewhere: a non-HTMLElement node (e.g. SVG or text) is not elsewhere", () => {
    const svgNode = { nodeName: "svg" };

    assert.equal(isActiveElementElsewhere(svgNode, body, documentElement), false);
});

// --- focusIfNotElsewhere: the DOM wrapper that checks and focuses in one call ---

test("focusIfNotElsewhere: does not focus the target when focus rests on a control elsewhere", () => {
    const elsewhere = new HTMLElement();
    const target = new HTMLElement();
    let focused = false;
    target.focus = () => { focused = true; };
    globalThis.document = { activeElement: elsewhere, body, documentElement };

    const moved = focusIfNotElsewhere(target, true);

    assert.equal(moved, false);
    assert.equal(focused, false);
});

test("focusIfNotElsewhere: focuses the target (honoring preventScroll) when focus is orphaned to body", () => {
    const target = new HTMLElement();
    let focusedWith;
    const activeDocument = { activeElement: body, body, documentElement };
    target.focus = (options) => { focusedWith = options; activeDocument.activeElement = target; };
    globalThis.document = activeDocument;

    const moved = focusIfNotElsewhere(target, true);

    assert.equal(moved, true);
    assert.deepEqual(focusedWith, { preventScroll: true });
});

test("focusIfNotElsewhere: reports false when focus() silently no-ops (hidden/non-focusable target)", () => {
    const target = new HTMLElement();
    // focus() leaves document.activeElement on body, modelling a non-focusable or detached target.
    target.focus = () => { };
    globalThis.document = { activeElement: body, body, documentElement };

    const moved = focusIfNotElsewhere(target, true);

    assert.equal(moved, false);
});

// --- focusSelector: unguarded, selector-addressed focus for a sibling landmark (the filters pane) ---

test("focusSelector: focuses the matched element and reports it landed", () => {
    const target = new HTMLElement();
    let focusedWith;
    const activeDocument = {
        activeElement: body,
        body,
        documentElement,
        querySelector: (selector) => selector === "[data-pane='filters']" ? target : null,
    };
    target.focus = (options) => { focusedWith = options; activeDocument.activeElement = target; };
    globalThis.document = activeDocument;

    const moved = focusSelector("[data-pane='filters']", true);

    assert.equal(moved, true);
    assert.deepEqual(focusedWith, { preventScroll: true });
});

test("focusSelector: returns false without throwing when the selector matches nothing (pane unmounted)", () => {
    globalThis.document = { activeElement: body, body, documentElement, querySelector: () => null };

    const moved = focusSelector("[data-pane='filters']", true);

    assert.equal(moved, false);
});

// --- focusSelectorIfNotElsewhere: the guarded selector focus for the deferred group-clear restore ---

test("focusSelectorIfNotElsewhere: declines when focus already rests on a control elsewhere", () => {
    const target = new HTMLElement();
    const elsewhere = new HTMLElement();
    let focused = false;
    target.focus = () => { focused = true; };
    globalThis.document = { activeElement: elsewhere, body, documentElement, querySelector: () => target };

    const moved = focusSelectorIfNotElsewhere("[data-pane='filters']", true);

    assert.equal(moved, false);
    assert.equal(focused, false);
});

test("focusSelectorIfNotElsewhere: focuses the matched element when focus is orphaned to body", () => {
    const target = new HTMLElement();
    const activeDocument = { activeElement: body, body, documentElement, querySelector: () => target };
    target.focus = () => { activeDocument.activeElement = target; };
    globalThis.document = activeDocument;

    const moved = focusSelectorIfNotElsewhere("[data-pane='filters']", true);

    assert.equal(moved, true);
});

test("focusSelectorIfNotElsewhere: returns false when the selector matches nothing", () => {
    globalThis.document = { activeElement: body, body, documentElement, querySelector: () => null };

    const moved = focusSelectorIfNotElsewhere("[data-pane='filters']", true);

    assert.equal(moved, false);
});
