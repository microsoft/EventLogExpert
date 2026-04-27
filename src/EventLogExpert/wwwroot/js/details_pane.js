(() => {
    const detailsPaneState = {
        activeDocumentListeners: [],
        controller: null,
        dotNetRef: null
    };

    function trackDetailsDocumentListener(event, handler) {
        const entry = { event, handler };
        const options = detailsPaneState.controller ? { signal: detailsPaneState.controller.signal } : undefined;
        document.addEventListener(event, handler, options);
        detailsPaneState.activeDocumentListeners.push(entry);

        return () => {
            document.removeEventListener(event, handler);
            const i = detailsPaneState.activeDocumentListeners.indexOf(entry);
            if (i !== -1) {
                detailsPaneState.activeDocumentListeners.splice(i, 1);
            }
        };
    }

    window.enableDetailsPaneResizer = (dotNetRef, savedHeight) => {
        window.disposeDetailsPaneResizer();

        const detailsPane = document.querySelector(".details-pane");
        const resizer = detailsPane?.querySelector(".details-resizer");

        if (detailsPane == null || resizer == null) {
            return;
        }

        // Apply persisted height (if any) before user interaction. CSS supplies
        // the default height via var(--details-pane-height, 30%) when no saved
        // value exists. Setting a custom property (rather than inline height)
        // lets the [data-toggle="false"] collapsed-state CSS rule win without
        // needing !important.
        if (savedHeight && savedHeight > 0) {
            const maxHeight = Math.max(60, Math.floor(window.innerHeight * 0.8));
            detailsPane.style.setProperty("--details-pane-height", `${Math.min(savedHeight, maxHeight)}px`);
        }

        detailsPaneState.dotNetRef = dotNetRef;
        detailsPaneState.controller = new AbortController();
        const signal = detailsPaneState.controller.signal;

        let y = 0;
        let h = 0;
        let untrackMove = null;
        let untrackUp = null;

        const mouseMoveHandler = function(e) {
            const distance = e.clientY - y;
            // Match the column-resize minimum (avoids the pane vanishing entirely
            // and being un-grabbable). CSS min-height still applies on top.
            const newHeight = Math.max(30, h - distance);

            detailsPane.style.setProperty("--details-pane-height", `${newHeight}px`);
        };

        const mouseUpHandler = function() {
            if (untrackMove) { untrackMove(); untrackMove = null; }
            if (untrackUp) { untrackUp(); untrackUp = null; }

            const ref = detailsPaneState.dotNetRef;

            if (ref && detailsPane.isConnected) {
                const newHeight = parseInt(window.getComputedStyle(detailsPane).height, 10);
                // Catch rejection in case the .NET object was disposed mid-drag.
                ref.invokeMethodAsync("OnDetailsPaneHeightChanged", newHeight).catch(() => { });
            }
        };

        const mouseDownHandler = function(e) {
            // Only respond to primary (left) button so right-click context menus
            // and middle-click are not intercepted.
            if (e.button !== 0) { return; }

            y = e.clientY;

            const styles = window.getComputedStyle(detailsPane);
            h = parseInt(styles.height, 10);

            untrackMove = trackDetailsDocumentListener("mousemove", mouseMoveHandler);
            untrackUp = trackDetailsDocumentListener("mouseup", mouseUpHandler);
        };

        resizer.addEventListener("mousedown", mouseDownHandler, { signal });
    };

    window.disposeDetailsPaneResizer = () => {
        if (detailsPaneState.controller) {
            detailsPaneState.controller.abort();
            detailsPaneState.controller = null;
        }

        for (const { event, handler } of detailsPaneState.activeDocumentListeners) {
            document.removeEventListener(event, handler);
        }

        detailsPaneState.activeDocumentListeners = [];
        detailsPaneState.dotNetRef = null;
    };
})();
