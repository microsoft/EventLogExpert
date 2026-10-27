import { test } from 'node:test';
import assert from 'node:assert/strict';
import { captureInlineAlertFocus, closeModal, rememberInlineAlert, restoreInlineAlertFocus, showModal } from '../../src/EventLogExpert.UI/Modal/ModalChrome.razor.js';
import { attach, detach, normalizeReturnTarget } from '../../src/EventLogExpert.UI/Banner/NotificationCenter.razor.js';

class FakeElement extends EventTarget {
    constructor(documentTarget, name) {
        super();
        this.documentTarget = documentTarget;
        this.name = name;
        this.children = [];
        this.attributes = {};
        this.dataset = {};
        this.disabled = false;
        this.isConnected = true;
        this.open = false;
    }

    append(child) {
        child.parentElement = this;
        this.children.push(child);
    }

    closest(selector) {
        let current = this;
        while (current) {
            if (selector === '[data-notification-trigger]' && current.dataset.notificationTrigger !== undefined) { return current; }
            if (selector === '[data-notification-panel]' && current.dataset.notificationPanel !== undefined) { return current; }
            if (selector === '[data-notification-return-trigger]' && current.dataset.notificationReturnTrigger !== undefined) { return current; }
            if (selector === 'dialog[open]' && current.name === 'dialog' && current.open) { return current; }
            if (selector === 'dialog' && current.name === 'dialog') { return current; }
            if (selector === '[inert]' && current.inert) { return current; }
            current = current.parentElement;
        }
        return null;
    }

    contains(target) { return target === this || this.children.some(child => child.contains(target)); }
    focus() { this.documentTarget.activeElement = this; }
    matches(selector) { return selector === ':disabled' && this.disabled; }
    getClientRects() { return this.rendered === false ? [] : [{}]; }
    hasAttribute(name) { return Object.hasOwn(this.attributes, name); }
    getAttribute(name) { return this.attributes[name] ?? null; }
    querySelector(selector) {
        if (selector.includes('.dialog-body')) { return this.body ?? null; }
        return null;
    }
    querySelectorAll(selector) {
        if (selector === '*') { return this.children.flatMap(child => [child, ...child.querySelectorAll('*')]); }
        return [];
    }
    setAttribute(name, value) { this.attributes[name] = value; }
    showModal() { this.open = true; this.alertButton.focus(); }
    close() { this.open = false; }
}

function createModalFixture() {
    const documentTarget = new EventTarget();
    documentTarget.querySelectorAll = () => [];
    const fallback = new FakeElement(documentTarget, 'workspace-fallback');
    documentTarget.getElementById = id => id === 'notification-trigger' ? documentTarget.notificationTrigger : null;
    const frames = [];
    const windowTarget = new EventTarget();
    globalThis.document = documentTarget;
    globalThis.window = windowTarget;
    globalThis.HTMLElement = FakeElement;
    globalThis.Element = FakeElement;
    globalThis.HTMLDialogElement = FakeElement;
    globalThis.requestAnimationFrame = callback => frames.push(callback);
    globalThis.MutationObserver = class { observe() {} disconnect() {} };
    globalThis.ResizeObserver = class { observe() {} disconnect() {} };
    documentTarget.querySelector = selector => selector === '[data-notification-focus-fallback]' ? fallback : null;
    documentTarget.body = new FakeElement(documentTarget, 'body');
    const dialog = new FakeElement(documentTarget, 'dialog');
    dialog.alertButton = new FakeElement(documentTarget, 'alert-button');
    dialog.append(dialog.alertButton);
    dialog.querySelector = selector => selector === '.inline-alert' ? dialog.inlineAlert ?? null : null;
    return { dialog, documentTarget, fallback, frames, windowTarget };
}

function createNotificationTarget(fixture) {
    const trigger = new FakeElement(fixture.documentTarget, 'trigger');
    trigger.dataset.notificationTrigger = '';
    trigger.id = 'notification-trigger';
    fixture.documentTarget.notificationTrigger = trigger;
    const panel = new FakeElement(fixture.documentTarget, 'panel');
    panel.dataset.notificationPanel = '';
    const card = new FakeElement(fixture.documentTarget, 'card');
    card.dataset.notificationCard = '';
    const cardButton = new FakeElement(fixture.documentTarget, 'button');
    card.append(cardButton);
    panel.append(card);
    panel.querySelector = () => null;
    panel.matches = () => false;
    panel.hidePopover = () => {};
    panel.getBoundingClientRect = () => ({ width: 100, height: 100 });
    trigger.getBoundingClientRect = () => ({ top: 0, bottom: 20, right: 100 });
    attach(trigger, panel);
    return { cardButton, panel, trigger };
}

test('A detached card retains its server-rendered trigger mapping before JS attachment', () => {
    const fixture = createModalFixture();
    const trigger = new FakeElement(fixture.documentTarget, 'trigger');
    trigger.id = 'notification-trigger';
    trigger.dataset.notificationTrigger = '';
    fixture.documentTarget.notificationTrigger = trigger;
    const card = new FakeElement(fixture.documentTarget, 'card');
    card.dataset.notificationReturnTrigger = trigger.id;
    const action = new FakeElement(fixture.documentTarget, 'action');
    card.append(action);
    action.isConnected = false;

    assert.equal(normalizeReturnTarget(action), trigger);
});

test('Modal show normalizes a detached notification card return target to its trigger', () => {
    const fixture = createModalFixture();
    const notification = createNotificationTarget(fixture);
    fixture.documentTarget.activeElement = notification.cardButton;
    notification.cardButton.isConnected = false;

    showModal(fixture.dialog);

    assert.equal(fixture.dialog._returnFocusElement, notification.trigger);
    detach(notification.panel);
});

test('Modal close restores a usable notification trigger on the next frame', () => {
    const fixture = createModalFixture();
    const notification = createNotificationTarget(fixture);
    fixture.documentTarget.activeElement = notification.cardButton;
    showModal(fixture.dialog);

    closeModal(fixture.dialog);
    assert.equal(fixture.documentTarget.activeElement, fixture.dialog.alertButton);
    fixture.frames.shift()();

    assert.equal(fixture.documentTarget.activeElement, notification.trigger);
    detach(notification.panel);
});

test('Modal close focuses the topmost open dialog when the saved trigger is disabled', () => {
    const fixture = createModalFixture();
    const notification = createNotificationTarget(fixture);
    const parentDialog = new FakeElement(fixture.documentTarget, 'parent-dialog');
    parentDialog.body = new FakeElement(fixture.documentTarget, 'dialog-body');
    parentDialog.querySelector = () => parentDialog.body;
    fixture.documentTarget.querySelectorAll = () => [parentDialog];
    fixture.documentTarget.activeElement = notification.cardButton;
    showModal(fixture.dialog);
    notification.trigger.disabled = true;

    closeModal(fixture.dialog);
    fixture.frames.shift()();

    assert.equal(fixture.documentTarget.activeElement, parentDialog.body);
    detach(notification.panel);
});

test('Modal close uses the marked workspace fallback when no saved target is usable', () => {
    const fixture = createModalFixture();
    fixture.documentTarget.activeElement = document.body;
    showModal(fixture.dialog);

    closeModal(fixture.dialog);
    fixture.frames.shift()();

    assert.equal(fixture.documentTarget.activeElement, fixture.fallback);
});

test('Standalone restoration preserves the caller-selected target', () => {
    const fixture = createModalFixture();
    const opener = new FakeElement(document, 'opener');
    opener.focus();
    showModal(fixture.dialog);
    closeModal(fixture.dialog);
    const selectedTarget = new FakeElement(document, 'caller-selected-target');
    selectedTarget.focus();
    fixture.frames.shift()();
    assert.equal(document.activeElement, selectedTarget);
});

test('Inline restoration preserves the caller-selected target', () => {
    const fixture = createModalFixture();
    const opener = new FakeElement(document, 'opener');
    opener.focus();
    showModal(fixture.dialog);
    captureInlineAlertFocus(fixture.dialog);
    fixture.dialog.inlineAlert = new FakeElement(document, 'inline-alert');
    fixture.dialog.append(fixture.dialog.inlineAlert);
    const accept = new FakeElement(document, 'inline-accept');
    fixture.dialog.inlineAlert.append(accept);
    rememberInlineAlert(fixture.dialog);
    accept.focus();
    restoreInlineAlertFocus(fixture.dialog);
    const selectedTarget = new FakeElement(document, 'next-item');
    selectedTarget.focus();
    fixture.frames.shift()();
    assert.equal(document.activeElement, selectedTarget);
});

test('A still-visible standalone modal opener is restored when the closing dialog still owns focus', () => {
    const fixture = createModalFixture();
    const opener = new FakeElement(document, 'opener');
    opener.focus();
    showModal(fixture.dialog);
    closeModal(fixture.dialog);
    assert.equal(document.activeElement, fixture.dialog.alertButton);
    fixture.frames.shift()();
    assert.equal(document.activeElement, opener);
});

test('Inline close restores the saved opener only when the removed alert still owns focus', () => {
    const fixture = createModalFixture();
    const opener = new FakeElement(document, 'opener');
    showModal(fixture.dialog);
    opener.focus();
    captureInlineAlertFocus(fixture.dialog);
    fixture.dialog.inlineAlert = new FakeElement(document, 'inline-alert');
    fixture.dialog.append(fixture.dialog.inlineAlert);
    const cancel = new FakeElement(document, 'inline-cancel');
    fixture.dialog.inlineAlert.append(cancel);
    rememberInlineAlert(fixture.dialog);
    cancel.focus();
    fixture.dialog.inlineAlert.isConnected = false;
    restoreInlineAlertFocus(fixture.dialog);
    fixture.frames.shift()();
    assert.equal(document.activeElement, opener);
});
