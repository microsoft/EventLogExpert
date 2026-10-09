// Unit tests for the focus-restoration guard extracted from wwwroot/Common/focusGuard.js. Run with Node's
// built-in test runner: `node --test tests/JsUnit`.
//
// There is no DOM here, so a minimal HTMLElement/document are stubbed. These tests lock the branch logic the C# side
// cannot exercise (bUnit mocks the JS interop): the pure "is focus elsewhere?" predicate, and that the DOM wrapper
// suppresses the focus exactly when that predicate is true - the regression that would silently reintroduce focus
// stealing.

import { test } from "node:test";
import assert from "node:assert/strict";
import {
    isActiveElementElsewhere,
    focusIfNotElsewhere,
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
    target.focus = (options) => { focusedWith = options; };
    globalThis.document = { activeElement: body, body, documentElement };

    const moved = focusIfNotElsewhere(target, true);

    assert.equal(moved, true);
    assert.deepEqual(focusedWith, { preventScroll: true });
});
