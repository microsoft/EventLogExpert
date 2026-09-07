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
            // Suppress only the drag's own trailing click, then drop the listeners on the next task. Blazor routes
            // activation through a document-level click listener, so a lingering suppressor would also swallow the
            // browser click synthesized by Enter/Space on the native group-header button (which has no mouseup to
            // clear it), breaking the first keyboard activation after a drag-scroll.
            const scrolledTabs = Array.from(tabs);

            for (let i = 0; i < scrolledTabs.length; i++) {
                scrolledTabs[i].addEventListener("click", preventClick);
            }

            setTimeout(() => {
                for (let i = 0; i < scrolledTabs.length; i++) {
                    scrolledTabs[i].removeEventListener("click", preventClick);
                }
            }, 0);
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
