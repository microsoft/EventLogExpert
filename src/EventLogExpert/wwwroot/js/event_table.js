(() => {
    let activeDocumentListeners = [];
    let controller = null;
    let dotNetRef = null;
    let keyboardResizeTimer = null;

    // Attaches a document-level listener tied to the current AbortController
    // signal so dispose() guarantees cleanup even if the caller forgets to
    // untrack. Returns an untrack function that both removes the listener and
    // drops the tracking entry to prevent unbounded growth across drags.
    function trackDocumentListener(event, handler) {
        const entry = { event, handler };
        const options = controller ? { signal: controller.signal } : undefined;
        document.addEventListener(event, handler, options);
        activeDocumentListeners.push(entry);

        return () => {
            document.removeEventListener(event, handler);
            const i = activeDocumentListeners.indexOf(entry);
            if (i !== -1) {
                activeDocumentListeners.splice(i, 1);
            }
        };
    }

    function removeTrackedListeners() {
        for (const { event, handler } of activeDocumentListeners) {
            document.removeEventListener(event, handler);
        }

        activeDocumentListeners = [];
    }

    window.initializeTableEvents = (ref) => {
        window.disposeTableEvents();

        dotNetRef = ref;
        controller = new AbortController();
        const signal = controller.signal;

        const table = document.getElementById("eventTable");

        if (!table) {
            return;
        }

        enableColumnResize(table, signal);
        enableColumnReorder(table, signal);
        registerKeyHandlers(table, signal);
    };

    window.disposeTableEvents = () => {
        if (controller) {
            controller.abort();
            controller = null;
        }

        removeTrackedListeners();

        dotNetRef = null;

        if (keyboardResizeTimer) {
            clearTimeout(keyboardResizeTimer);
            keyboardResizeTimer = null;
        }

        const table = document.getElementById("eventTable");
        if (table) {
            table.querySelectorAll(".table-divider").forEach(x => x.remove());
            // Clear any in-progress drag styling so headers don't remain
            // semi-transparent if dispose runs mid-drag.
            table.querySelectorAll("th.dragging").forEach(x => x.classList.remove("dragging"));
        }

        // Drag indicators are appended to document.body, so clean them up there
        // in case dispose runs mid-drag (before mouseup).
        document.body.querySelectorAll(".drag-indicator").forEach(x => x.remove());
    };

    window.refreshColumnResize = () => {
        const table = document.getElementById("eventTable");

        if (!table || !controller) {
            return;
        }

        table.querySelectorAll(".table-divider").forEach(x => x.remove());
        enableColumnResize(table, controller.signal);
    };

    function getColumnName(th) {
        return th.getAttribute("data-column");
    }

    function enableColumnResize(table, signal) {
        const columns = table.querySelectorAll("th[data-column]");

        for (const column of columns) {
            createResizableColumn(table, column, signal);
        }
    }

    function createResizableColumn(table, column, signal) {
        let startX = 0;
        let startW = 0;
        let untrackMove = null;
        let untrackUp = null;

        const divider = document.createElement("div");
        divider.classList.add("table-divider");
        column.appendChild(divider);
        divider.tabIndex = 0;

        const mouseMoveHandler = function(e) {
            const distance = e.clientX - startX;
            const newWidth = Math.max(30, startW + distance);
            column.style.width = `${newWidth}px`;
        };

        const mouseUpHandler = function() {
            if (untrackMove) { untrackMove(); untrackMove = null; }
            if (untrackUp) { untrackUp(); untrackUp = null; }

            const colName = getColumnName(column);
            const newWidth = parseInt(window.getComputedStyle(column).width, 10);

            if (dotNetRef && colName) {
                // Catch rejection in case the .NET object was disposed mid-drag
                // (component teardown, column set change, etc.).
                dotNetRef.invokeMethodAsync("OnColumnResized", colName, newWidth).catch(() => { });
            }

            // Rebuild dividers after resize
            window.refreshColumnResize();
        };

        const mouseDownHandler = function(e) {
            if (e.button !== 0) {
                return;
            }

            e.stopPropagation();
            startX = e.clientX;
            startW = parseInt(window.getComputedStyle(column).width, 10);

            untrackMove = trackDocumentListener("mousemove", mouseMoveHandler);
            untrackUp = trackDocumentListener("mouseup", mouseUpHandler);
        };

        const keyboardResizeHandler = function(e) {
            const w = parseInt(window.getComputedStyle(column).width, 10);

            if (e.key === "ArrowRight") {
                column.style.width = `${w + 10}px`;
            } else if (e.key === "ArrowLeft") {
                column.style.width = `${Math.max(30, w - 10)}px`;
            } else {
                return;
            }

            // Suppress the WebView's default arrow-key scroll while a focused
            // divider is being keyboard-resized.
            e.preventDefault();
            e.stopPropagation();

            // Debounce keyboard resize persistence
            if (keyboardResizeTimer) {
                clearTimeout(keyboardResizeTimer);
            }

            keyboardResizeTimer = setTimeout(() => {
                    const colName = getColumnName(column);
                    const newWidth = parseInt(window.getComputedStyle(column).width, 10);

                    if (dotNetRef && colName) {
                        dotNetRef.invokeMethodAsync("OnColumnResized", colName, newWidth).catch(() => { });
                    }

                    keyboardResizeTimer = null;
                },
                300);
        };

        divider.addEventListener("mousedown", mouseDownHandler, { signal });
        divider.addEventListener("keydown", keyboardResizeHandler, { signal });
    }

    function enableColumnReorder(table, signal) {
        const headerRow = table.querySelector("thead tr");

        if (!headerRow) {
            return;
        }

        let dragSource = null;
        let dragIndicator = null;
        let pendingTarget = null;
        let pendingInsertAfter = false;

        // Event delegation: single listener on the header row handles all columns.
        // This survives Blazor DOM updates that may recreate th elements.
        headerRow.addEventListener("mousedown",
            function(e) {
                // Only start drag on primary (left) button so right-click for
                // the column context menu isn't intercepted.
                if (e.button !== 0) {
                    return;
                }

                if (e.target.classList.contains("table-divider") ||
                    e.target.closest(".menu-toggle")) {
                    return;
                }

                const th = e.target.closest("th[data-column]");

                if (!th) {
                    return;
                }

                dragSource = th;

                const startX = e.clientX;
                let hasMoved = false;
                let untrackMove = null;
                let untrackUp = null;
                pendingTarget = null;

                const moveHandler = function(e) {
                    const distance = Math.abs(e.clientX - startX);

                    if (distance < 5) {
                        return;
                    }

                    if (!hasMoved) {
                        hasMoved = true;
                        dragSource.classList.add("dragging");
                    }

                    const allHeaders = Array.from(headerRow.querySelectorAll("th[data-column]"));
                    const sourceIndex = allHeaders.indexOf(dragSource);
                    const drop = computeDropInfo(allHeaders, e.clientX, e.clientY, sourceIndex);

                    if (drop) {
                        removeIndicator();
                        pendingTarget = drop.targetColumn;
                        pendingInsertAfter = drop.insertAfter;

                        dragIndicator = document.createElement("div");
                        dragIndicator.classList.add("drag-indicator");

                        const refRect = drop.refRect;

                        dragIndicator.style.position = "fixed";
                        dragIndicator.style.top = `${refRect.top}px`;
                        dragIndicator.style.height = `${refRect.height}px`;
                        dragIndicator.style.width = "2px";
                        dragIndicator.style.backgroundColor = "var(--clr-lightblue)";
                        dragIndicator.style.zIndex = "100";
                        dragIndicator.style.pointerEvents = "none";
                        dragIndicator.style.left = `${drop.indicatorX}px`;

                        document.body.appendChild(dragIndicator);
                    } else {
                        // Either explicit cancel (drop === false, cursor over
                        // source) or no header under cursor (drop === null,
                        // e.g. dragged into the table body). Clear any stale
                        // pendingTarget/indicator so mouseup doesn't act on it.
                        removeIndicator();
                        pendingTarget = null;
                    }
                };

                const upHandler = function() {
                    if (untrackMove) { untrackMove(); untrackMove = null; }
                    if (untrackUp) { untrackUp(); untrackUp = null; }

                    if (dragSource) {
                        dragSource.classList.remove("dragging");
                    }

                    removeIndicator();

                    if (hasMoved && dragSource && pendingTarget) {
                        const sourceColName = getColumnName(dragSource);

                        if (dotNetRef && sourceColName) {
                            // Catch rejection in case the .NET object was disposed
                            // mid-drag (component teardown, column set change, etc.).
                            dotNetRef.invokeMethodAsync("OnColumnReordered", sourceColName, pendingTarget, pendingInsertAfter).catch(() => { });
                        }
                    }

                    dragSource = null;
                    pendingTarget = null;
                };

                untrackMove = trackDocumentListener("mousemove", moveHandler);
                untrackUp = trackDocumentListener("mouseup", upHandler);
            },
            { signal });

        // Returns:
        //   { targetColumn, insertAfter, indicatorX, refRect } — valid drop position
        //   false — cursor is over the source column (cancel)
        //   null — no column found under cursor
        function computeDropInfo(allHeaders, clientX, clientY, sourceIndex) {
            let targetIndex = -1;

            const el = document.elementFromPoint(clientX, clientY);

            if (el) {
                const th = el.closest("th[data-column]");

                if (th) {
                    targetIndex = allHeaders.indexOf(th);
                }
            }

            if (targetIndex === -1) {
                const firstRect = allHeaders[0].getBoundingClientRect();
                const lastRect = allHeaders[allHeaders.length - 1].getBoundingClientRect();

                if (clientX < firstRect.left) {
                    targetIndex = 0;
                } else if (clientX > lastRect.right) {
                    targetIndex = allHeaders.length - 1;
                } else {
                    return null;
                }
            }

            if (targetIndex === sourceIndex) {
                return false;
            }

            const targetTh = allHeaders[targetIndex];
            const targetColumn = getColumnName(targetTh);
            const targetRect = targetTh.getBoundingClientRect();
            const insertAfter = targetIndex > sourceIndex;

            // Indicator at the target's far edge (away from source). Drop inserts
            // the source column on that same far side of target.
            const indicatorX = insertAfter ? targetRect.right : targetRect.left;

            return { targetColumn, insertAfter, indicatorX, refRect: targetRect };
        }

        function removeIndicator() {
            if (dragIndicator) {
                dragIndicator.remove();
                dragIndicator = null;
            }
        }
    }

    function registerKeyHandlers(table, signal) {
        // Suppress the WebView's native arrow/Home/End/PageUp/Down scroll so
        // Blazor's @onkeydown can drive selection without competing scroll.
        // Capture-phase + preventDefault only (never stopPropagation) so the
        // .NET handler still fires.
        const navKeys = new Set([
            "ArrowUp", "ArrowDown", "Home", "End", "PageUp", "PageDown"
        ]);

        const container = table.parentNode;

        if (!container) {
            return;
        }

        container.addEventListener("keydown",
            function(e) {
                // Skip preventDefault when the table has no body rows. With an
                // empty table there's nothing for the .NET keyboard handler
                // to act on, so trapping these keys would only swallow the
                // user's normal scroll/select-all gestures while focus sits
                // on the focusable container.
                if (!table.querySelector("tbody tr")) {
                    return;
                }

                if (navKeys.has(e.key)) {
                    e.preventDefault();
                    return;
                }

                // Ctrl+A: prevent the WebView's native Select-All so the
                // table's own Ctrl+A handler in HandleKeyDown owns the gesture.
                if (e.ctrlKey && (e.key === "a" || e.key === "A")) {
                    e.preventDefault();
                }
            },
            { capture: true, signal });
    }

    window.scrollToRow = (offset) => {
        const table = document.getElementById("eventTable");

        if (!table) {
            return;
        }

        const row = table.getElementsByTagName("tr")[0];

        if (!row) {
            return;
        }

        table.parentNode.scrollTo({
            top: row.offsetHeight * offset - (table.parentNode.offsetHeight / 3),
            behavior: "smooth"
        });
    };

    // Returns the PageUp/PageDown jump size in body rows. Derived from the
    // scroll container's clientHeight and the measured body-row height
    // (falling back to 22px when no body row is measurable yet), then
    // reduced by one row to preserve context above/below after the page
    // jump (mirroring Windows Explorer / VS Code behavior). Returns at
    // least 1 when the table/container exists, or 0 only when the table
    // or its parent container is unavailable.
    window.getEventTablePageSize = () => {
        const table = document.getElementById("eventTable");

        return computePageSize(table);
    };

    function computePageSize(table) {
        if (!table || !table.parentNode) {
            return 0;
        }

        // Sample only body rows so the header row's height (which can differ
        // from data rows) doesn't skew the page-size calculation.
        const row = table.querySelector("tbody tr");
        // Fall back to a default row height (~22px) when no body row has
        // rendered yet so the first PageDown still performs a sensible jump
        // instead of returning 0 and forcing callers to handle an
        // "unmeasured" case.
        const rowHeight = row ? row.getBoundingClientRect().height : 22;

        if (!rowHeight) {
            // Measured a zero-height row (e.g., display:none); use the
            // default row height as a safe approximation.
            return Math.max(1, Math.floor(table.parentNode.clientHeight / 22) - 1);
        }

        // Subtract one row to leave context above/below after a page jump,
        // mirroring Windows Explorer / VS Code behavior.
        return Math.max(1, Math.floor(table.parentNode.clientHeight / rowHeight) - 1);
    }

    // Scrolls the row at the given index into view and focuses it. Targets
    // rows by aria-rowindex (set by Razor as displayed-events index + 2)
    // because Virtualize only renders rows in/near the viewport — indexing
    // into the live `<tbody tr>` NodeList would point at the wrong row
    // once the user has scrolled away from the top. When the row is not
    // yet materialized, we scroll to its approximate position and retry
    // after Virtualize has had a chance to render the new viewport slice.
    window.focusEventTableRow = (index) => {
        const table = document.getElementById("eventTable");

        if (!table) {
            return;
        }

        const container = table.parentNode;
        const ariaRowIndex = index + 2;
        const selector = `tbody tr[aria-rowindex="${ariaRowIndex}"]`;

        const tryFocus = () => {
            const row = table.querySelector(selector);

            if (!row) { return false; }

            // 'nearest' avoids jumping when already on screen.
            row.scrollIntoView({ block: "nearest", behavior: "auto" });
            row.focus({ preventScroll: true });

            return true;
        };

        if (tryFocus()) { return; }

        // Sample only body rows so the header row's height doesn't skew
        // the scroll-target calculation.
        const sampleRow = table.querySelector("tbody tr");
        const rowHeight = sampleRow ? sampleRow.getBoundingClientRect().height : 22;

        if (rowHeight) {
            const target = rowHeight * index - container.clientHeight / 2;
            container.scrollTo({ top: Math.max(0, target), behavior: "auto" });
        }

        if (document.activeElement !== container) {
            container.focus({ preventScroll: true });
        }

        // Two RAFs cover Blazor's render-after-scroll cycle so the
        // virtualized row has time to enter the DOM before we retry.
        requestAnimationFrame(() => requestAnimationFrame(tryFocus));
    };
})();
