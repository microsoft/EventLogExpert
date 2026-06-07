// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

let menuOpenerElement = null;

// Skip <body> so we don't restore focus to the document root on close.
export function captureMenuOpener() {
    const candidate = document.activeElement;
    menuOpenerElement = (candidate instanceof HTMLElement && candidate !== document.body) ? candidate : null;
}

export function restoreMenuOpenerFocus() {
    const target = menuOpenerElement;
    menuOpenerElement = null;

    if (!target || !document.body.contains(target) || typeof target.focus !== "function") {
        return;
    }

    // Defer to next frame so the popup tears down before focus moves; some browsers otherwise
    // re-route focus to <body>.
    requestAnimationFrame(() => {
        try {
            target.focus({ preventScroll: true });
        } catch { /* element detached between frames */  }

    });
}

// Positions a fixed-position .submenu next to its parent <li>, flipping left when it would
// clip the right edge and clamping vertically. Hidden until menu-submenu-positioned is set.
export function positionMenuSubmenu(submenu) {
    if (!submenu) { return; }

    const parentLi = submenu.parentElement && submenu.parentElement.closest("li.menu-item");
    if (!parentLi) { return; }

    const margin = 4;
    const parentRect = parentLi.getBoundingClientRect();

    // Hide if the parent has scrolled out of its panel — don't leave the submenu floating.
    // Use inert so the hidden submenu also leaves the AT tree and isn't focusable; if focus
    // was inside, restore it to the parent item before inerting.
    const scrollContainer = parentLi.closest(".menu-host-popup, .submenu");
    if (scrollContainer) {
        const containerRect = scrollContainer.getBoundingClientRect();
        if (parentRect.bottom <= containerRect.top || parentRect.top >= containerRect.bottom) {
            if (submenu.contains(document.activeElement) && typeof parentLi.focus === "function") {
                try { parentLi.focus({ preventScroll: true }); } catch { /* parent detached */ }
            }
            submenu.inert = true;
            submenu.classList.remove("menu-submenu-positioned");
            return;
        }
    }
    submenu.inert = false;

    // Reset stale inline values so we measure natural size before clamping.
    submenu.style.left = "";
    submenu.style.top = "";
    const submenuRect = submenu.getBoundingClientRect();
    const viewportWidth = document.documentElement.clientWidth;
    const viewportHeight = document.documentElement.clientHeight;

    let left = parentRect.right - 2;
    if (left + submenuRect.width > viewportWidth - margin) {
        left = parentRect.left - submenuRect.width + 2;
    }
    if (left < margin) { left = margin; }

    let top = parentRect.top - 5;
    if (top + submenuRect.height > viewportHeight - margin) {
        top = Math.max(margin, viewportHeight - submenuRect.height - margin);
    }
    if (top < margin) { top = margin; }

    submenu.style.left = `${left}px`;
    submenu.style.top = `${top}px`;
    submenu.classList.add("menu-submenu-positioned");
}

// Clamps the top-level popup into the viewport. Measures from the anchored position set by
// MenuHost; only mutates left/top when clamping is actually needed.
export function clampMenuPopup(popup) {
    if (!popup) { return; }

    const margin = 4;
    const popupRect = popup.getBoundingClientRect();
    const viewportWidth = document.documentElement.clientWidth;
    const viewportHeight = document.documentElement.clientHeight;

    let left = popupRect.left;
    if (left + popupRect.width > viewportWidth - margin) {
        left = viewportWidth - popupRect.width - margin;
    }
    if (left < margin) { left = margin; }

    let top = popupRect.top;
    if (top + popupRect.height > viewportHeight - margin) {
        top = viewportHeight - popupRect.height - margin;
    }
    if (top < margin) { top = margin; }

    if (left !== popupRect.left) { popup.style.left = `${left}px`; }
    if (top !== popupRect.top) { popup.style.top = `${top}px`; }
    popup.classList.add("menu-popup-positioned");
}

export function repositionOpenMenuSubmenus() {
    const popups = document.getElementsByClassName("menu-host-popup");
    for (let index = 0; index < popups.length; index++) {
        clampMenuPopup(popups[index]);
    }

    const submenus = document.getElementsByClassName("submenu");
    for (let index = 0; index < submenus.length; index++) {
        positionMenuSubmenu(submenus[index]);
    }
}

let menuViewportListener = null;

// Capture-phase scroll catches inner-element scrolling (.menu-host-popup, .submenu) without
// per-element bindings. RAF coalesces rapid wheel input to one reposition per frame.
export function attachMenuViewportListeners() {
    if (menuViewportListener) { return; }

    let frameScheduled = false;
    menuViewportListener = () => {
        if (frameScheduled) { return; }
        frameScheduled = true;
        requestAnimationFrame(() => {
            frameScheduled = false;
            repositionOpenMenuSubmenus();
        });
    };

    window.addEventListener("scroll", menuViewportListener, true);
    window.addEventListener("resize", menuViewportListener);
}

export function detachMenuViewportListeners() {
    if (!menuViewportListener) { return; }

    window.removeEventListener("scroll", menuViewportListener, true);
    window.removeEventListener("resize", menuViewportListener);
    menuViewportListener = null;
}