window.registerTabPaneEvents = () => {
    const tabPane = document.getElementById("tab-pane");

    if (!tabPane) { return; }

    registerTabPaneScroller(tabPane);
};

window.registerTabPaneScroller = (tabPane) => {
    let canDrag, isScrolling = false;
    let startPos, currentPos;

    const preventClick = (e) => {
        e.preventDefault();
        e.stopImmediatePropagation();
    }

    tabPane.addEventListener("mousedown", (e) => {
        canDrag = true;

        startPos = e.pageX - tabPane.offsetLeft;
        currentPos = tabPane.scrollLeft;
    });
    
    tabPane.addEventListener("mouseup", (e) => {
        canDrag = false;

        const tabs = tabPane.getElementsByClassName("tab");

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

    tabPane.addEventListener("mousemove", (e) => {
        if (!canDrag) { return; }

        isScrolling = true;

        e.preventDefault();

        const offset = e.pageX - tabPane.offsetLeft;
        const pos = offset - startPos;
        
        tabPane.scrollLeft = currentPos - pos;
    });

    tabPane.addEventListener("mouseleave", () => { canDrag = false; });
}
