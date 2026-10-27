// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

const registrations = new WeakMap();
const triggerByElement = new WeakMap();
const toastRegistrations = new WeakMap();
const VIEWPORT_GAP = 8;
const MIN_TOAST_HEIGHT = 64;
const MAX_TOAST_HEIGHT = 192;

export function normalizeReturnTarget(target) {
    if (!(target instanceof HTMLElement)) {
        return null;
    }

    const notificationTrigger = target.closest('[data-notification-trigger]') ?? triggerByElement.get(target);
    const returnTriggerId = target.closest('[data-notification-return-trigger]')?.dataset.notificationReturnTrigger;
    const mappedReturnTrigger = returnTriggerId ? document.getElementById(returnTriggerId) : null;
    const notificationPanel = target.closest('[data-notification-panel]');
    const trigger = mappedReturnTrigger ?? notificationTrigger ?? registrations.get(notificationPanel)?.trigger;
    if (trigger instanceof HTMLElement) {
        return isUsableTrigger(trigger) ? trigger : getSafeFocusTarget(trigger);
    }

    return target;
}

export function attach(trigger, panel, dotNetReference, toast) {
    detach(panel);
    const controller = new AbortController();
    const options = { signal: controller.signal };
    const isOpen = () => panel.matches(':popover-open');
    let lastFocusedElement = null;
    let lastFocusedCardIndex = -1;
    let dismissalDispatchedBeforeToggle = false;
    let escapeLatched = false;
    let dismissActivationKey = null;
    let toastDesired = false;
    let disposed = false;
    let clearFocusGeneration = 0;
    let pendingClearFocus = null;
    let pendingAnimationFrame = null;
    const scheduleReposition = () => {
        if (disposed || (!isOpen() && !toastDesired) || pendingAnimationFrame !== null) {
            return;
        }
        pendingAnimationFrame = requestAnimationFrame(() => {
            pendingAnimationFrame = null;
            if (disposed) {
                return;
            }
            positionPanel();
            if (toastDesired) {
                positionDesiredToast(toast, registration);
            } else {
                setToastVisible(toast, false);
            }
        });
    };

    trigger.setAttribute('aria-expanded', 'false');
    const mapReturnTargets = root => {
        if (root instanceof HTMLElement) {
            triggerByElement.set(root, trigger);
        }
        for (const element of root.querySelectorAll('*')) {
            if (element instanceof HTMLElement) {
                triggerByElement.set(element, trigger);
            }
        }
    };
    mapReturnTargets(panel);

    const focusTrigger = () => {
        if (isUsableTrigger(trigger)) {
            trigger.focus({ preventScroll: true });
        }
    };
    const close = restoreFocus => {
        if (!isOpen()) {
            return;
        }
        const shouldRestoreFocus = restoreFocus && panel.contains(document.activeElement);
        if (shouldRestoreFocus) {
            focusTrigger();
        }
        document.dispatchEvent(new Event('focus-tooltip:dismiss'));
        dismissalDispatchedBeforeToggle = true;
        panel.hidePopover();
    };
    const positionPanel = () => {
        if (!isOpen()) {
            return;
        }
        const bounds = trigger.getBoundingClientRect();
        const spaceBelow = Math.max(0, window.innerHeight - bounds.bottom);
        const spaceAbove = Math.max(0, bounds.top);
        const placeAbove = spaceAbove > spaceBelow;
        const maximumHeight = Math.max(0, Math.max(spaceAbove, spaceBelow) - VIEWPORT_GAP * 2);
        panel.style.maxHeight = `min(36rem, ${maximumHeight}px)`;
        const panelBounds = panel.getBoundingClientRect();
        panel.style.left = `${Math.max(VIEWPORT_GAP,
            Math.min(bounds.right - panelBounds.width, window.innerWidth - panelBounds.width - VIEWPORT_GAP))}px`;
        panel.style.top = `${Math.max(VIEWPORT_GAP, placeAbove
            ? bounds.top - panelBounds.height - VIEWPORT_GAP
            : bounds.bottom + VIEWPORT_GAP)}px`;
    };
    const focusCard = card => {
        const target = card.matches('.notification-card-progress') ? null
            : card.querySelector('button:not(:disabled), a[href], [tabindex]:not([tabindex="-1"])');
        (target instanceof HTMLElement ? target : card).focus({ preventScroll: true });
    };
    const preserveFocusAfterMutation = () => {
        const activeElement = document.activeElement;
        const focusLost = !activeElement || activeElement === document.body
            || activeElement === document.documentElement || panel.contains(activeElement);
        if (!isOpen() || !focusLost || !(lastFocusedElement instanceof HTMLElement)
            || (lastFocusedElement.isConnected && !lastFocusedElement.matches(':disabled')
                && !lastFocusedElement.closest('[inert]'))) {
            return;
        }

        const cards = [...panel.querySelectorAll('[data-notification-card]')];
        const focusedCard = lastFocusedElement.closest('[data-notification-card]');
        const currentIndex = cards.indexOf(focusedCard);
        const nextIndex = currentIndex >= 0 ? currentIndex + 1 : lastFocusedCardIndex;
        if (nextIndex >= 0 && nextIndex < cards.length) {
            focusCard(cards[nextIndex]);
            return;
        }

        const previousIndex = currentIndex >= 0 ? currentIndex - 1 : lastFocusedCardIndex - 1;
        if (previousIndex >= 0 && previousIndex < cards.length) {
            focusCard(cards[previousIndex]);
            return;
        }

        panel.focus({ preventScroll: true });
    };
    const onEscapeKeyDown = event => {
        if (event.key !== 'Escape') {
            return;
        }
        if (escapeLatched) {
            event.preventDefault();
            return;
        }
        if (!isOpen() || event.defaultPrevented) {
            return;
        }

        event.preventDefault();
        escapeLatched = true;
        close(true);
    };
    const onDismissKeyDown = event => {
        const activationKey = event.key === ' ' || event.code === 'Space' ? ' ' : event.key;
        if (dismissActivationKey === activationKey) {
            event.preventDefault();
            return;
        }
        if ((activationKey === 'Enter' || activationKey === ' ')
            && event.target instanceof Element
            && (event.target.closest('[data-notification-card] .banner-dismiss')
                || event.target.closest('[data-notification-clear-all]'))) {
            dismissActivationKey = activationKey;
        }
    };
    const onKeyUp = event => {
        if (event.key === 'Escape') {
            escapeLatched = false;
        }
        const activationKey = event.key === ' ' || event.code === 'Space' ? ' ' : event.key;
        if (activationKey === dismissActivationKey) {
            dismissActivationKey = null;
        }
    };
    const onToggle = () => {
        if (disposed) { return; }

        const open = isOpen();
        trigger.setAttribute('aria-expanded', String(open));
        if (open) {
            dismissalDispatchedBeforeToggle = false;
            toastDesired = false;
            setToastVisible(toast, false);
            document.dispatchEvent(new Event('focus-tooltip:dismiss'));
            positionPanel();
            panel.style.visibility = '';
            scheduleReposition();
            panel.focus({ preventScroll: true });
        } else if (!dismissalDispatchedBeforeToggle) {
            document.dispatchEvent(new Event('focus-tooltip:dismiss'));
        }
        dismissalDispatchedBeforeToggle = false;
        if (dotNetReference) {
            dotNetReference.invokeMethodAsync('OnPopoverToggle', open).catch(() => {});
        }
    };

    panel.addEventListener('beforetoggle', event => {
        if (event.newState !== 'open') {
            return;
        }
        if (trigger.disabled || trigger.closest('[inert]')) {
            event.preventDefault();
            return;
        }
        // Chromium lays out the first open frame before 'toggle' fires; keep it unpainted until positioned.
        panel.style.visibility = 'hidden';
        toastDesired = false;
        setToastVisible(toast, false);
    }, options);
    panel.addEventListener('toggle', onToggle, options);
    document.addEventListener('keydown', onEscapeKeyDown, options);
    document.addEventListener('keydown', onDismissKeyDown, { ...options, capture: true });
    document.addEventListener('keyup', onKeyUp, options);
    window.addEventListener('blur', () => {
        escapeLatched = false;
        dismissActivationKey = null;
    }, options);
    panel.addEventListener('pointerdown', event => {
        if (!(event.target instanceof Element)
            || !event.target.closest('button, a, input, select, textarea, [contenteditable="true"]')) {
            panel.focus({ preventScroll: true });
        }
    }, options);
    panel.addEventListener('focusin', event => {
        lastFocusedElement = event.target;
        const card = event.target instanceof Element ? event.target.closest('[data-notification-card]') : null;
        lastFocusedCardIndex = card ? [...panel.querySelectorAll('[data-notification-card]')].indexOf(card) : -1;
    }, options);
    document.addEventListener('focusin', event => {
        if (event.target && event.target !== document.body && event.target !== document.documentElement
            && !trigger.contains(event.target) && !panel.contains(event.target)) {
            close(false);
        }
        scheduleReposition();
    }, options);
    window.addEventListener('resize', scheduleReposition, options);
    const mutationObserver = new MutationObserver(mutations => {
        if (disposed) {
            return;
        }
        for (const mutation of mutations) {
            for (const addedNode of mutation.addedNodes) {
                if (addedNode instanceof HTMLElement) {
                    mapReturnTargets(addedNode);
                }
            }
        }
        preserveFocusAfterMutation();
        if (trigger.disabled || trigger.closest('[inert]')) {
            close(false);
        }
        scheduleReposition();
    });
    mutationObserver.observe(panel, {
        attributes: true,
        attributeFilter: ['disabled', 'inert'],
        childList: true,
        subtree: true
    });
    const isClearFocusLive = action => {
        if (disposed || registrations.get(panel) !== action.registration
            || action.generation !== clearFocusGeneration || !panel.isConnected || !trigger.isConnected
            || (action.ownDialog && (!action.ownDialog.isConnected || !action.ownDialog.open))) {
            return false;
        }
        if ([...document.querySelectorAll('dialog:modal')].some(dialog => dialog !== action.ownDialog)) {
            return false;
        }
        const activeDialog = document.activeElement?.closest('dialog[open]');
        return !activeDialog || !activeDialog.isConnected || activeDialog === action.ownDialog;
    };
    const drainClearFocus = () => {
        const action = pendingClearFocus;
        if (!action) { return; }
        if (!isClearFocusLive(action)) {
            pendingClearFocus = null;
            action.resolve(false);
            return;
        }
        if (trigger.disabled || trigger.closest('[inert]')) { return; }

        pendingClearFocus = null;
        const focusTarget = action.outcome === 'empty' || !isUsableTrigger(trigger)
            ? getSafeFocusTarget(trigger) : trigger;
        focusTarget?.focus({ preventScroll: true });
        close(false);
        action.resolve(true);
    };
    const availabilityObserver = new MutationObserver(() => {
        drainClearFocus();
        if (trigger.disabled || trigger.closest('[inert]')) {
            close(false);
        }
        scheduleReposition();
    });
    availabilityObserver.observe(trigger, { attributes: true, attributeFilter: ['disabled'] });
    const header = trigger.closest('header');
    if (header) {
        availabilityObserver.observe(header, { attributes: true, attributeFilter: ['inert'] });
    }
    const resizeObserver = new ResizeObserver(scheduleReposition);
    resizeObserver.observe(panel);
    if (toast instanceof HTMLElement) {
        resizeObserver.observe(toast);
    }
    const containingDialog = trigger.closest('dialog');
    const workspace = containingDialog ? null : document.querySelector('main.page-content');
    let filterPane = null;
    const filterObserver = new MutationObserver(scheduleReposition);
    const rebindFilterObserver = () => {
        if (disposed) {
            return;
        }
        const currentFilterPane = workspace?.querySelector('.filter-pane') ?? null;
        if (currentFilterPane === filterPane) {
            return;
        }
        filterObserver.disconnect();
        filterPane = currentFilterPane;
        if (filterPane instanceof HTMLElement) {
            filterObserver.observe(filterPane, {
                childList: true,
                subtree: true,
                characterData: true,
                attributes: true
            });
        }
        scheduleReposition();
    };
    const workspaceObserver = new MutationObserver(rebindFilterObserver);
    if (containingDialog instanceof HTMLElement) {
        resizeObserver.observe(containingDialog);
    } else {
        const statsDrawer = document.querySelector('.stats-drawer');
        if (statsDrawer instanceof HTMLElement) {
            resizeObserver.observe(statsDrawer);
        }
        if (workspace instanceof HTMLElement) {
            workspaceObserver.observe(workspace, { childList: true, subtree: true });
            workspace.addEventListener('scroll', scheduleReposition, { ...options, passive: true });
            rebindFilterObserver();
        }
    }
    const registration = {
        availabilityObserver,
        close,
        panel,
        controller,
        focusTrigger,
        filterObserver,
        workspaceObserver,
        workspace,
        scheduleReposition,
        completeClearFocus(outcome) {
            pendingClearFocus?.resolve(false);
            pendingClearFocus = null;
            const generation = ++clearFocusGeneration;
            return new Promise(resolve => {
                pendingClearFocus = {
                    registration, generation, ownDialog: trigger.closest('dialog'), outcome, resolve
                };
                drainClearFocus();
            });
        },
        dispose() {
            disposed = true;
            clearFocusGeneration++;
            pendingClearFocus?.resolve(false);
            pendingClearFocus = null;
            if (pendingAnimationFrame !== null) {
                cancelAnimationFrame(pendingAnimationFrame);
                pendingAnimationFrame = null;
            }
        },
        mutationObserver,
        resizeObserver,
        toast,
        trigger,
        get toastDesired() { return toastDesired; },
        set toastDesired(value) { toastDesired = value; }
    };
    registrations.set(panel, registration);
    if (toast instanceof HTMLElement) {
        toastRegistrations.set(toast, registration);
    }
}

export function completeClearFocus(panel, outcome) {
    return registrations.get(panel)?.completeClearFocus(outcome);
}

function isUsableTrigger(trigger) {
    return trigger.isConnected && trigger.getClientRects().length > 0
        && !trigger.disabled && !trigger.closest('[inert]')
        && trigger.getAttribute('aria-hidden') !== 'true';
}

export function slotChanged(panel) {
    registrations.get(panel)?.scheduleReposition();
}

export function positionToast(toast, shouldShow) {
    if (!(toast instanceof HTMLElement)) {
        return;
    }

    const registration = toastRegistrations.get(toast);
    if (!registration) {
        setToastVisible(toast, false);
        return;
    }

    registration.toastDesired = shouldShow;
    if (!shouldShow || registration.panel?.matches(':popover-open')) {
        setToastVisible(toast, false);
        return;
    }

    registration.scheduleReposition();
}

function positionDesiredToast(toast, registration) {
    if (!(toast instanceof HTMLElement)) {
        return;
    }
    if (registration.panel.matches(':popover-open')) {
        setToastVisible(toast, false);
        return;
    }

    const workspace = registration.workspace;
    const filterPane = workspace?.querySelector('.filter-pane');
    const statusBar = document.querySelector('footer[data-pane="status"]');
    const trigger = registration.trigger;
    if (!(workspace instanceof HTMLElement) || !(statusBar instanceof HTMLElement)
        || !trigger.isConnected || trigger.closest('[inert]')) {
        setToastVisible(toast, false);
        return;
    }

    const upperBoundary = (filterPane instanceof HTMLElement
        ? filterPane.getBoundingClientRect().bottom
        : workspace.getBoundingClientRect().top);
    const statsDrawer = document.querySelector('.stats-drawer');
    const drawerBounds = statsDrawer?.getBoundingClientRect();
    const lowerBoundary = drawerBounds?.height > 0 ? drawerBounds.top : statusBar.getBoundingClientRect().top;
    const availableHeight = lowerBoundary - VIEWPORT_GAP - upperBoundary;
    if (availableHeight < MIN_TOAST_HEIGHT) {
        setToastVisible(toast, false);
        return;
    }

    const triggerBounds = trigger.getBoundingClientRect();
    const direction = getComputedStyle(document.documentElement).direction;
    const inlineEnd = direction === 'rtl'
        ? triggerBounds.left
        : window.innerWidth - triggerBounds.right;
    toast.style.insetInlineEnd = `${Math.max(VIEWPORT_GAP, inlineEnd)}px`;
    toast.style.bottom = `${window.innerHeight - lowerBoundary + VIEWPORT_GAP}px`;
    toast.style.maxHeight = `${Math.min(MAX_TOAST_HEIGHT, availableHeight)}px`;

    const toastBounds = toast.getBoundingClientRect();
    if (fullyCoversFocusedElement(toastBounds, document.activeElement)) {
        setToastVisible(toast, false);
        return;
    }

    setToastVisible(toast, true);
}

export function retireFocusBeforeHide(panel) {
    const registration = registrations.get(panel);
    if (!registration) {
        return;
    }

    const activeElement = document.activeElement;
    if (activeElement === registration.trigger || panel.contains(activeElement)) {
        const focusTarget = getSafeFocusTarget(panel);
        focusTarget?.focus({ preventScroll: true });
    }
    if (registration.panel.matches(':popover-open')) {
        registration.close(false);
    } else {
        document.dispatchEvent(new Event('focus-tooltip:dismiss'));
    }
}

export function setUnavailable(panel, unavailable) {
    if (unavailable) {
        const registration = registrations.get(panel);
        registration?.close(false);
        if (registration?.toast instanceof HTMLElement) {
            registration.toastDesired = false;
            setToastVisible(registration.toast, false);
        }
    }
}

export function prepareModalAction(panel) {
    const registration = registrations.get(panel);
    if (!registration || !panel.matches(':popover-open')) {
        return;
    }
    registration.close(false);
    registration.focusTrigger();
}

export function detach(panel) {
    const registration = registrations.get(panel);
    if (!registration) {
        return;
    }
    registration.dispose();
    registration.controller.abort();
    registration.close(false);
    registration.availabilityObserver.disconnect();
    registration.mutationObserver.disconnect();
    registration.resizeObserver.disconnect();
    registration.filterObserver.disconnect();
    registration.workspaceObserver.disconnect();
    if (registration.toast instanceof HTMLElement) {
        toastRegistrations.delete(registration.toast);
    }
    registrations.delete(panel);
}

function getSafeFocusTarget(panel) {
    const dialog = panel.closest('dialog[open]') ?? [...document.querySelectorAll('dialog[open]')].at(-1);
    if (dialog instanceof HTMLDialogElement) {
        const title = dialog.querySelector('h2[tabindex="-1"]');
        if (title instanceof HTMLElement) {
            return title;
        }
        const body = dialog.querySelector('.dialog-body');
        if (body instanceof HTMLElement) {
            if (!body.hasAttribute('tabindex')) {
                body.tabIndex = -1;
            }
            return body;
        }
        return null;
    }

    const workspace = document.querySelector('[data-notification-focus-fallback]');
    return workspace instanceof HTMLElement ? workspace : null;
}

function fullyCoversFocusedElement(toastBounds, focusedElement) {
    if (!(focusedElement instanceof HTMLElement)
        || focusedElement === document.body
        || focusedElement === document.documentElement
        || !focusedElement.isConnected) {
        return false;
    }

    const focusedBounds = focusedElement.getBoundingClientRect();
    if (focusedBounds.width <= 0 || focusedBounds.height <= 0) {
        return false;
    }
    if (containsRect(toastBounds, focusedBounds)) {
        return true;
    }

    const style = getComputedStyle(focusedElement);
    const outlineWidth = Number.parseFloat(style.outlineWidth) || 0;
    const outlineOffset = Number.parseFloat(style.outlineOffset) || 0;
    if (outlineWidth <= 0 || style.outlineStyle === 'none') {
        return false;
    }

    const indicatorInset = Math.max(0, outlineWidth + outlineOffset);
    const indicatorBounds = {
        left: focusedBounds.left - indicatorInset,
        right: focusedBounds.right + indicatorInset,
        top: focusedBounds.top - indicatorInset,
        bottom: focusedBounds.bottom + indicatorInset
    };
    return containsRect(toastBounds, indicatorBounds);
}

function containsRect(outer, inner) {
    return outer.left <= inner.left
        && outer.right >= inner.right
        && outer.top <= inner.top
        && outer.bottom >= inner.bottom;
}

function setToastVisible(toast, visible) {
    if (toast instanceof HTMLElement) {
        toast.dataset.toastVisible = String(visible);
    }
}
