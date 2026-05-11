window.registerLogTabBarEvents = () => {
    const logTabBar = document.querySelector(".log-tab-bar");

    if (!logTabBar) { return; }

    registerLogTabBarScroller(logTabBar);
};

window.registerLogTabBarScroller = (logTabBar) => {
    let canDrag, isScrolling = false;
    let startPos, currentPos;

    const preventClick = (e) => {
        e.preventDefault();
        e.stopImmediatePropagation();
    }

    logTabBar.addEventListener("mousedown", (e) => {
        canDrag = true;

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

        isScrolling = true;

        e.preventDefault();

        const offset = e.pageX - logTabBar.offsetLeft;
        const pos = offset - startPos;
        
        logTabBar.scrollLeft = currentPos - pos;
    });

    logTabBar.addEventListener("wheel", (e) => {
        e.preventDefault();

        logTabBar.scrollLeft += e.deltaY;
    });

    logTabBar.addEventListener("mouseleave", () => { canDrag = false; });
}
