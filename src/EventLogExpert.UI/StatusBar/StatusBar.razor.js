// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

import { isUsableFocusTarget } from '../Common/focusGuard.js';
import { isClipped } from '../Common/focusTooltip.js';

const registrations = new WeakMap();
const ITEM_SELECTOR = '[data-statusbar-item]';
const FLOOR_SELECTOR = '[data-statusbar-floor]';
const TRUNCATABLE_SELECTOR = '.status-bar-source, .status-bar-order-label, .status-bar-resolver';
const VARIANT_DEFAULTS = new Map([
    ['counts', 'counts-full'],
    ['memory', 'memory-full'],
    ['failed', 'failed-full'],
    ['buffer', 'buffer-full']
]);
const HIDE_ORDER = [
    'resolver',
    'coverage',
    'filter',
    'group',
    'sort',
    'loading',
    'live',
    'new-events',
    'source'
];

function getItem(content, name) {
    return [...content.querySelectorAll(ITEM_SELECTOR)].find(item => item.dataset.statusbarItem === name) ?? null;
}

function selectVariant(content, group, name) {
    for (const variant of content.querySelectorAll('[data-variant]')) {
        if (variant.dataset.variant.startsWith(`${group}-`)) {
            if (variant.dataset.variant === name) {
                variant.setAttribute('data-active', '');
            } else {
                variant.removeAttribute('data-active');
            }
        }
    }
    for (const companion of content.querySelectorAll(`[data-statusbar-companion="${group}"]`)) {
        if (!companion.dataset.statusbarCompanionVariant) {
            continue;
        }
        if (companion.dataset.statusbarCompanionVariant === name
            && !getItem(content, group)?.hasAttribute('data-statusbar-hidden')) {
            companion.removeAttribute('hidden');
        } else {
            companion.setAttribute('hidden', '');
        }
    }
}

function resetFitState(content) {
    for (const label of content.querySelectorAll(FLOOR_SELECTOR)) {
        label.style.removeProperty('min-width');
    }
    for (const item of content.querySelectorAll(ITEM_SELECTOR)) {
        item.removeAttribute('data-statusbar-hidden');
    }
    for (const companion of content.querySelectorAll('[data-statusbar-companion]')) {
        companion.removeAttribute('hidden');
    }
    for (const separator of content.querySelectorAll('.status-bar-sep')) {
        separator.removeAttribute('data-statusbar-separator-hidden');
    }
    for (const item of content.querySelectorAll('[data-statusbar-right-first]')) {
        item.removeAttribute('data-statusbar-right-first');
    }
    for (const [group, variant] of VARIANT_DEFAULTS) {
        selectVariant(content, group, variant);
    }
}

function applyShrinkFloors(content, registration) {
    for (const label of content.querySelectorAll(FLOOR_SELECTOR)) {
        let zeroWidth = registration.labelZeroWidths.get(label);
        if (zeroWidth === undefined) {
            const font = content.ownerDocument.defaultView.getComputedStyle(label).font;
            zeroWidth = registration.fontZeroWidths.get(font);
            if (zeroWidth === undefined) {
                registration.measurementContext.font = font;
                zeroWidth = registration.measurementContext.measureText('0').width;
                registration.fontZeroWidths.set(font, zeroWidth);
            }
            registration.labelZeroWidths.set(label, zeroWidth);
        }
        const floorWidth = zeroWidth * Number(label.dataset.statusbarFloor);
        label.style.minWidth = `${Math.min(label.scrollWidth, floorWidth)}px`;
    }
}

function invalidateFontMetrics(registration) {
    registration.labelZeroWidths = new WeakMap();
    registration.fontZeroWidths.clear();
}

function isFit(content) {
    return content.scrollWidth <= content.clientWidth;
}

function updateSeparators(content) {
    for (const separator of content.querySelectorAll('.status-bar-sep')) {
        let previousItem = separator.previousElementSibling;
        while (previousItem && !previousItem.matches(ITEM_SELECTOR)) {
            previousItem = previousItem.previousElementSibling;
        }
        let nextItem = separator.nextElementSibling;
        while (nextItem && !nextItem.matches(ITEM_SELECTOR)) {
            nextItem = nextItem.nextElementSibling;
        }

        if (!previousItem || !nextItem
            || previousItem.hasAttribute('data-statusbar-hidden')
            || nextItem.hasAttribute('data-statusbar-hidden')) {
            separator.setAttribute('data-statusbar-separator-hidden', '');
        } else {
            separator.removeAttribute('data-statusbar-separator-hidden');
        }
    }
}

function updateRightAlignment(content) {
    for (const item of content.querySelectorAll('[data-statusbar-side="right"][data-statusbar-right-first]')) {
        item.removeAttribute('data-statusbar-right-first');
    }
    const firstVisibleRightItem = [...content.querySelectorAll(`${ITEM_SELECTOR}[data-statusbar-side="right"]`)]
        .find(item => !item.hasAttribute('data-statusbar-hidden'));
    firstVisibleRightItem?.setAttribute('data-statusbar-right-first', '');
}

function updateHiddenDescription(content) {
    const countsButton = content.querySelector('[data-statusbar-counts]');
    if (!countsButton) {
        return;
    }

    const statsHint = content.ownerDocument.getElementById('statusbar-stats-hint')?.textContent ?? '';
    const hiddenLabel = content.ownerDocument.getElementById('statusbar-hidden-label');
    const descriptions = [...content.querySelectorAll('[data-hidden-description]')];
    const hiddenDescriptions = descriptions.filter(description => {
        const item = getItem(content, description.dataset.hiddenDescription);
        if (!item) {
            return false;
        }
        if (item.hasAttribute('data-statusbar-hidden')) {
            return true;
        }
        const activeVariant = [...item.querySelectorAll('[data-variant][data-active]')]
            .find(variant => variant.dataset.variant !== `${description.dataset.hiddenDescription}-full`
                && !variant.dataset.variant.endsWith('-full'));
        return activeVariant !== undefined || (description.dataset.hiddenDescription === 'counts'
            && !content.querySelector('[data-variant="counts-full"][data-active]'));
    });
    const separatorId = content.ownerDocument.getElementById('statusbar-hidden-separator')?.id;
    const describedIds = ['statusbar-stats-hint'];
    let tooltip = statsHint;
    if (hiddenDescriptions.length > 0 && hiddenLabel) {
        describedIds.push(hiddenLabel.id);
        hiddenDescriptions.forEach((description, index) => {
            if (index > 0 && separatorId) {
                describedIds.push(separatorId);
            }
            describedIds.push(description.id);
        });
        const hiddenValues = hiddenDescriptions.map(description => description.textContent)
            .join(content.dataset.hiddenItemSeparator);
        const formatArguments = [statsHint, hiddenLabel.textContent, hiddenValues];
        tooltip = content.dataset.hiddenTooltipFormat.replace(/\{([012])\}/g,
            (_, index) => formatArguments[Number(index)]);
    }
    countsButton.setAttribute('aria-describedby', describedIds.join(' '));
    countsButton.setAttribute('data-tooltip', tooltip);
}

function moveFocusBeforeHide(content, item) {
    const activeElement = content.ownerDocument.activeElement;
    if (!(activeElement instanceof HTMLElement) || !item.contains(activeElement)) {
        return;
    }

    const countsButton = content.querySelector('[data-statusbar-counts]');
    const footer = content.closest('footer[data-pane="status"]');
    const statusBar = content.closest('.status-bar') ?? footer;
    const notificationTrigger = statusBar?.querySelector('[data-notification-trigger]');
    const focusTarget = [countsButton, notificationTrigger, footer, statusBar].find(isUsableFocusTarget);
    focusTarget?.focus({ preventScroll: true });
}

function hideItem(content, name) {
    const item = getItem(content, name);
    if (!item || item.hasAttribute('data-statusbar-hidden')) {
        return;
    }

    moveFocusBeforeHide(content, item);
    item.setAttribute('data-statusbar-hidden', '');
    for (const companion of content.querySelectorAll(`[data-statusbar-companion="${name}"]`)) {
        companion.setAttribute('hidden', '');
    }
    updateSeparators(content);
    updateRightAlignment(content);
    updateHiddenDescription(content);
}

function updateClippedTabStops(content) {
    for (const span of content.querySelectorAll(TRUNCATABLE_SELECTOR)) {
        const item = span.closest(ITEM_SELECTOR);
        if (item?.hasAttribute('data-statusbar-hidden')) {
            span.removeAttribute('tabindex');
        } else if (isClipped(span)) {
            if (span.getAttribute('tabindex') !== '0') {
                span.setAttribute('tabindex', '0');
            }
        } else if (content.ownerDocument.activeElement !== span) {
            span.removeAttribute('tabindex');
        }
    }
}

function isMemoryNormal(content) {
    return getItem(content, 'memory')?.dataset.statusbarMemoryLevel === 'normal';
}

function runFitPass(content, registration) {
    if (registration.disposed || content.getClientRects().length === 0 || registration.running) {
        return;
    }

    registration.running = true;
    try {
        resetFitState(content);
        applyShrinkFloors(content, registration);
        updateSeparators(content);
        updateRightAlignment(content);
        updateHiddenDescription(content);

        if (!isFit(content)) {
            if (isMemoryNormal(content)) {
                hideItem(content, 'memory');
                if (isFit(content)) { return; }
            }
            for (const itemName of HIDE_ORDER) {
                hideItem(content, itemName);
                if (isFit(content)) { return; }
            }
            selectVariant(content, 'counts', 'counts-shown-total');
            updateHiddenDescription(content);
            if (isFit(content)) { return; }
            selectVariant(content, 'counts', 'counts-total');
            updateHiddenDescription(content);
            if (isFit(content)) { return; }
            if (!isMemoryNormal(content) && getItem(content, 'memory')) {
                selectVariant(content, 'memory', 'memory-level');
                updateHiddenDescription(content);
                if (isFit(content)) { return; }
            }
            selectVariant(content, 'failed', 'failed-compact');
            updateHiddenDescription(content);
            if (isFit(content)) { return; }
            selectVariant(content, 'buffer', 'buffer-compact');
            updateHiddenDescription(content);
            if (isFit(content)) { return; }
            for (const itemName of ['buffer', 'failed', 'memory']) {
                hideItem(content, itemName);
                if (isFit(content)) { return; }
            }
        }
    } finally {
        updateSeparators(content);
        updateRightAlignment(content);
        updateHiddenDescription(content);
        updateClippedTabStops(content);
        registration.running = false;
    }
}

export function attach(content) {
    detach(content);
    const document = content.ownerDocument;
    const sizingSentinel = content.querySelector('.status-bar-sizing-sentinel');
    const fontSentinel = content.querySelector('.status-bar-font-sentinel');
    const registration = {
        disposed: false,
        fontZeroWidths: new Map(),
        labelZeroWidths: new WeakMap(),
        measurementContext: document.createElement('canvas').getContext('2d'),
        running: false,
        resizeObserver: null,
        mutationObserver: null,
        sizingInlineSize: null,
        fontMetricsSize: null
    };
    const resizeObserver = new ResizeObserver(entries => {
        if (registration.disposed) {
            return;
        }
        const changed = entries.reduce((hasChangedEntry, entry) => {
            if (entry.target === sizingSentinel) {
                const inlineSize = entry.contentBoxSize?.[0]?.inlineSize ?? entry.contentRect?.width
                    ?? sizingSentinel.getBoundingClientRect().width;
                const hasChanged = registration.sizingInlineSize !== inlineSize;
                registration.sizingInlineSize = inlineSize;
                return hasChanged || hasChangedEntry;
            }
            if (entry.target === fontSentinel) {
                const inlineSize = entry.contentBoxSize?.[0]?.inlineSize ?? entry.contentRect?.width
                    ?? fontSentinel.getBoundingClientRect().width;
                const blockSize = entry.contentBoxSize?.[0]?.blockSize ?? entry.contentRect?.height
                    ?? fontSentinel.getBoundingClientRect().height;
                const hasChanged = registration.fontMetricsSize?.inlineSize !== inlineSize
                    || registration.fontMetricsSize?.blockSize !== blockSize;
                registration.fontMetricsSize = { inlineSize, blockSize };
                if (hasChanged) { invalidateFontMetrics(registration); }
                return hasChanged || hasChangedEntry;
            }
            return hasChangedEntry;
        }, false);
        if (changed) {
            runFitPass(content, registration);
        }
    });
    registration.resizeObserver = resizeObserver;
    if (sizingSentinel) {
        resizeObserver.observe(sizingSentinel);
    }
    if (fontSentinel) {
        resizeObserver.observe(fontSentinel);
    }
    const mutationObserver = new MutationObserver(records => {
        if (registration.disposed || !records.some(record => record.type === 'childList' || record.type === 'characterData')) {
            return;
        }
        if (records.some(record => record.type === 'childList')) {
            invalidateFontMetrics(registration);
        }
        runFitPass(content, registration);
        mutationObserver.takeRecords();
    });
    registration.mutationObserver = mutationObserver;
    mutationObserver.observe(content, { childList: true, characterData: true, subtree: true });
    const onBlur = event => {
        if (event.target.matches(TRUNCATABLE_SELECTOR) && !isClipped(event.target)) {
            event.target.removeAttribute('tabindex');
        }
    };
    const onFocusOut = () => runFitPass(content, registration);
    content.addEventListener('blur', onBlur, true);
    content.addEventListener('focusout', onFocusOut);
    document.fonts?.addEventListener('loadingdone', onFontsLoaded);
    function onFontsLoaded() {
        invalidateFontMetrics(registration);
        runFitPass(content, registration);
    }

    registrations.set(content, () => {
        registration.disposed = true;
        resizeObserver.disconnect();
        mutationObserver.disconnect();
        document.fonts?.removeEventListener('loadingdone', onFontsLoaded);
        content.removeEventListener('blur', onBlur, true);
        content.removeEventListener('focusout', onFocusOut);
        for (const label of content.querySelectorAll(FLOOR_SELECTOR)) {
            label.style.removeProperty('min-width');
        }
        for (const span of content.querySelectorAll(TRUNCATABLE_SELECTOR)) {
            span.removeAttribute('tabindex');
        }
        resetFitState(content);
        updateHiddenDescription(content);
    });
    runFitPass(content, registration);
}

export function detach(content) {
    registrations.get(content)?.();
    registrations.delete(content);
}

