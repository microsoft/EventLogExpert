// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

export function registerLogTabBarEvents() {
    const logTabBar = document.querySelector(".log-tab-bar");

    if (!logTabBar) { return; }

    registerLogTabBarScroller(logTabBar);
}

function registerLogTabBarScroller(logTabBar) {
    // A press only becomes a drag-scroll after moving past this many pixels, so pointer jitter during a
    // click does not latch scrolling and suppress the click that activates/toggles a tab.
    const DRAG_THRESHOLD_PX = 5;

    let canDrag, isScrolling = false;
    let startPos, currentPos;

    const preventClick = (e) => {
        e.preventDefault();
        e.stopImmediatePropagation();
    }

    logTabBar.addEventListener("mousedown", (e) => {
        if (e.button !== 0) { return; }

        canDrag = true;
        // Reset the latch at the start of every press so a previous drag that ended outside the bar (its
        // mouseup never ran here) can't leave isScrolling stuck true and suppress this press's click.
        isScrolling = false;

        startPos = e.pageX - logTabBar.offsetLeft;
        currentPos = logTabBar.scrollLeft;
    });
    
    logTabBar.addEventListener("mouseup", (e) => {
        canDrag = false;

        const tabs = logTabBar.getElementsByClassName("tab");

        if (isScrolling) {
            for (let i = 0; i < tabs.length; i++) {
                tabs[i].addEventListener("click", preventClick);
            }
        } else {
            for (let i = 0; i < tabs.length; i++) {
                tabs[i].removeEventListener("click", preventClick);
            }
        }

        isScrolling = false;
    });

    logTabBar.addEventListener("mousemove", (e) => {
        if (!canDrag) { return; }

        const offset = e.pageX - logTabBar.offsetLeft;
        const pos = offset - startPos;

        if (!isScrolling && Math.abs(pos) < DRAG_THRESHOLD_PX) { return; }

        isScrolling = true;

        e.preventDefault();

        logTabBar.scrollLeft = currentPos - pos;
    });

    logTabBar.addEventListener("wheel", (e) => {
        e.preventDefault();

        logTabBar.scrollLeft += e.deltaY;
    });

    logTabBar.addEventListener("mouseleave", () => { canDrag = false; });
}
