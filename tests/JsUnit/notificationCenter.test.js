import { test } from 'node:test';
import assert from 'node:assert/strict';
import { readFile } from 'node:fs/promises';
import { captureInlineAlertFocus, closeModal, rememberInlineAlert, restoreInlineAlertFocus } from '../../src/EventLogExpert.UI/Modal/ModalChrome.razor.js';
import {
    attach,
    completeClearFocus,
    detach,
    normalizeReturnTarget,
    positionToast,
    prepareModalAction,
    slotChanged,
    retireFocusBeforeHide,
    setUnavailable
} from '../../src/EventLogExpert.UI/Banner/NotificationCenter.razor.js';

class FakeDocument extends EventTarget {
    listeners = [];

    constructor() {
        super();
        this.activeElement = new FakeElement('body', this);
        this.documentElement = new FakeElement('html', this);
        this.documentElement.direction = 'ltr';
        this.body = this.activeElement;
        this.filterPane = new FakeElement('filter-pane', this);
        this.filterPane.rect = { top: 0, bottom: 100, left: 0, right: 800, width: 800, height: 100 };
        this.statusBar = new FakeElement('status-bar', this);
        this.statusBar.rect = { top: 570, bottom: 600, left: 0, right: 800, width: 800, height: 30 };
        this.workspace = new FakeElement('workspace', this);
        this.workspace.rect = { top: 20, bottom: 570, left: 0, right: 800, width: 800, height: 550 };
        this.statsDrawer = new FakeElement('stats-drawer', this);
        this.statsDrawer.rect = { top: 570, bottom: 570, left: 0, right: 800, width: 800, height: 0 };
        this.elementsById = new Map();
    }

    addEventListener(type, callback, options) {
        this.listeners.push({ callback, options, type });
        super.addEventListener(type, callback, options);
    }

    querySelectorAll(selector) { return selector === 'dialog:modal' ? this.modalDialogs ?? [] : []; }

    getElementById(id) { return this.elementsById.get(id) ?? null; }

    querySelector(selector) {
        if (selector === '.filter-pane') { return this.filterPane; }
        if (selector === 'main.page-content') { return this.workspace; }
        if (selector === '.stats-drawer') { return this.statsDrawer; }
        if (selector === 'footer[data-pane="status"]') { return this.statusBar; }
        if (selector === '[data-notification-focus-fallback]') { return this.workspace; }
        return null;
    }

    invokeListeners(type, event) {
        for (const listener of this.listeners.filter(listener => listener.type === type)) {
            if (!listener.options?.signal?.aborted) { listener.callback(event); }
        }
    }
}

class FakeElement extends EventTarget {
    constructor(name, documentTarget) {
        super();
        this.name = name;
        this.documentTarget = documentTarget;
        this.attributes = {};
        this.children = [];
        this.dataset = {};
        this.disabled = false;
        this.inert = false;
        this.isConnected = true;
        this.open = false;
        this.parentElement = null;
        this.geometryWrites = [];
        this.geometryReads = 0;
        this.style = new Proxy({}, {
            set: (styles, property, value) => {
                this.geometryWrites.push([property, value]);
                styles[property] = value;
                return true;
            }
        });
        this.listeners = [];
        this.rect = null;
        this.direction = 'ltr';
    }

    set tabIndex(value) { this.setAttribute('tabindex', String(value)); }
    get tabIndex() { return Number(this.getAttribute('tabindex') ?? -1); }

    addEventListener(type, callback, options) {
        this.listeners.push({ callback, options, type });
        super.addEventListener(type, callback, options);
    }

    append(child) {
        child.parentElement = this;
        this.children.push(child);
    }

    closest(selector) {
        let current = this;
        while (current) {
            if (selector === '[data-notification-trigger]' && current.dataset.notificationTrigger !== undefined) {
                return current;
            }
            if (selector === '[data-notification-panel]' && current.dataset.notificationPanel !== undefined) {
                return current;
            }
            if (selector === '[data-notification-card]' && current.dataset.notificationCard !== undefined) {
                return current;
            }
            if (selector === '[data-notification-return-trigger]'
                && current.dataset.notificationReturnTrigger !== undefined) {
                return current;
            }
            if (selector === '[data-notification-card] .banner-dismiss'
                && current.dataset.bannerDismiss !== undefined) {
                return current;
            }
            if (selector === '[data-notification-clear-all]' && current.dataset.notificationClearAll !== undefined) { return current; }
            if (selector === '[inert]' && current.inert) { return current; }
            if (selector === 'header' && current.name === 'header') { return current; }
            if (selector === 'dialog' && current.name === 'dialog') { return current; }
            if (selector === 'dialog[open]' && current.name === 'dialog' && current.open) { return current; }
            if (selector.startsWith('button') && current.name === 'button' && !current.disabled) { return current; }
            current = current.parentElement;
        }
        return null;
    }

    contains(target) {
        return target === this || this.children.some(child => child.contains(target));
    }

    focus() {
        this.focusCount = (this.focusCount ?? 0) + 1;
        this.documentTarget.activeElement = this;
        let current = this;
        while (current) {
            for (const listener of current.listeners.filter(listener => listener.type === 'focusin')) {
                if (!listener.options?.signal?.aborted) { listener.callback({ target: this }); }
            }
            current = current.parentElement;
        }
        this.documentTarget.invokeListeners('focusin', { target: this });
    }

    getClientRects() { return this.rendered === false ? [] : [this.getBoundingClientRect()]; }

    getBoundingClientRect() {
        this.geometryReads++;
        if (this.rect) { return this.rect; }
        if (this.name === 'trigger') {
            return { top: 570, bottom: 594, right: 790, left: 690, width: 100, height: 24 };
        }
        if (this.name === 'toast') {
            return { top: 300, bottom: 400, right: 790, left: 500, width: 290, height: 100 };
        }
        return { top: 0, bottom: 10, right: 10, left: 0, width: 10, height: 10 };
    }

    getAttribute(name) { return this.attributes[name] ?? null; }
    hasAttribute(name) { return Object.hasOwn(this.attributes, name); }

    matches(selector) {
        if (selector === ':popover-open') { return this.open; }
        if (selector === ':disabled') { return this.disabled; }
        if (selector === '.notification-card-progress') { return this.isProgress === true; }
        if (selector === 'dialog[open]') { return this.name === 'dialog' && this.open; }
        return false;
    }

    querySelector(selector) {
        if (selector === '.filter-pane' && this.name === 'workspace') { return this.documentTarget.filterPane; }
        if (selector === '[data-notification-trigger]') {
            return this.children.find(child => child.dataset.notificationTrigger !== undefined) ?? null;
        }
        if (selector === 'h2[tabindex="-1"]') { return this.heading ?? null; }
        if (selector === '.dialog-body') { return this.dialogBody ?? null; }
        if (selector === 'button:not(:disabled), a[href], [tabindex]:not([tabindex="-1"])') {
            return this.children.find(child => child.name === 'button' && !child.disabled) ?? null;
        }
        return null;
    }

    querySelectorAll(selector) {
        const descendants = this.children.flatMap(child => [child, ...child.querySelectorAll('*')]);
        if (selector === '*') { return descendants; }
        if (selector === '[data-notification-card]') {
            return descendants.filter(child => child.dataset.notificationCard !== undefined);
        }
        return [];
    }

    close() { this.open = false; }

    setAttribute(name, value) { this.attributes[name] = value; }
    removeAttribute(name) { delete this.attributes[name]; }

    hidePopover() {
        this.open = false;
        this.dispatchEvent(new Event('toggle'));
    }
}

class FakeDialog extends FakeElement {
    constructor(documentTarget, hasTitle) {
        super('dialog', documentTarget);
        this.open = true;
        this.heading = hasTitle ? new FakeElement('heading', documentTarget) : null;
        this.dialogBody = new FakeElement('dialog-body', documentTarget);
        this.firstControl = new FakeElement('button', documentTarget);
        this.dialogBody.append(this.firstControl);
        if (this.heading) { this.append(this.heading); }
        this.append(this.dialogBody);
    }

    querySelector(selector) {
        if (selector === 'h2[tabindex="-1"]') { return this.heading; }
        if (selector === '.dialog-body') { return this.dialogBody; }
        if (selector === '.inline-alert') { return this.inlineAlert ?? null; }
        return super.querySelector(selector);
    }
}

function createNotificationFixture({ dialogTitle = null, withToast = true, filterInitiallyMounted = true } = {}) {
    const documentTarget = new FakeDocument();
    if (!filterInitiallyMounted) { documentTarget.filterPane = null; }
    const windowTarget = new FakeElement('window', documentTarget);
    windowTarget.innerWidth = 800;
    windowTarget.innerHeight = 600;
    globalThis.document = documentTarget;
    globalThis.window = windowTarget;
    globalThis.HTMLElement = FakeElement;
    globalThis.Element = FakeElement;
    globalThis.HTMLDialogElement = FakeDialog;
    globalThis.getComputedStyle = element => ({
        direction: element.direction ?? 'ltr',
        outlineStyle: element.outlineStyle ?? 'none',
        outlineWidth: element.outlineWidth ?? '0px',
        outlineOffset: element.outlineOffset ?? '0px'
    });
    const animationFrames = new Map();
    const cancelledFrames = [];
    let requestedFrames = 0;
    globalThis.requestAnimationFrame = callback => {
        const identifier = ++requestedFrames;
        animationFrames.set(identifier, callback);
        return identifier;
    };
    globalThis.cancelAnimationFrame = identifier => {
        cancelledFrames.push(identifier);
        animationFrames.delete(identifier);
    };
    const flushFrame = () => {
        const callbacks = [...animationFrames.values()];
        animationFrames.clear();
        for (const callback of callbacks) { callback(); }
    };
    const observers = [];
    class Observer {
        constructor(callback) {
            this.callback = callback;
            this.disconnected = false;
            this.observations = [];
            observers.push(this);
        }
        observe(target, options) {
            this.disconnected = false;
            this.target = target;
            this.observations.push(target);
            this.targets ??= [];
            this.targets.push(target);
            this.options = options;
        }
        disconnect() {
            this.disconnected = true;
            this.disconnectCount = (this.disconnectCount ?? 0) + 1;
            this.targets = [];
        }
        unobserve() {}
        fire(targets = this.targets ?? []) { this.callback(targets.map(target => ({ target, addedNodes: [] }))); }
    }
    globalThis.MutationObserver = class extends Observer { kind = 'mutation'; };
    globalThis.ResizeObserver = class extends Observer { kind = 'resize'; };

    const center = new FakeElement('center', documentTarget);
    const trigger = new FakeElement('trigger', documentTarget);
    trigger.dataset.notificationTrigger = '';
    trigger.parentElement = center;
    const panel = new FakeElement('panel', documentTarget);
    panel.dataset.notificationPanel = '';
    panel.open = false;
    panel.parentElement = center;
    panel.focus = FakeElement.prototype.focus.bind(panel);
    panel.querySelector = selector => FakeElement.prototype.querySelector.call(panel, selector);
    panel.querySelectorAll = selector => FakeElement.prototype.querySelectorAll.call(panel, selector);
    panel.contains = target => FakeElement.prototype.contains.call(panel, target);
    panel.hidePopover = () => {
        panel.open = false;
        panel.dispatchEvent(new Event('toggle'));
    };
    const card = new FakeElement('card', documentTarget);
    card.dataset.notificationCard = '';
    const dismissButton = new FakeElement('button', documentTarget);
    dismissButton.dataset.bannerDismiss = '';
    card.append(dismissButton);
    panel.append(card);
    const toast = withToast ? new FakeElement('toast', documentTarget) : null;
    if (toast) { toast.parentElement = center; }
    const dialog = dialogTitle === null ? null : new FakeDialog(documentTarget, dialogTitle);
    if (dialog) {
        const header = new FakeElement('header', documentTarget);
        dialog.append(header);
        header.append(panel);
        header.append(trigger);
    }
    const dotNetReference = {
        calls: [],
        invokeMethodAsync(method, isOpen) {
            this.calls.push([method, isOpen]);
            return Promise.resolve();
        }
    };
    attach(trigger, panel, dotNetReference, toast);
    const open = () => {
        panel.open = true;
        panel.dispatchEvent(new Event('toggle'));
    };
    return {
        animationFrames,
        cancelledFrames,
        flushFrame,
        get requestedFrames() { return requestedFrames; },
        card,
        center,
        dialog,
        dismissButton,
        documentTarget,
        dotNetReference,
        observers,
        open,
        panel,
        toast,
        trigger,
        windowTarget
    };
}

test('Escape closes the panel in bubble phase, restores the trigger, and prevents held repeats', () => {
    const fixture = createNotificationFixture();
    fixture.open();
    assert.equal(document.activeElement, fixture.panel);
    assert.equal(fixture.trigger.getAttribute('aria-expanded'), 'true');
    const escapeListener = fixture.documentTarget.listeners.find(listener =>
        listener.callback.name === 'onEscapeKeyDown');
    assert.equal(escapeListener?.options?.capture ?? false, false);

    const firstEscape = Object.assign(new Event('keydown', { cancelable: true }), { key: 'Escape' });
    fixture.documentTarget.invokeListeners('keydown', firstEscape);
    assert.equal(fixture.panel.open, false);
    assert.equal(firstEscape.defaultPrevented, true);
    assert.equal(fixture.trigger.getAttribute('aria-expanded'), 'false');
    assert.equal(document.activeElement, fixture.trigger);

    const repeatedEscape = Object.assign(new Event('keydown', { cancelable: true }), { key: 'Escape' });
    fixture.documentTarget.invokeListeners('keydown', repeatedEscape);
    assert.equal(repeatedEscape.defaultPrevented, true);
    fixture.documentTarget.invokeListeners('keyup', Object.assign(new Event('keyup'), { key: 'Escape' }));
    detach(fixture.panel);
});

test('Opening keeps the panel unpainted until it is positioned, and a cancelled open leaves it untouched', () => {
    const fixture = createNotificationFixture();
    const beforeOpen = Object.assign(new Event('beforetoggle', { cancelable: true }), { newState: 'open' });
    fixture.panel.dispatchEvent(beforeOpen);
    assert.equal(fixture.panel.style.visibility, 'hidden');

    fixture.open();
    assert.equal(fixture.panel.style.visibility, '');
    assert.notEqual(fixture.panel.style.top, undefined);

    fixture.panel.open = false;
    fixture.trigger.disabled = true;
    const blockedOpen = Object.assign(new Event('beforetoggle', { cancelable: true }), { newState: 'open' });
    fixture.panel.dispatchEvent(blockedOpen);
    assert.equal(blockedOpen.defaultPrevented, true);
    assert.equal(fixture.panel.style.visibility, '');
    detach(fixture.panel);
});

test('An already-prevented Escape leaves the panel open', () => {
    const fixture = createNotificationFixture();
    fixture.open();
    const handledEscape = Object.assign(new Event('keydown', { cancelable: true }), { key: 'Escape' });
    handledEscape.preventDefault();
    fixture.documentTarget.invokeListeners('keydown', handledEscape);
    assert.equal(fixture.panel.open, true);
    detach(fixture.panel);
});

test('Focusing the notification trigger does not dismiss its open panel', () => {
    const fixture = createNotificationFixture();
    fixture.open();
    fixture.trigger.focus();
    assert.equal(fixture.panel.open, true);
    assert.equal(document.activeElement, fixture.trigger);
    detach(fixture.panel);
});

test('Holding Enter after dismiss activation prevents a second control activation', () => {
    const fixture = createNotificationFixture();
    fixture.open();
    fixture.dismissButton.focus();
    const firstKeyDown = Object.assign(new Event('keydown', { cancelable: true }), { key: 'Enter' });
    Object.defineProperty(firstKeyDown, 'target', { value: fixture.dismissButton });
    fixture.documentTarget.invokeListeners('keydown', firstKeyDown);
    assert.equal(firstKeyDown.defaultPrevented, false);

    const repeatedKeyDown = Object.assign(new Event('keydown', { cancelable: true }), { key: 'Enter' });
    Object.defineProperty(repeatedKeyDown, 'target', { value: fixture.documentTarget.workspace });
    fixture.documentTarget.invokeListeners('keydown', repeatedKeyDown);
    assert.equal(repeatedKeyDown.defaultPrevented, true);

    fixture.documentTarget.invokeListeners('keyup', Object.assign(new Event('keyup'), { key: 'Enter' }));
    const nextKeyDown = Object.assign(new Event('keydown', { cancelable: true }), { key: 'Enter' });
    Object.defineProperty(nextKeyDown, 'target', { value: fixture.dismissButton });
    fixture.documentTarget.invokeListeners('keydown', nextKeyDown);
    assert.equal(nextKeyDown.defaultPrevented, false);
    detach(fixture.panel);
});

test('Holding Space after dismiss activation prevents a second control activation', () => {
    const fixture = createNotificationFixture();
    fixture.open();
    const firstKeyDown = Object.assign(new Event('keydown', { cancelable: true }), { key: ' ', code: 'Space' });
    Object.defineProperty(firstKeyDown, 'target', { value: fixture.dismissButton });
    fixture.documentTarget.invokeListeners('keydown', firstKeyDown);
    const repeatedKeyDown = Object.assign(new Event('keydown', { cancelable: true }), { key: ' ', code: 'Space' });
    Object.defineProperty(repeatedKeyDown, 'target', { value: fixture.documentTarget.workspace });
    fixture.documentTarget.invokeListeners('keydown', repeatedKeyDown);
    assert.equal(repeatedKeyDown.defaultPrevented, true);
    detach(fixture.panel);
});

test('Programmatic zero-count retirement moves focus and dismisses focus tooltips before the trigger hides', () => {
    const fixture = createNotificationFixture();
    fixture.trigger.focus();
    let tooltipDismissals = 0;
    fixture.documentTarget.addEventListener('focus-tooltip:dismiss', () => tooltipDismissals++);

    retireFocusBeforeHide(fixture.panel);

    assert.equal(document.activeElement, fixture.documentTarget.workspace);
    assert.equal(tooltipDismissals, 1);
    assert.equal(fixture.panel.open, false);
    detach(fixture.panel);
});

test('A status-bar slot change schedules repositioning only for open panels or desired toasts', () => {
    const fixture = createNotificationFixture();
    slotChanged(fixture.panel);
    assert.equal(fixture.animationFrames.size, 0);

    fixture.open();
    slotChanged(fixture.panel);
    assert.equal(fixture.animationFrames.size, 1);
    detach(fixture.panel);
});

test('Programmatic zero-count retirement moves panel focus before closing', () => {
    const fixture = createNotificationFixture();
    fixture.open();
    let focusedWhenHidden = null;
    const hidePopover = fixture.panel.hidePopover;
    fixture.panel.hidePopover = () => {
        focusedWhenHidden = document.activeElement;
        hidePopover.call(fixture.panel);
    };

    retireFocusBeforeHide(fixture.panel);

    assert.equal(focusedWhenHidden, fixture.documentTarget.workspace);
    assert.equal(fixture.panel.open, false);
    detach(fixture.panel);
});

test('Programmatic zero-count retirement focuses the titled dialog heading, not a control', () => {
    const fixture = createNotificationFixture({ dialogTitle: true });
    const dialog = fixture.dialog;
    fixture.open();
    fixture.panel.focus();
    retireFocusBeforeHide(fixture.panel);
    assert.equal(document.activeElement, dialog.heading);
    assert.notEqual(document.activeElement, dialog.firstControl);
    detach(fixture.panel);
});

test('Programmatic zero-count retirement focuses the titleless dialog body, not a control', () => {
    const fixture = createNotificationFixture({ dialogTitle: false });
    const dialog = fixture.dialog;
    fixture.open();
    fixture.panel.focus();
    retireFocusBeforeHide(fixture.panel);
    assert.equal(dialog.dialogBody.getAttribute('tabindex'), '-1');
    assert.equal(document.activeElement, dialog.dialogBody);
    assert.notEqual(document.activeElement, dialog.firstControl);
    detach(fixture.panel);
});

test('A removed focused card moves focus to the next card and maps detached actions to the trigger', () => {
    const fixture = createNotificationFixture();
    fixture.open();
    fixture.dismissButton.focus();
    const nextCard = new FakeElement('card', document);
    nextCard.dataset.notificationCard = '';
    const nextButton = new FakeElement('button', document);
    nextCard.append(nextButton);
    nextCard.parentElement = fixture.panel;
    fixture.panel.children.push(nextCard);
    fixture.dismissButton.isConnected = false;
    document.activeElement = document.body;
    const mutationObserver = fixture.observers.find(observer => observer.target === fixture.panel);

    mutationObserver.callback([{ addedNodes: [] }]);

    assert.equal(document.activeElement, nextButton);
    assert.equal(normalizeReturnTarget(fixture.dismissButton), fixture.trigger);
    detach(fixture.panel);
});

for (const fallbackTarget of ['body', 'documentElement', null]) {
    test(`Transient ${fallbackTarget} focus does not close the panel before removed-card focus recovery`, () => {
        const fixture = createNotificationFixture();
        fixture.open();
        fixture.dismissButton.focus();
        const nextCard = new FakeElement('card', document);
        nextCard.dataset.notificationCard = '';
        const nextButton = new FakeElement('button', document);
        nextCard.append(nextButton);
        nextCard.parentElement = fixture.panel;
        fixture.panel.children = [nextCard];
        fixture.dismissButton.isConnected = false;
        document.activeElement = fallbackTarget ? document[fallbackTarget] : null;

        fixture.documentTarget.invokeListeners('focusin', { target: document.activeElement });
        assert.equal(fixture.panel.open, true);
        fixture.observers.find(observer => observer.target === fixture.panel).callback([{ addedNodes: [] }]);

        assert.equal(document.activeElement, nextButton);
        assert.equal(fixture.panel.open, true);
        fixture.documentTarget.workspace.focus();
        assert.equal(fixture.panel.open, false);
        detach(fixture.panel);
    });
}

test('Background card removal does not pull focus away from the trigger', () => {
    const fixture = createNotificationFixture();
    fixture.open();
    fixture.dismissButton.focus();
    const nextCard = new FakeElement('card', document);
    nextCard.dataset.notificationCard = '';
    const nextButton = new FakeElement('button', document);
    nextCard.append(nextButton);
    nextCard.parentElement = fixture.panel;
    fixture.panel.children.push(nextCard);
    fixture.trigger.focus();
    fixture.dismissButton.isConnected = false;

    fixture.observers.find(observer => observer.target === fixture.panel).callback([{ addedNodes: [] }]);

    assert.equal(document.activeElement, fixture.trigger);
    detach(fixture.panel);
});

test('A removed final card leaves focus on the panel until the logical count is retired', () => {
    const fixture = createNotificationFixture();
    fixture.open();
    fixture.dismissButton.focus();
    fixture.dismissButton.isConnected = false;
    fixture.panel.children = [];
    document.activeElement = document.body;

    fixture.observers.find(observer => observer.target === fixture.panel).callback([{ addedNodes: [] }]);

    assert.equal(document.activeElement, fixture.panel);
    assert.equal(fixture.panel.open, true);
    detach(fixture.panel);
});

test('Modal action handoff closes the panel and focuses its trigger first', () => {
    const fixture = createNotificationFixture();
    fixture.open();
    prepareModalAction(fixture.panel);
    assert.equal(fixture.panel.open, false);
    assert.equal(document.activeElement, fixture.trigger);
    detach(fixture.panel);
});

test('Main-window positioning never ResizeObserves the footer or filter pane', () => {
    const fixture = createNotificationFixture();
    const resizeTargets = fixture.observers.filter(observer => observer.kind === 'resize')
        .flatMap(observer => observer.observations);
    assert.equal(resizeTargets.includes(fixture.documentTarget.statusBar), false);
    assert.equal(resizeTargets.includes(fixture.documentTarget.filterPane), false);
    assert.deepEqual(resizeTargets, [fixture.panel, fixture.toast, fixture.documentTarget.statsDrawer]);
    detach(fixture.panel);
});

test('Modal resize observer tracks its dialog and applies side-aware panel height and placement', () => {
    const fixture = createNotificationFixture({ dialogTitle: true, withToast: false });
    fixture.trigger.rect = { top: 100, bottom: 124, right: 790, left: 690, width: 100, height: 24 };
    fixture.open();

    const resizeObserver = fixture.observers.find(observer => observer.targets?.includes(fixture.dialog));
    assert.ok(resizeObserver);
    assert.ok(resizeObserver.targets.includes(fixture.panel));
    assert.equal(fixture.panel.style.maxHeight, 'min(36rem, 460px)');
    assert.equal(fixture.panel.style.top, '132px');

    fixture.trigger.rect = { top: 540, bottom: 564, right: 790, left: 690, width: 100, height: 24 };
    resizeObserver.fire([fixture.dialog]);
    assert.equal(fixture.panel.style.top, '132px');
    fixture.flushFrame();

    assert.equal(fixture.panel.style.maxHeight, 'min(36rem, 524px)');
    assert.equal(fixture.panel.style.top, '522px');
    detach(fixture.panel);
});

test('Toast remains visible with a large workspace focus target and hides only when it fully covers a control', () => {
    const fixture = createNotificationFixture();
    fixture.toast.rect = { top: 300, bottom: 400, right: 790, left: 500, width: 290, height: 100 };
    fixture.documentTarget.workspace.rect = { top: 0, bottom: 600, right: 800, left: 0, width: 800, height: 600 };

    positionToast(fixture.toast, true);
    fixture.flushFrame();
    assert.equal(fixture.toast.dataset.toastVisible, 'true');

    const coveredControl = new FakeElement('button', fixture.documentTarget);
    coveredControl.rect = { top: 340, bottom: 360, right: 650, left: 620, width: 30, height: 20 };
    fixture.documentTarget.activeElement = coveredControl;
    positionToast(fixture.toast, true);
    fixture.flushFrame();
    assert.equal(fixture.toast.dataset.toastVisible, 'false');

    fixture.documentTarget.activeElement = fixture.documentTarget.workspace;
    positionToast(fixture.toast, true);
    fixture.flushFrame();
    assert.equal(fixture.toast.dataset.toastVisible, 'true');
    detach(fixture.panel);
});

test('A focusin event hides a fully covered control only in the scheduled frame', () => {
    const fixture = createNotificationFixture();
    fixture.toast.rect = { top: 300, bottom: 400, right: 790, left: 500, width: 290, height: 100 };
    fixture.documentTarget.workspace.rect = { top: 0, bottom: 600, right: 800, left: 0, width: 800, height: 600 };
    positionToast(fixture.toast, true);
    fixture.flushFrame();
    assert.equal(fixture.toast.dataset.toastVisible, 'true');

    const coveredControl = new FakeElement('button', fixture.documentTarget);
    coveredControl.rect = { top: 340, bottom: 360, right: 650, left: 620, width: 30, height: 20 };
    fixture.documentTarget.activeElement = coveredControl;
    fixture.documentTarget.invokeListeners('focusin', { target: coveredControl });
    assert.equal(fixture.toast.dataset.toastVisible, 'true');
    fixture.flushFrame();

    assert.equal(fixture.toast.dataset.toastVisible, 'false');
    detach(fixture.panel);
});

test('Toast hides when there is not enough room below the filter toolbar', () => {
    const fixture = createNotificationFixture();
    fixture.documentTarget.filterPane.rect = { top: 0, bottom: 500, left: 0, right: 800, width: 800, height: 500 };
    fixture.documentTarget.statusBar.rect = { top: 550, bottom: 600, left: 0, right: 800, width: 800, height: 50 };

    positionToast(fixture.toast, true);
    fixture.flushFrame();

    assert.equal(fixture.toast.dataset.toastVisible, 'false');
    detach(fixture.panel);
});

test('Unavailable notifications close the panel and detach releases observers', () => {
    const fixture = createNotificationFixture();
    fixture.open();
    setUnavailable(fixture.panel, true);
    assert.equal(fixture.panel.open, false);
    detach(fixture.panel);
    assert.ok(fixture.observers.every(observer => observer.disconnected));
});

test('A burst of main-window triggers coalesces into one frame without synchronous geometry writes', () => {
    const fixture = createNotificationFixture();
    const resizeObserver = fixture.observers.find(observer => observer.kind === 'resize');
    const filterObserver = fixture.observers.find(observer => observer.target === fixture.documentTarget.filterPane);
    assert.deepEqual(filterObserver.options, {
        childList: true, subtree: true, characterData: true, attributes: true
    });
    const initialReads = fixture.trigger.geometryReads + fixture.toast.geometryReads;

    positionToast(fixture.toast, true);
    fixture.windowTarget.dispatchEvent(new Event('resize'));
    resizeObserver.fire();
    for (const type of ['childList', 'attributes', 'characterData']) {
        filterObserver.callback([{ type }]);
    }
    fixture.documentTarget.workspace.dispatchEvent(new Event('scroll'));
    fixture.documentTarget.invokeListeners('focusin', { target: fixture.documentTarget.workspace });

    assert.equal(fixture.requestedFrames, 1);
    assert.equal(fixture.animationFrames.size, 1);
    assert.deepEqual(fixture.toast.geometryWrites, []);
    assert.deepEqual(fixture.panel.geometryWrites, []);
    assert.equal(fixture.trigger.geometryReads + fixture.toast.geometryReads, initialReads);
    assert.equal(fixture.toast.dataset.toastVisible, undefined);
    fixture.flushFrame();
    assert.equal(fixture.toast.dataset.toastVisible, 'true');
    assert.equal(fixture.toast.style.bottom, '38px');
    assert.equal(fixture.toast.style.maxHeight, '192px');
    assert.equal(fixture.animationFrames.size, 0);
    const scrollListener = fixture.documentTarget.workspace.listeners.find(listener => listener.type === 'scroll');
    assert.equal(scrollListener.options.passive, true);
    assert.equal(scrollListener.options.capture ?? false, false);
    assert.equal(fixture.documentTarget.listeners.some(listener => listener.type === 'scroll'), false);
    assert.equal(fixture.windowTarget.listeners.some(listener => listener.type === 'scroll'), false);
    detach(fixture.panel);
});

for (const dialogTitle of [null, true]) {
    const hostName = dialogTitle ? 'modal' : 'main';
    test(`Panel opening positions immediately and later observers defer geometry in ${hostName} hosts`, () => {
        const fixture = createNotificationFixture({ dialogTitle, withToast: dialogTitle === null });
        fixture.open();
        assert.equal(fixture.panel.style.top, '552px');
        assert.equal(fixture.panel.style.maxHeight, 'min(36rem, 554px)');
        assert.equal(fixture.animationFrames.size, 1);
        fixture.flushFrame();
        fixture.panel.geometryWrites.length = 0;
        const initialReads = fixture.trigger.geometryReads + fixture.panel.geometryReads;
        fixture.trigger.rect = { top: 450, bottom: 474, right: 790, left: 690, width: 100, height: 24 };
        const resizeObserver = fixture.observers.find(observer => observer.kind === 'resize');
        resizeObserver.fire();
        fixture.observers.find(observer => observer.target === fixture.panel).callback([{ addedNodes: [] }]);
        fixture.observers.find(observer => observer.observations.includes(fixture.trigger)).callback([]);
        fixture.windowTarget.dispatchEvent(new Event('resize'));
        assert.deepEqual(fixture.panel.geometryWrites, []);
        assert.equal(fixture.trigger.geometryReads + fixture.panel.geometryReads, initialReads);
        assert.equal(fixture.animationFrames.size, 1);
        fixture.flushFrame();
        assert.equal(fixture.panel.style.top, '432px');
        assert.equal(fixture.panel.style.maxHeight, 'min(36rem, 434px)');
        detach(fixture.panel);
    });
}

test('Idle observers and events do not request frames without an open panel or desired toast', () => {
    const fixture = createNotificationFixture();
    for (const observer of fixture.observers) {
        observer.callback([{ addedNodes: [] }]);
    }
    fixture.windowTarget.dispatchEvent(new Event('resize'));
    fixture.documentTarget.workspace.dispatchEvent(new Event('scroll'));
    fixture.documentTarget.invokeListeners('focusin', { target: fixture.trigger });
    assert.equal(fixture.requestedFrames, 0);
    detach(fixture.panel);
});

for (const hideMethod of ['expiry', 'unavailable', 'panel open']) {
    test(`Toast ${hideMethod} hides synchronously and stays hidden across a pending frame`, () => {
        const fixture = createNotificationFixture();
        positionToast(fixture.toast, true);
        fixture.flushFrame();
        assert.equal(fixture.toast.dataset.toastVisible, 'true');
        fixture.windowTarget.dispatchEvent(new Event('resize'));
        assert.equal(fixture.animationFrames.size, 1);
        const initialToastWrites = fixture.toast.geometryWrites.length;
        if (hideMethod === 'expiry') { positionToast(fixture.toast, false); }
        if (hideMethod === 'unavailable') { setUnavailable(fixture.panel, true); }
        if (hideMethod === 'panel open') { fixture.open(); }
        assert.equal(fixture.toast.dataset.toastVisible, 'false');
        fixture.flushFrame();
        assert.equal(fixture.toast.dataset.toastVisible, 'false');
        assert.equal(fixture.toast.geometryWrites.length, initialToastWrites);
        detach(fixture.panel);
    });
}

test('Filter observation follows mount, replacement and unmount and ignores unrelated workspace mutations', () => {
    const fixture = createNotificationFixture({ filterInitiallyMounted: false });
    const workspaceObserver = fixture.observers.find(observer => observer.target === fixture.documentTarget.workspace);
    assert.deepEqual(workspaceObserver.options, { childList: true, subtree: true });
    positionToast(fixture.toast, true);
    fixture.flushFrame();
    assert.equal(fixture.toast.dataset.toastVisible, 'true');
    workspaceObserver.callback([{ addedNodes: [] }]);
    assert.equal(fixture.animationFrames.size, 0);

    const firstFilter = new FakeElement('filter-pane', fixture.documentTarget);
    firstFilter.rect = { top: 20, bottom: 530, left: 0, right: 800, width: 800, height: 510 };
    fixture.documentTarget.filterPane = firstFilter;
    workspaceObserver.callback([{ addedNodes: [firstFilter] }]);
    const filterObserver = fixture.observers.find(observer => observer.target === firstFilter);
    assert.ok(filterObserver);
    assert.equal(fixture.toast.dataset.toastVisible, 'true');
    fixture.flushFrame();
    assert.equal(fixture.toast.dataset.toastVisible, 'false');

    const replacementFilter = new FakeElement('filter-pane', fixture.documentTarget);
    replacementFilter.rect = { top: 20, bottom: 200, left: 0, right: 800, width: 800, height: 180 };
    fixture.documentTarget.filterPane = replacementFilter;
    const disconnectsBefore = filterObserver.disconnectCount;
    workspaceObserver.callback([{ addedNodes: [replacementFilter] }]);
    assert.equal(filterObserver.disconnectCount, disconnectsBefore + 1);
    assert.deepEqual(filterObserver.targets, [replacementFilter]);
    fixture.flushFrame();
    assert.equal(fixture.toast.dataset.toastVisible, 'true');

    replacementFilter.rect.bottom = 520;
    filterObserver.callback([{ type: 'attributes', attributeName: 'data-toggle' }]);
    fixture.flushFrame();
    assert.equal(fixture.toast.dataset.toastVisible, 'false');
    fixture.documentTarget.filterPane = null;
    workspaceObserver.callback([{ addedNodes: [] }]);
    assert.equal(filterObserver.disconnected, true);
    assert.deepEqual(filterObserver.targets, []);
    fixture.flushFrame();
    assert.equal(fixture.toast.dataset.toastVisible, 'true');

    fixture.documentTarget.filterPane = firstFilter;
    workspaceObserver.callback([{ addedNodes: [firstFilter] }]);
    assert.deepEqual(filterObserver.targets, [firstFilter]);
    fixture.flushFrame();
    assert.equal(fixture.toast.dataset.toastVisible, 'false');
    detach(fixture.panel);
});

test('The stats drawer top bounds the toast while open and the footer top bounds it while closed', () => {
    const fixture = createNotificationFixture();
    positionToast(fixture.toast, true);
    fixture.flushFrame();
    const resizeObserver = fixture.observers.find(observer => observer.kind === 'resize');
    for (const [height, expectedBottom, expectedMaximum, expectedVisible] of [
        [240, '278px', '192px', 'true'],
        [120, '158px', '192px', 'true'],
        [300, '338px', '162px', 'true'],
        [420, '458px', '192px', 'false'],
        [0, '38px', '192px', 'true']
    ]) {
        fixture.documentTarget.statsDrawer.rect = {
            top: 570 - height, bottom: 570, left: 0, right: 800, width: 800, height
        };
        const previousBottom = fixture.toast.style.bottom;
        resizeObserver.fire([fixture.documentTarget.statsDrawer]);
        assert.equal(fixture.toast.style.bottom, previousBottom);
        fixture.flushFrame();
        assert.equal(fixture.toast.dataset.toastVisible, expectedVisible);
        if (expectedVisible === 'true') {
            assert.equal(fixture.toast.style.bottom, expectedBottom);
            assert.equal(fixture.toast.style.maxHeight, expectedMaximum);
        }
    }
    fixture.documentTarget.filterPane.rect.bottom = 400;
    fixture.documentTarget.statsDrawer.rect = {
        top: 450, bottom: 570, left: 0, right: 800, width: 800, height: 120
    };
    resizeObserver.fire();
    fixture.flushFrame();
    assert.equal(fixture.toast.dataset.toastVisible, 'false');
    fixture.documentTarget.statsDrawer.rect.height = 0;
    resizeObserver.fire();
    fixture.flushFrame();
    assert.equal(fixture.toast.style.bottom, '38px');
    assert.equal(fixture.toast.style.maxHeight, '162px');
    assert.equal(fixture.toast.dataset.toastVisible, 'true');
    detach(fixture.panel);
});

test('Without a filter pane the workspace top determines available toast height', () => {
    const fixture = createNotificationFixture({ filterInitiallyMounted: false });
    fixture.documentTarget.workspace.rect.top = 450;
    positionToast(fixture.toast, true);
    fixture.flushFrame();
    assert.equal(fixture.toast.style.maxHeight, '112px');
    assert.equal(fixture.toast.dataset.toastVisible, 'true');
    fixture.documentTarget.workspace.rect.top = 510;
    fixture.documentTarget.workspace.dispatchEvent(new Event('scroll'));
    fixture.flushFrame();
    assert.equal(fixture.toast.dataset.toastVisible, 'false');
    detach(fixture.panel);
});

test('Workspace scroll rechecks filter geometry without a mutation or a window resize', () => {
    const fixture = createNotificationFixture();
    fixture.documentTarget.filterPane.rect.bottom = 530;
    positionToast(fixture.toast, true);
    fixture.flushFrame();
    assert.equal(fixture.toast.dataset.toastVisible, 'false');
    fixture.documentTarget.filterPane.rect.bottom = 350;
    fixture.documentTarget.workspace.dispatchEvent(new Event('scroll'));
    assert.equal(fixture.toast.dataset.toastVisible, 'false');
    fixture.flushFrame();
    assert.equal(fixture.toast.dataset.toastVisible, 'true');
    detach(fixture.panel);
});

test('Focus outside closes the panel synchronously while focus in the trigger or panel never closes it', () => {
    const fixture = createNotificationFixture();
    fixture.open();
    fixture.flushFrame();
    fixture.dismissButton.focus();
    assert.equal(fixture.panel.open, true);
    fixture.trigger.focus();
    assert.equal(fixture.panel.open, true);
    fixture.documentTarget.workspace.focus();
    assert.equal(fixture.panel.open, false);
    assert.equal(document.activeElement, fixture.documentTarget.workspace);
    detach(fixture.panel);
});

test('Detach cancels frames, disconnects observers and removes listeners despite queued callbacks', () => {
    const fixture = createNotificationFixture();
    positionToast(fixture.toast, true);
    const pendingCallback = [...fixture.animationFrames.values()][0];
    const requestedBefore = fixture.requestedFrames;
    const frameIdentifier = [...fixture.animationFrames.keys()][0];
    detach(fixture.panel);
    assert.deepEqual(fixture.cancelledFrames, [frameIdentifier]);
    assert.equal(fixture.animationFrames.size, 0);
    assert.ok(fixture.observers.every(observer => observer.disconnected));
    const listeners = [fixture.documentTarget, fixture.windowTarget, fixture.documentTarget.workspace,
        fixture.documentTarget.statusBar, fixture.panel].flatMap(target => target.listeners);
    assert.ok(listeners.every(listener => listener.options.signal.aborted));
    for (const observer of fixture.observers) { observer.callback([{ addedNodes: [] }]); }
    fixture.windowTarget.dispatchEvent(new Event('resize'));
    fixture.documentTarget.workspace.dispatchEvent(new Event('scroll'));
    pendingCallback();
    assert.equal(fixture.requestedFrames, requestedBefore);
    assert.deepEqual(fixture.panel.geometryWrites, []);
    assert.deepEqual(fixture.toast.geometryWrites, []);
});

for (const key of ['Enter', ' ']) {
    test(`Clear all latches ${key} across confirmation until keyup or blur`, () => {
        const fixture = createNotificationFixture();
        const clearButton = new FakeElement('button', document);
        clearButton.dataset.notificationClearAll = '';
        fixture.panel.append(clearButton);
        const keyDown = target => {
            const event = Object.assign(new Event('keydown', { cancelable: true }), { key });
            Object.defineProperty(event, 'target', { value: target });
            fixture.documentTarget.invokeListeners('keydown', event);
            return event;
        };
        assert.equal(keyDown(clearButton).defaultPrevented, false);
        assert.equal(keyDown(fixture.documentTarget.workspace).defaultPrevented, true);
        fixture.documentTarget.invokeListeners('keyup', { key });
        assert.equal(keyDown(clearButton).defaultPrevented, false);
        fixture.windowTarget.dispatchEvent(new Event('blur'));
        assert.equal(keyDown(fixture.documentTarget.workspace).defaultPrevented, false);
        detach(fixture.panel);
    });
}

for (const modalHost of [false, true]) {
    for (const outcome of ['empty', 'remaining']) {
        for (const restoreFirst of [false, true]) {
            test(`Clear focus ${modalHost ? 'inline' : 'standalone'} ${outcome}, restore ${restoreFirst ? 'first' : 'last'}`, async () => {
                const fixture = createNotificationFixture({ dialogTitle: modalHost ? true : null });
                const { trigger, panel, dialog } = fixture;
                trigger.focus();
                let closeAlert;
                if (modalHost) {
                    captureInlineAlertFocus(dialog);
                    dialog.inlineAlert = new FakeElement('inline-alert', document);
                    dialog.append(dialog.inlineAlert);
                    const cancel = new FakeElement('button', document);
                    dialog.inlineAlert.append(cancel);
                    rememberInlineAlert(dialog);
                    cancel.focus();
                    closeAlert = () => restoreInlineAlertFocus(dialog);
                } else {
                    const alert = new FakeDialog(document, false);
                    alert._returnFocusElement = trigger;
                    alert.firstControl.focus();
                    closeAlert = () => closeModal(alert);
                }
                closeAlert();
                if (restoreFirst) { fixture.flushFrame(); }
                const completed = await completeClearFocus(panel,
                    outcome);
                assert.equal(completed, true);
                const expected = outcome === 'empty'
                    ? dialog?.heading ?? fixture.documentTarget.workspace : trigger;
                assert.equal(document.activeElement, expected);
                fixture.flushFrame();
                assert.equal(document.activeElement, expected);
                detach(panel);
            });
        }
    }
}

for (const modalHost of [false, true]) {
    test(`Pending clear focus drains exactly once after ${modalHost ? 'inert' : 'disabled'} clears`, async () => {
        const fixture = createNotificationFixture({ dialogTitle: modalHost ? false : null });
        const header = fixture.trigger.closest('header');
        if (modalHost) { header.inert = true; } else { fixture.trigger.disabled = true; }
        const pending = completeClearFocus(fixture.panel, 'remaining');
        assert.equal(document.activeElement, document.body);
        if (modalHost) { header.inert = false; } else { fixture.trigger.disabled = false; }
        const observer = fixture.observers.find(candidate => candidate.observations.includes(fixture.trigger));
        observer.fire();
        assert.equal(await pending, true);
        assert.equal(document.activeElement, fixture.trigger);
        observer.fire();
        assert.equal(fixture.trigger.focusCount, 1);
        detach(fixture.panel);
    });

    test(`Detached ${modalHost ? 'modal' : 'main'} host discards pending clear focus without fallback`, async () => {
        const fixture = createNotificationFixture({ dialogTitle: modalHost ? true : null });
        fixture.trigger.disabled = true;
        const pending = completeClearFocus(fixture.panel, 'remaining');
        fixture.panel.isConnected = false;
        fixture.trigger.isConnected = false;
        fixture.trigger.disabled = false;
        const observer = fixture.observers.find(candidate => candidate.observations.includes(fixture.trigger));
        observer.fire();
        assert.equal(await pending, false);
        assert.equal(document.activeElement, document.body);
        fixture.panel.isConnected = true;
        fixture.trigger.isConnected = true;
        observer.fire();
        assert.equal(document.activeElement, document.body);
        detach(fixture.panel);
    });
}

for (const ownershipSignal of ['modal', 'active-dialog']) {
    test(`Another dialog's ${ownershipSignal} ownership discards pending clear focus`, async () => {
        const fixture = createNotificationFixture();
        fixture.trigger.disabled = true;
        const pending = completeClearFocus(fixture.panel, 'remaining');
        const otherDialog = new FakeDialog(document, false);
        if (ownershipSignal === 'modal') { fixture.documentTarget.modalDialogs = [otherDialog]; }
        else { otherDialog.firstControl.focus(); }
        const previousFocus = document.activeElement;
        fixture.trigger.disabled = false;
        const observer = fixture.observers.find(candidate => candidate.observations.includes(fixture.trigger));
        observer.fire();
        assert.equal(await pending, false);
        assert.equal(document.activeElement, previousFocus);
        fixture.documentTarget.modalDialogs = [];
        document.activeElement = document.body;
        observer.fire();
        assert.equal(document.activeElement, document.body);
        detach(fixture.panel);
    });
}

test('A closed alert button is orphaned when disabled clears before its close frame', async () => {
    const fixture = createNotificationFixture();
    const alert = new FakeDialog(document, false);
    alert._returnFocusElement = fixture.trigger;
    alert.firstControl.focus();
    fixture.trigger.disabled = true;
    closeModal(alert);
    const pending = completeClearFocus(fixture.panel, 'remaining');
    assert.equal(document.activeElement, alert.firstControl);
    fixture.trigger.disabled = false;
    fixture.observers.find(candidate => candidate.observations.includes(fixture.trigger)).fire();
    assert.equal(await pending, true);
    assert.equal(document.activeElement, fixture.trigger);
    fixture.flushFrame();
    assert.equal(document.activeElement, fixture.trigger);
    detach(fixture.panel);
});

test('A newer clear transaction supersedes the pending generation', async () => {
    const fixture = createNotificationFixture();
    fixture.trigger.disabled = true;
    const first = completeClearFocus(fixture.panel, 'remaining');
    const second = completeClearFocus(fixture.panel, 'empty');
    assert.equal(await first, false);
    fixture.trigger.disabled = false;
    fixture.observers.find(candidate => candidate.observations.includes(fixture.trigger)).fire();
    assert.equal(await second, true);
    assert.equal(document.activeElement, fixture.documentTarget.workspace);
    detach(fixture.panel);
});

test('Teardown resolves pending focus without focusing a disposed registration', async () => {
    const fixture = createNotificationFixture();
    fixture.trigger.disabled = true;
    const pending = completeClearFocus(fixture.panel, 'remaining');
    detach(fixture.panel);
    assert.equal(await pending, false);
    fixture.trigger.disabled = false;
    fixture.observers.forEach(observer => observer.fire());
    assert.equal(document.activeElement, document.body);
});

test('A closed own dialog discards pending focus without a workspace fallback', async () => {
    const fixture = createNotificationFixture({ dialogTitle: true });
    fixture.trigger.disabled = true;
    const pending = completeClearFocus(fixture.panel, 'remaining');
    fixture.dialog.open = false;
    fixture.trigger.disabled = false;
    fixture.observers.find(candidate => candidate.observations.includes(fixture.trigger)).fire();
    assert.equal(await pending, false);
    assert.equal(document.activeElement, document.body);
    detach(fixture.panel);
});

test('Unavailable notification return triggers normalize to a safe target', () => {
    const fixture = createNotificationFixture();
    fixture.trigger.rendered = false;
    assert.equal(normalizeReturnTarget(fixture.dismissButton), fixture.documentTarget.workspace);
    fixture.trigger.rendered = true;
    fixture.trigger.disabled = true;
    assert.equal(normalizeReturnTarget(fixture.dismissButton), fixture.documentTarget.workspace);
    detach(fixture.panel);
});

test('Focused card removal never focuses a running task Cancel button', () => {
    const fixture = createNotificationFixture();
    fixture.open();
    fixture.dismissButton.focus();
    const progressCard = new FakeElement('card', document);
    progressCard.dataset.notificationCard = '';
    progressCard.isProgress = true;
    progressCard.append(new FakeElement('button', document));
    fixture.panel.append(progressCard);
    fixture.dismissButton.isConnected = false;
    fixture.card.isConnected = false;
    fixture.panel.children = [progressCard];
    document.activeElement = document.body;
    fixture.observers.find(observer => observer.target === fixture.panel).fire();
    assert.equal(document.activeElement, progressCard);
    detach(fixture.panel);
});


test('Panel CSS keeps cards edge-to-edge, separators out of grid flow and the footer outside the scroll area', async () => {
    const css = await readFile(new URL('../../src/EventLogExpert.UI/Banner/NotificationCenter.razor.css', import.meta.url), 'utf8');
    const rule = selector => css.slice(css.indexOf(`${selector} {`) + selector.length + 2).split('}')[0];
    const separator = rule('.notification-active-list ::deep .banner-entry + .banner-entry::before');
    assert.match(separator, /position:\s*absolute;/);
    assert.match(separator, /inset-inline:\s*6px;/);
    assert.match(separator, /border-top:\s*1px solid/);
    const adjacentCard = rule('.notification-active-list ::deep .banner-entry + .banner-entry');
    assert.match(adjacentCard, /margin-top:\s*2px;/);
    assert.doesNotMatch(adjacentCard, /margin:\s*2px 6px/);
    assert.doesNotMatch(rule('.notification-panel'), /display:\s*flex;/);
    assert.match(rule('.notification-panel:popover-open'), /display:\s*flex;/);
    assert.match(rule('.notification-active-list'), /min-height:\s*0;/);
    assert.match(rule('.notification-active-list'), /overflow-y:\s*auto;/);
    assert.match(rule('.notification-panel-footer'), /flex:\s*none;/);
    assert.match(rule('.notification-active-list ::deep .banner-entry:focus-visible'), /outline-offset:\s*-1px;/);
    assert.match(css, /@media \(forced-colors: active\)[\s\S]*::before[\s\S]*border-top-color:\s*CanvasText;/);
});

test('Detach closes the panel without invoking .NET, including an already-queued toggle', () => {
    const fixture = createNotificationFixture();
    fixture.open();
    const queuedToggle = fixture.panel.listeners.find(listener => listener.type === 'toggle').callback;
    const callCount = fixture.dotNetReference.calls.length;

    detach(fixture.panel);
    queuedToggle();

    assert.equal(fixture.panel.open, false);
    assert.equal(fixture.dotNetReference.calls.length, callCount);
});
