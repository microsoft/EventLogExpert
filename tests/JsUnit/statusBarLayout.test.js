import assert from 'node:assert/strict';
import { readFile } from 'node:fs/promises';
import { registerHooks } from 'node:module';
import { test } from 'node:test';

registerHooks({
    resolve(specifier, context, nextResolve) {
        if (specifier.startsWith('../Common/') && context.parentURL.endsWith('/StatusBar.razor.js')) {
            return nextResolve(new URL(specifier.replace('../Common/', '../../src/EventLogExpert.UI/wwwroot/Common/'), import.meta.url).href, context);
        }
        return nextResolve(specifier, context);
    }
});
const { attach, detach } = await import('../../src/EventLogExpert.UI/StatusBar/StatusBar.razor.js');

class FakeElement extends EventTarget {
    attributes = new Map();
    children = [];
    classNames = new Set();
    dataset = {};
    style = { minWidth: '', removeProperty: property => { this.style[property.replace(/-([a-z])/g, (_, letter) => letter.toUpperCase())] = ''; } };
    isConnected = true;
    sequence = [];
    textWrites = 0;

    constructor(document, name, classes = '', attributes = {}) {
        super();
        this.ownerDocument = document;
        this.name = name;
        this.id = attributes.id ?? '';
        this.classNames = new Set(classes.split(' ').filter(Boolean));
        this.clientWidth = 0;
        this.clientHeight = 20;
        this.scrollWidth = 0;
        this.scrollHeight = 20;
        this.rectangle = { width: 0, height: 20 };
        this.textContent = '';
        for (const [attribute, value] of Object.entries(attributes)) {
            this.setAttribute(attribute, value);
        }
    }

    set textContent(value) {
        this._textContent = value;
        if (this.ownerDocument?.writingText) {
            this.textWrites++;
        }
    }

    get textContent() { return this._textContent; }

    append(child) {
        child.parentElement = this;
        this.children.push(child);
    }

    setAttribute(name, value) {
        this.attributes.set(name, String(value));
        if (name.startsWith('data-')) {
            this.dataset[name.slice(5).replace(/-([a-z])/g, (_, letter) => letter.toUpperCase())] = String(value);
        }
        if (name.startsWith('data-statusbar-hidden')) {
            this.ownerDocument?.sequence.push(`hide:${this.dataset.statusbarItem}`);
        }
        this.ownerDocument?.sequence.push(`set:${this.name}:${name}`);
    }

    removeAttribute(name) {
        this.attributes.delete(name);
        if (name.startsWith('data-')) {
            delete this.dataset[name.slice(5).replace(/-([a-z])/g, (_, letter) => letter.toUpperCase())];
        }
    }

    getAttribute(name) { return this.attributes.get(name) ?? null; }
    hasAttribute(name) { return this.attributes.has(name); }
    matches(selector) {
        if (selector === '[data-statusbar-item]') { return this.hasAttribute('data-statusbar-item'); }
        if (selector === '[data-notification-trigger]') { return this.hasAttribute('data-notification-trigger'); }
        if (selector === '.status-bar-source') { return this.classNames.has('status-bar-source'); }
        if (selector === '.status-bar-order-label') { return this.classNames.has('status-bar-order-label'); }
        if (selector === '.status-bar-resolver') { return this.classNames.has('status-bar-resolver'); }
        if (selector === '.status-bar-sep') { return this.classNames.has('status-bar-sep'); }
        if (selector === '[data-variant]') { return this.hasAttribute('data-variant'); }
        return false;
    }

    querySelectorAll(selector) {
        if (selector === '[data-variant][data-active]') {
            return this.children.filter(child => child.hasAttribute('data-variant') && child.hasAttribute('data-active'));
        }
        return [];
    }

    closest(selector) {
        let current = this;
        while (current) {
            if (selector === '[data-statusbar-item]' && current.matches(selector)) { return current; }
            if (selector === 'footer[data-pane="status"]' && current.name === 'status-footer') { return current; }
            if (selector === '[inert]' && current.inert) { return current; }
            if (selector === '[aria-hidden="true"]' && current.getAttribute('aria-hidden') === 'true') { return current; }
            if (selector === '[data-statusbar-hidden]' && current.hasAttribute('data-statusbar-hidden')) { return current; }
            current = current.parentElement;
        }
        return null;
    }

    contains(target) { return target === this || this.children.some(child => child.contains(target)); }

    getClientRects() {
        return this.hasAttribute('hidden') || this.hasAttribute('data-statusbar-hidden') ? [] : [this.getBoundingClientRect()];
    }

    getBoundingClientRect() { return { ...this.rectangle }; }

    focus() {
        this.ownerDocument.activeElement = this;
        this.ownerDocument.sequence.push(`focus:${this.name}`);
    }

    get previousElementSibling() { return this.ownerDocument.previousElement(this); }
    get nextElementSibling() { return this.ownerDocument.nextElement(this); }
}

globalThis.HTMLElement = FakeElement;

class FakeResizeObserver {
    static instances = [];
    constructor(callback) {
        this.callback = callback;
        this.targets = new Set();
        this.disconnected = false;
        FakeResizeObserver.instances.push(this);
    }
    observe(target) { this.targets.add(target); }
    disconnect() { this.disconnected = true; this.targets.clear(); }
    fire(target) { this.callback([{ target }]); }
}

class FakeMutationObserver {
    static instance;
    constructor(callback) {
        this.callback = callback;
        this.records = [];
        FakeMutationObserver.instance = this;
    }
    observe(target, options) { this.target = target; this.options = options; }
    takeRecords() { this.takeRecordsCount = (this.takeRecordsCount ?? 0) + 1; return []; }
    disconnect() { this.disconnected = true; }
    fire(type = 'characterData') { this.callback([{ type }]); }
}

globalThis.ResizeObserver = FakeResizeObserver;
globalThis.MutationObserver = FakeMutationObserver;

function createFixture({ memoryLevel = 'normal', includeCounts = true, triggerVisible = false } = {}) {
    FakeResizeObserver.instances = [];
    const document = {
        activeElement: null,
        fonts: new EventTarget(),
        writingText: false,
        sequence: [],
        elementsById: new Map(),
        defaultView: {
            getComputedStyle: element => {
                document.styleReads = (document.styleReads ?? 0) + 1;
                return ({
                display: element.hasAttribute('data-statusbar-hidden') || element.hasAttribute('hidden') ? 'none' : 'inline',
                font: element.font ?? '400 12px Test',
                letterSpacing: element.letterSpacing ?? 'normal'
            }); }
        },
        createElement(tagName) {
            assert.equal(tagName, 'canvas');
            this.canvasCount = (this.canvasCount ?? 0) + 1;
            return { getContext: () => ({ font: '', measureText: () => {
                document.measurements = (document.measurements ?? 0) + 1;
                return { width: document.zeroWidth ?? 10 };
            } }) };
        },
        getElementById(id) { return this.elementsById.get(id) ?? null; },
        querySelector(selector) { return selector === '[data-notification-trigger]' ? trigger : null; }
    };
    document.activeElement = new FakeElement(document, 'body');
    document.body = document.activeElement;

    const footer = new FakeElement(document, 'status-footer');
    footer.querySelector = selector => selector === '[data-notification-trigger]' ? trigger : null;
    footer.focus = () => {
        document.activeElement = footer;
        document.sequence.push('focus:footer');
    };
    const content = new FakeElement(document, 'content', 'status-bar-content');
    content.dataset.hiddenItemSeparator = '; ';
    content.dataset.hiddenTooltipFormat = '{0}; {1}{2}';
    content.clientWidth = 100;
    content.forcedScrollWidth = 1000;
    Object.defineProperty(content, 'scrollWidth', {
        get() {
            this.scrollWidthReads = (this.scrollWidthReads ?? 0) + 1;
            return this.measureWidth ? this.measureWidth() : this.forcedScrollWidth;
        }
    });
    content.getClientRects = () => [{}];
    content.closest = selector => selector === 'footer[data-pane="status"]' ? footer : null;
    content.getBoundingClientRect = () => ({ width: 100, height: 26 });
    content.querySelector = selector => {
        if (selector === '.status-bar-sizing-sentinel') { return sizingSentinel; }
        if (selector === '.status-bar-font-sentinel') { return fontSentinel; }
        if (selector === '[data-statusbar-counts]') { return items.find(item => item.dataset.statusbarItem === 'counts') ?? null; }
        if (selector === '[data-variant="counts-full"][data-active]') {
            return variants.find(variant => variant.dataset.variant === 'counts-full' && variant.hasAttribute('data-active')) ?? null;
        }
        return null;
    };
    content.querySelectorAll = selector => {
        if (selector === '[data-statusbar-item]') { return items; }
        if (selector === '[data-statusbar-floor]') { return floors; }
        if (selector === '[data-statusbar-companion]') { return companions; }
        if (selector === '.status-bar-sep') { return separators; }
        if (selector === '[data-statusbar-right-first]') { return items.filter(item => item.hasAttribute('data-statusbar-right-first')); }
        if (selector === '[data-statusbar-side="right"][data-statusbar-right-first]') {
            return items.filter(item => item.dataset.statusbarSide === 'right' && item.hasAttribute('data-statusbar-right-first'));
        }
        if (selector === '[data-statusbar-item][data-statusbar-side="right"]') {
            return items.filter(item => item.dataset.statusbarSide === 'right');
        }
        if (selector === '[data-variant]') { return variants; }
        if (selector === '.status-bar-source, .status-bar-order-label, .status-bar-resolver') { return truncatable; }
        if (selector.startsWith('[data-statusbar-companion=')) {
            const key = selector.match(/"([^"]+)"/)?.[1];
            return companions.filter(companion => companion.dataset.statusbarCompanion === key);
        }
        if (selector === '[data-hidden-description]') { return descriptions; }
        return [];
    };

    const sizingSentinel = new FakeElement(document, 'sizing-sentinel');
    const fontSentinel = new FakeElement(document, 'font-sentinel');
    fontSentinel.rectangle.width = 95;
    const trigger = new FakeElement(document, 'notification-trigger', '', { 'data-notification-trigger': '' });
    trigger.getClientRects = () => triggerVisible ? [{}] : [];

    const items = [];
    const variants = [];
    const floors = [];
    const companions = [];
    const descriptions = [];
    const separators = [];
    const sequence = [];
    const itemWidths = new Map();
    const addItem = (key, side = 'left', width = 30) => {
        const item = new FakeElement(document, key, '', { 'data-statusbar-item': key });
        item.dataset.statusbarItem = key;
        item.dataset.statusbarSide = side;
        item.width = width;
        items.push(item);
        sequence.push(item);
        itemWidths.set(key, width);
        return item;
    };
    const addSeparator = () => {
        const separator = new FakeElement(document, `separator-${separators.length}`, 'status-bar-sep');
        separators.push(separator);
        sequence.push(separator);
        return separator;
    };
    const source = addItem('source', 'left', 90);
    const sourceLabel = new FakeElement(document, 'source-label', 'status-bar-source', { 'data-statusbar-floor': '12' });
    sourceLabel.scrollWidth = 60;
    sourceLabel.clientWidth = 60;
    sourceLabel.parentElement = source;
    source.append(sourceLabel);
    floors.push(sourceLabel);
    truncatable.push(sourceLabel);

    addSeparator();
    let counts = null;
    if (includeCounts) {
        counts = addItem('counts', 'left', 70);
        counts.setAttribute('data-statusbar-counts', '');
        for (const [name, width, active] of [['counts-full', 100, true], ['counts-shown-total', 50, false], ['counts-total', 30, false]]) {
            const variant = new FakeElement(document, name, 'status-bar-counts', { 'data-variant': name });
            variant.dataset.variant = name;
            variant.width = width;
            if (active) { variant.setAttribute('data-active', ''); }
            counts.append(variant);
            variants.push(variant);
        }
    }
    addSeparator();
    addItem('coverage', 'left', 50);
    addSeparator();
    addItem('filter', 'left', 40);
    const filterCompanion = new FakeElement(document, 'filter-companion', '', { 'data-statusbar-companion': 'filter' });
    companions.push(filterCompanion);
    addSeparator();
    const group = addItem('group', 'left', 70);
    const groupLabel = new FakeElement(document, 'group-label', 'status-bar-order-label', { 'data-statusbar-floor': '8' });
    groupLabel.font = '600 12px Test';
    groupLabel.scrollWidth = 200;
    groupLabel.clientWidth = 200;
    groupLabel.parentElement = group;
    group.append(groupLabel);
    floors.push(groupLabel);
    truncatable.push(groupLabel);
    addSeparator();
    addItem('sort', 'left', 70);
    addSeparator();
    const memory = addItem('memory', 'right', 50);
    memory.dataset.statusbarMemoryLevel = memoryLevel;
    for (const [name, width, active] of [['memory-full', 100, true], ['memory-level', 40, false]]) {
        const variant = new FakeElement(document, name, '', { 'data-variant': name });
        variant.dataset.variant = name;
        variant.width = width;
        if (active) { variant.setAttribute('data-active', ''); }
        variants.push(variant);
        memory.append(variant);
    }
    const memoryCompanion = new FakeElement(document, 'memory-companion', '', { 'data-statusbar-companion': 'memory' });
    companions.push(memoryCompanion);
    const resolver = addItem('resolver', 'right', 80);
    const resolverLabel = new FakeElement(document, 'resolver-label', 'status-bar-resolver', { 'data-statusbar-floor': '12' });
    resolverLabel.scrollWidth = 150;
    resolverLabel.clientWidth = 150;
    resolverLabel.parentElement = resolver;
    resolverLabel.font = '400 12px Test';
    resolver.append(resolverLabel);
    floors.push(resolverLabel);
    truncatable.push(resolverLabel);
    addItem('loading', 'right', 65);
    addItem('live', 'right', 80);
    addItem('new-events', 'right', 90);
    const failed = addItem('failed', 'right', 70);
    for (const [name, width, active] of [['failed-full', 70, true], ['failed-compact', 35, false]]) {
        const variant = new FakeElement(document, name, '', { 'data-variant': name });
        variant.dataset.variant = name;
        variant.width = width;
        if (active) { variant.setAttribute('data-active', ''); }
        variants.push(variant);
        failed.append(variant);
    }
    const buffer = addItem('buffer', 'right', 70);
    for (const [name, width, active] of [['buffer-full', 70, true], ['buffer-compact', 20, false]]) {
        const variant = new FakeElement(document, name, '', { 'data-variant': name });
        variant.dataset.variant = name;
        variant.width = width;
        if (active) { variant.setAttribute('data-active', ''); }
        variants.push(variant);
        buffer.append(variant);
    }

    for (const key of ['failed', 'buffer']) {
        companions.push(new FakeElement(document, `${key}-companion`, '', {
            'data-statusbar-companion': key, 'data-statusbar-companion-variant': `${key}-compact`, hidden: ''
        }));
    }

    for (const key of ['source', 'coverage', ...(counts ? ['counts'] : []), 'filter', 'sort', 'group', 'memory', 'loading', 'failed', 'resolver', 'live', 'buffer', 'new-events']) {
        descriptions.push(new FakeElement(document, `hidden-${key}`, '', { 'data-hidden-description': key, id: `hidden-${key}`, hidden: '' }));
    }
    for (const [id, value] of [['statusbar-stats-hint', 'Show statistics'], ['statusbar-hidden-label', 'Hidden: '], ['statusbar-hidden-separator', '; ']]) {
        const element = new FakeElement(document, id);
        element.id = id;
        element.textContent = value;
        document.elementsById.set(id, element);
    }
    for (const description of descriptions) {
        description.textContent = description.dataset.hiddenDescription;
        document.elementsById.set(description.id, description);
    }

    content.measureWidth = () => items.reduce((total, item) => {
        if (item.hasAttribute('data-statusbar-hidden')) { return total; }
        const selectedVariant = item.querySelectorAll('[data-variant][data-active]')[0];
        return total + (selectedVariant?.width ?? itemWidths.get(item.dataset.statusbarItem) ?? 0);
    }, 0) + separators.filter(separator => !separator.hasAttribute('data-statusbar-separator-hidden')).length * 5;
    document.previousElement = element => {
        const index = sequence.indexOf(element);
        return index > 0 ? sequence[index - 1] : null;
    };
    document.nextElement = element => {
        const index = sequence.indexOf(element);
        return index >= 0 && index < sequence.length - 1 ? sequence[index + 1] : null;
    };

    return {
        content, descriptions, document, failed, footer, fontSentinel, items, memory,
        resizeObserver: FakeResizeObserver.instances[0], separators, source, sourceLabel, trigger, variants,
        start() {
            attach(content);
            return { resizeObserver: FakeResizeObserver.instances.at(-1), mutationObserver: FakeMutationObserver.instance };
        }
    };
}

let truncatable = [];

function fixture(options) {
    truncatable = [];
    return createFixture(options);
}

function hiddenOrder(document) {
    return document.sequence.filter(value => value.startsWith('hide:')).map(value => value.slice(5));
}

test('The fit pass applies v4 degradation order and never hides counts or the active trigger', () => {
    const layout = fixture({ memoryLevel: 'elevated', triggerVisible: true });
    layout.content.clientWidth = 0;
    layout.content.forcedScrollWidth = 1000;
    layout.start();

    assert.deepEqual(hiddenOrder(layout.document), [
        'resolver', 'coverage', 'filter', 'group', 'sort', 'loading', 'live', 'new-events',
        'source', 'buffer', 'failed', 'memory'
    ]);
    assert.equal(layout.items.find(item => item.dataset.statusbarItem === 'counts').hasAttribute('data-statusbar-hidden'), false);
    assert.equal(layout.trigger.hasAttribute('data-statusbar-hidden'), false);
    assert.equal(layout.document.activeElement, layout.document.body);
    assert.equal(layout.items.find(item => item.dataset.statusbarItem === 'counts').getAttribute('aria-describedby'),
        'statusbar-stats-hint statusbar-hidden-label ' + layout.descriptions.map(item => item.id).join(' statusbar-hidden-separator '));
    detach(layout.content);
});

for (const key of ['failed', 'buffer']) {
    test(`The ${key} companion is exposed only for its visible compact variant`, () => {
        const layout = fixture({ memoryLevel: 'elevated' });
        const companion = layout.content.querySelectorAll(`[data-statusbar-companion="${key}"]`)[0];
        const item = layout.items.find(element => element.dataset.statusbarItem === key);
        layout.content.clientWidth = 2000;
        layout.document.writingText = true;
        const { mutationObserver } = layout.start();

        assert.equal(companion.hasAttribute('hidden'), true);
        layout.content.clientWidth = 125;
        mutationObserver.fire();
        assert.equal(item.hasAttribute('data-statusbar-hidden'), false);
        assert.equal(item.querySelectorAll('[data-variant][data-active]')[0].dataset.variant, `${key}-compact`);
        assert.equal(companion.hasAttribute('hidden'), false);

        layout.content.clientWidth = 0;
        mutationObserver.fire();
        assert.equal(item.hasAttribute('data-statusbar-hidden'), true);
        assert.equal(companion.hasAttribute('hidden'), true);

        layout.content.clientWidth = 2000;
        mutationObserver.fire();
        assert.equal(item.hasAttribute('data-statusbar-hidden'), false);
        assert.equal(item.querySelectorAll('[data-variant][data-active]')[0].dataset.variant, `${key}-full`);
        assert.equal(companion.hasAttribute('hidden'), true);
        assert.equal(companion.textWrites, 0);
        detach(layout.content);
    });
}

test('Normal memory hides before resolver and its companion hides with the chip', () => {
    const layout = fixture({ memoryLevel: 'normal' });
    layout.content.clientWidth = 0;
    layout.content.forcedScrollWidth = 1000;
    layout.start();

    assert.deepEqual(hiddenOrder(layout.document).slice(0, 2), ['memory', 'resolver']);
    assert.equal(layout.memory.hasAttribute('data-statusbar-hidden'), true);
    const companion = layout.content.querySelectorAll('[data-statusbar-companion]');
    assert.ok(companion.find(element => element.dataset.statusbarCompanion === 'memory').hasAttribute('hidden'));
    detach(layout.content);
});

test('Shrink floors use the smaller of natural text width and the configured ch floor', () => {
    const layout = fixture();
    layout.start();

    assert.equal(layout.sourceLabel.style.minWidth, '60px');
    assert.equal(layout.content.querySelectorAll('[data-statusbar-floor]')[1].style.minWidth, '80px');
    assert.equal(layout.content.querySelectorAll('[data-statusbar-floor]')[2].style.minWidth, '120px');
    detach(layout.content);
});

test('Shrink floors are remeasured without the previous minimum when label text becomes shorter', () => {
    const layout = fixture();
    let naturalWidth = 200;
    Object.defineProperty(layout.sourceLabel, 'scrollWidth', {
        get() { return Math.max(naturalWidth, Number.parseFloat(layout.sourceLabel.style.minWidth) || 0); }
    });
    const { mutationObserver } = layout.start();
    assert.equal(layout.sourceLabel.style.minWidth, '120px');

    naturalWidth = 30;
    layout.sourceLabel.textContent = 'Log';
    mutationObserver.fire();

    assert.equal(layout.sourceLabel.style.minWidth, '30px');
    detach(layout.content);
});

test('A focused item moves focus to counts before it is hidden', () => {
    const layout = fixture();
    layout.content.clientWidth = 0;
    layout.content.forcedScrollWidth = 1000;
    layout.sourceLabel.setAttribute('tabindex', '0');
    layout.document.activeElement = layout.sourceLabel;
    layout.start();

    const focusIndex = layout.document.sequence.indexOf('focus:counts');
    const hideIndex = layout.document.sequence.indexOf('set:source:data-statusbar-hidden');
    assert.ok(focusIndex >= 0 && focusIndex < hideIndex);
    detach(layout.content);
});

test('A focused item without a table uses the active trigger before the footer', () => {
    const layout = fixture({ includeCounts: false, triggerVisible: true });
    layout.content.clientWidth = 0;
    layout.content.forcedScrollWidth = 1000;
    layout.document.activeElement = layout.sourceLabel;
    layout.start();

    assert.ok(layout.document.sequence.indexOf('focus:notification-trigger') < layout.document.sequence.indexOf('set:source:data-statusbar-hidden'));
    assert.notEqual(layout.document.activeElement, layout.footer);
    detach(layout.content);
});

test('A focused item with no table or active trigger falls back to the footer', () => {
    const layout = fixture({ includeCounts: false, triggerVisible: false });
    layout.content.clientWidth = 0;
    layout.content.forcedScrollWidth = 1000;
    layout.document.activeElement = layout.sourceLabel;
    layout.start();

    assert.ok(layout.document.sequence.indexOf('focus:footer') < layout.document.sequence.indexOf('set:source:data-statusbar-hidden'));
    detach(layout.content);
});

test('The first visible right item receives auto margin and separators adjacent to hidden items are removed', () => {
    const layout = fixture({ memoryLevel: 'normal' });
    layout.content.clientWidth = 60;
    layout.content.measureWidth = () => layout.items
        .filter(item => !item.hasAttribute('data-statusbar-hidden'))
        .reduce((width, item) => width + (item.dataset.statusbarItem === 'memory' ? 100 : 20), 0)
        + layout.separators.filter(separator => !separator.hasAttribute('data-statusbar-separator-hidden')).length * 5;
    layout.start();

    assert.equal(layout.memory.hasAttribute('data-statusbar-hidden'), true);
    const firstRight = layout.items.find(item => item.dataset.statusbarSide === 'right' && !item.hasAttribute('data-statusbar-hidden'));
    assert.equal(firstRight?.hasAttribute('data-statusbar-right-first'), true);
    assert.equal(layout.separators[0].hasAttribute('data-statusbar-separator-hidden'), true);
    assert.equal(layout.separators.at(-1).hasAttribute('data-statusbar-separator-hidden'), true);
    detach(layout.content);
});

test('The fit pass changes attributes only and the mutation callback drains records after fitting', () => {
    const layout = fixture();
    const { mutationObserver } = layout.start();
    const totalTextWrites = [...layout.items, ...layout.variants, ...layout.descriptions].reduce((count, element) => count + element.textWrites, 0);
    layout.document.writingText = true;
    mutationObserver.fire('characterData');

    assert.equal([...layout.items, ...layout.variants, ...layout.descriptions].reduce((count, element) => count + element.textWrites, 0), totalTextWrites);
    assert.equal(mutationObserver.takeRecordsCount, 1);
    assert.deepEqual(mutationObserver.options, { childList: true, characterData: true, subtree: true });
    detach(layout.content);
});

test('Only the sizing and font-metrics sentinels are observed, and letter-spacing changes rerun the pass', () => {
    const layout = fixture();
    const { resizeObserver } = layout.start();

    assert.deepEqual([...resizeObserver.targets], [layout.content.querySelector('.status-bar-sizing-sentinel'), layout.fontSentinel]);
    const readsBefore = layout.content.scrollWidthReads;
    layout.fontSentinel.letterSpacing = '2px';
    layout.fontSentinel.rectangle.width = 118;
    resizeObserver.fire(layout.fontSentinel);
    assert.ok(layout.content.scrollWidthReads > readsBefore);
    assert.equal([...resizeObserver.targets].some(target => layout.items.includes(target)), false);
    detach(layout.content);
});

test('The zero-block sizing sentinel ignores block-size-only notifications', () => {
    const layout = fixture();
    const { resizeObserver } = layout.start();
    const sizingSentinel = layout.content.querySelector('.status-bar-sizing-sentinel');
    resizeObserver.callback([{ target: sizingSentinel, contentBoxSize: [{ inlineSize: 400, blockSize: 0 }] }]);
    const readsAfterInlineChange = layout.content.scrollWidthReads;
    resizeObserver.callback([{ target: sizingSentinel, contentBoxSize: [{ inlineSize: 400, blockSize: 2 }] }]);

    assert.ok(readsAfterInlineChange > 0);
    assert.equal(layout.content.scrollWidthReads, readsAfterInlineChange);
    detach(layout.content);
});

test('Hidden descriptions compose the stats hint and hidden values into attributes only', () => {
    const layout = fixture();
    layout.content.clientWidth = 0;
    layout.content.forcedScrollWidth = 1000;
    layout.start();
    const counts = layout.items.find(item => item.dataset.statusbarItem === 'counts');

    assert.equal(counts.getAttribute('aria-describedby').startsWith('statusbar-stats-hint statusbar-hidden-label'), true);
    assert.equal(counts.getAttribute('data-tooltip').startsWith('Show statistics; Hidden: '), true);
    assert.equal(counts.getAttribute('data-tooltip').includes('source; '), true);
    assert.equal(counts.getAttribute('data-tooltip').includes('memory; '), true);
    assert.equal(counts.getAttribute('data-tooltip').endsWith('; '), false);
    assert.equal(counts.getAttribute('data-tooltip'), 'Show statistics; Hidden: ' + layout.descriptions.map(item => item.textContent).join('; '));
    detach(layout.content);
});

test('The spoken hidden-item description references the localized separator between items without changing text nodes', () => {
    const layout = fixture();
    layout.content.dataset.hiddenItemSeparator = ' / ';
    layout.document.getElementById('statusbar-hidden-separator').textContent = '/';
    const fullWidth = layout.content.measureWidth();
    layout.content.clientWidth = 0;
    layout.document.writingText = true;
    const { mutationObserver } = layout.start();
    const counts = layout.items.find(item => item.dataset.statusbarItem === 'counts');
    const spokenDescription = () => counts.getAttribute('aria-describedby').split(' ')
        .map(id => layout.document.getElementById(id).textContent.trim())
        .join(' ');

    assert.equal(spokenDescription(), 'Show statistics Hidden: ' + layout.descriptions.map(item => item.textContent).join(' / '));
    assert.equal(counts.getAttribute('data-tooltip'), 'Show statistics; Hidden: ' + layout.descriptions.map(item => item.textContent).join(' / '));
    assert.equal(counts.getAttribute('aria-describedby').endsWith('statusbar-hidden-separator'), false);

    layout.content.clientWidth = fullWidth - 50;
    mutationObserver.fire();
    assert.equal(spokenDescription(), 'Show statistics Hidden: memory');
    assert.equal(counts.getAttribute('data-tooltip'), 'Show statistics; Hidden: memory');

    layout.content.clientWidth = 2000;
    mutationObserver.fire();
    assert.equal(spokenDescription(), 'Show statistics');
    assert.ok(layout.descriptions.every(description => description.textWrites === 0));
    detach(layout.content);
});

test('An empty notification slot has no width or margin and only its trigger is display-none', async () => {
    const css = await readFile(new URL('../../src/EventLogExpert.UI/Banner/NotificationCenter.razor.css', import.meta.url), 'utf8');
    const emptySlot = css.match(/\.notification-center-statusbar\s*\{([^}]*)\}/)?.[1] ?? '';
    const shownSlot = css.match(/\.notification-center-statusbar\[data-trigger-shown="true"\]\s*\{([^}]*)\}/)?.[1] ?? '';
    const retiredTrigger = css.match(/\.notification-trigger-hidden\s*\{([^}]*)\}/)?.[1] ?? '';

    assert.match(emptySlot, /width:\s*0;/);
    assert.match(emptySlot, /margin-inline-start:\s*0;/);
    assert.doesNotMatch(emptySlot, /display:\s*none/);
    assert.match(shownSlot, /width:\s*3\.75rem;/);
    assert.match(shownSlot, /margin-inline-start:\s*8px;/);
    assert.match(retiredTrigger, /display:\s*none;/);
});

test('Inactive variants are display-none in CSS and the font sentinel stays clipped from row overflow', async () => {
    const css = await readFile(new URL('../../src/EventLogExpert.UI/StatusBar/StatusBar.razor.css', import.meta.url), 'utf8');
    assert.match(css, /\[data-variant\]:not\(\[data-active\]\)[\s\S]*?display:\s*none/);
    assert.match(css, /\.status-bar-font-sentinel-wrapper[\s\S]*?inline-size:\s*0[\s\S]*?block-size:\s*0[\s\S]*?overflow:\s*clip/);
    const notificationCss = await readFile(new URL('../../src/EventLogExpert.UI/Banner/NotificationCenter.razor.css', import.meta.url), 'utf8');
    assert.match(notificationCss, /\.notification-center-statusbar\s*\{[^}]*width:\s*0;[^}]*margin-inline-start:\s*0;/);
    assert.match(notificationCss, /\.notification-center-statusbar\[data-trigger-shown="true"\][^}]*width:\s*3\.75rem;[^}]*margin-inline-start:\s*8px;/);
    assert.match(notificationCss, /\.notification-trigger-hidden\s*\{\s*display:\s*none;/);
    assert.doesNotMatch(css, /line-height\s*:/);
});

test('Focus retirement ignores an earlier modal trigger and uses the status-bar trigger', () => {
    const layout = fixture({ includeCounts: false, triggerVisible: true });
    const modalTrigger = new FakeElement(layout.document, 'modal-trigger');
    layout.document.querySelector = () => modalTrigger;
    layout.document.activeElement = layout.sourceLabel;
    layout.content.clientWidth = 0;

    layout.start();

    assert.equal(layout.document.activeElement, layout.trigger);
    detach(layout.content);
});

for (const unavailable of ['disabled', 'inert', 'aria-hidden', 'detached']) {
    test(`Focus retirement falls back to the footer for an ${unavailable} trigger`, () => {
        const layout = fixture({ includeCounts: false, triggerVisible: true });
        if (unavailable === 'disabled') { layout.trigger.disabled = true; }
        if (unavailable === 'inert') { layout.trigger.inert = true; }
        if (unavailable === 'aria-hidden') { layout.trigger.setAttribute('aria-hidden', 'true'); }
        if (unavailable === 'detached') { layout.trigger.isConnected = false; }
        layout.document.activeElement = layout.sourceLabel;
        layout.content.clientWidth = 0;

        layout.start();

        assert.equal(layout.document.activeElement, layout.footer);
        detach(layout.content);
    });
}

test('Repeated synchronous fits reuse the canvas and font measurements until font metrics change', () => {
    const layout = fixture();
    layout.content.clientWidth = 10000;
    const { mutationObserver, resizeObserver } = layout.start();
    const initialStyleReads = layout.document.styleReads;
    const initialMeasurements = layout.document.measurements;

    mutationObserver.fire();
    mutationObserver.fire();

    assert.equal(layout.document.canvasCount, 1);
    assert.equal(layout.document.styleReads, initialStyleReads);
    assert.equal(layout.document.measurements, initialMeasurements);
    layout.document.zeroWidth = 12;
    layout.fontSentinel.rectangle.width += 10;
    resizeObserver.callback([
        { target: layout.content.querySelector('.status-bar-sizing-sentinel'), contentBoxSize: [{ inlineSize: 400 }] },
        { target: layout.fontSentinel }
    ]);
    assert.equal(layout.content.querySelectorAll('[data-statusbar-floor]')[1].style.minWidth, '96px');
    assert.ok(layout.document.measurements > initialMeasurements);
    const afterResize = layout.document.measurements;
    layout.document.fonts.dispatchEvent(new Event('loadingdone'));
    assert.ok(layout.document.measurements > afterResize);
    detach(layout.content);
});

test('A collapsed compact notification slot and empty modal header have no reserved chrome', async () => {
    const root = new URL('../../src/EventLogExpert.UI/', import.meta.url);
    const notificationCss = await readFile(new URL('Banner/NotificationCenter.razor.css', root), 'utf8');
    const modalCss = await readFile(new URL('Modal/ModalChrome.razor.css', root), 'utf8');
    assert.match(notificationCss, /\.notification-center-compact\[data-trigger-shown="false"\]\s*\{[^}]*width:\s*0;[^}]*min-width:\s*0;[^}]*flex-basis:\s*0;/);
    assert.match(modalCss, /\.dialog-header-collapsed\s*\{[^}]*display:\s*flex;/);
});
