// Tiny DOM helper for the menu system. Lets the .NET MenuBar anchor each dropdown to the
// bottom-left of its trigger button instead of the cursor position, and tracks the element that
// had focus when a popup opened so MenuHost can restore it on close.
window.getMenuElementRect = (element) => {
    if (!element) {
        return { left: 0, top: 0, right: 0, bottom: 0, width: 0, height: 0 };
    }
    const rect = element.getBoundingClientRect();
    return {
        left: rect.left,
        top: rect.top,
        right: rect.right,
        bottom: rect.bottom,
        width: rect.width,
        height: rect.height,
    };
};

let menuOpenerElement = null;

// Captures whatever HTML element currently holds focus so the menu can restore it on close.
// Skipping <body> avoids restoring focus to the document root, which provides no useful keyboard
// landing spot for users.
window.captureMenuOpener = () => {
    const candidate = document.activeElement;
    menuOpenerElement = (candidate instanceof HTMLElement && candidate !== document.body) ? candidate : null;
};

window.restoreMenuOpenerFocus = () => {
    const target = menuOpenerElement;
    menuOpenerElement = null;

    if (!target || !document.body.contains(target) || typeof target.focus !== "function") {
        return;
    }

    // Defer to next frame so the popup has fully torn down before focus moves; otherwise some
    // browsers re-route focus to <body> after we set it.
    requestAnimationFrame(() => {
        try {
            target.focus({ preventScroll: true });
        } catch { /* element detached between frames */  }
    
    });
};
