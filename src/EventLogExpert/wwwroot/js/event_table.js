window.registerTableEvents = (id) => {
    const table = document.getElementById(id);

    deleteColumnResize(table);
    enableColumnResize(table);

    registerKeyHandlers(table);
};

window.deleteColumnResize = (table) => {
    table.querySelectorAll(".table-divider").forEach(x => x.remove());
};

window.enableColumnResize = (table) => {
    const columns = table.querySelectorAll("th");

    if (columns != null) {
        const createResizableColumn = function(column) {
            let x = 0;
            let w = 0;

            const divider = document.createElement("div");
            divider.classList.add("table-divider");

            column.appendChild(divider);

            const mouseMoveHandler = function(e) {
                const distance = e.clientX - x;

                column.style.width = `${w + distance}px`;
            };

            const mouseUpHandler = function() {
                document.removeEventListener("mousemove", mouseMoveHandler);
                document.removeEventListener("mouseup", mouseUpHandler);

                window.deleteColumnResize(table);
                window.enableColumnResize(table);
            };

            const mouseDownHandler = function(e) {
                x = e.clientX;

                const styles = window.getComputedStyle(column);
                w = parseInt(styles.width, 10);

                document.addEventListener("mousemove", mouseMoveHandler);
                document.addEventListener("mouseup", mouseUpHandler);
            };

            divider.addEventListener("mousedown", mouseDownHandler);
        };

        for (let i = 0; i < columns.length - 1; i++) {
            createResizableColumn(columns[i]);
        }
    }
};

window.registerKeyHandlers = (table) => {
    const selectAdjacentRow = function(direction) {
        const tableRows = table.getElementsByTagName("tr");
        const selectedRow = table.getElementsByClassName("selected")[0];

        for (let i = 0; i < tableRows.length; i++) {
            if (tableRows[i] === selectedRow) {
                tableRows[i + direction].focus();

                break;
            }
        }
    };

    const keyDownHandler = function(e) {
        if (e.key === "ArrowUp") {
            e.preventDefault();
            selectAdjacentRow(-1);
        }

        if (e.key === "ArrowDown") {
            e.preventDefault();
            selectAdjacentRow(+1);
        }
    };

    table.addEventListener("keydown", keyDownHandler);
};

window.scrollToRow = (id, offset) => {
    const table = document.getElementById(id);
    const row = table.getElementsByTagName("tr")[0];

    table.parentNode.scrollTo({top: row.offsetHeight * offset, behavior: "smooth"});
}
